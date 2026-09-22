using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace NekoConverter.Core.Documents;

/// <summary>
/// Разбор DOCX на плоский список блоков.
/// DOCX — это обычный ZIP с XML внутри, поэтому никакие тяжёлые библиотеки не нужны:
/// используется только System.IO.Compression + System.Xml.Linq.
///
/// Что поддерживается: абзацы, заголовки, жирный/курсив/подчёркивание, размер шрифта,
/// маркированные абзацы, простые таблицы, встроенные изображения (DrawingML и VML).
/// Что НЕ поддерживается: объединение ячеек, колонки, текстовые поля, сноски,
/// колонтитулы, наследование стилей из styles.xml (кроме размера по умолчанию).
/// </summary>
public static class DocxParser
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace REL = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>1 пункт = 12700 EMU (английских метрических единиц).</summary>
    private const double EmuPerPoint = 12700.0;

    public static DocxDocument Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static DocxDocument Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Parse(stream);
    }

    public static DocxDocument Parse(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return Parse(zip);
    }

    public static DocxDocument Parse(ZipArchive zip)
    {
        var documentEntry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("Not a DOCX: the archive has no word/document.xml");

        XDocument document;
        using (var s = documentEntry.Open())
        {
            document = XDocument.Load(s);
        }

        var relationships = LoadRelationships(zip);
        var defaultSizePt = ReadDefaultFontSize(zip);

        var body = document.Root?.Element(W + "body")
            ?? throw new InvalidDataException("Corrupted DOCX: the w:body element is missing");

        var blocks = new List<DocxBlock>();

        foreach (var child in body.Elements())
        {
            if (child.Name == W + "p")
            {
                ParseParagraph(child, blocks, relationships, zip, defaultSizePt);
            }
            else if (child.Name == W + "tbl")
            {
                blocks.Add(ParseTable(child, defaultSizePt));
            }
        }

        return new DocxDocument(blocks);
    }

    /// <summary>Карта rId -> путь внутри архива (например rId4 -> word/media/image1.png).</summary>
    private static Dictionary<string, string> LoadRelationships(ZipArchive zip)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        var entry = zip.GetEntry("word/_rels/document.xml.rels");
        if (entry is null)
        {
            return map;
        }

        XDocument rels;
        using (var s = entry.Open())
        {
            rels = XDocument.Load(s);
        }

        foreach (var rel in rels.Root?.Elements(REL + "Relationship") ?? [])
        {
            var id = (string?)rel.Attribute("Id");
            var target = (string?)rel.Attribute("Target");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
            {
                continue;
            }

            // Цели бывают относительные ("media/image1.png") и абсолютные ("/word/media/...").
            map[id] = target.StartsWith('/')
                ? target.TrimStart('/')
                : "word/" + target;
        }

        return map;
    }

    /// <summary>
    /// Размер шрифта по умолчанию из styles.xml (w:docDefaults/w:rPrDefault/w:rPr/w:sz).
    /// В DOCX размер хранится в полупунктах, поэтому делим на 2.
    /// </summary>
    private static double ReadDefaultFontSize(ZipArchive zip)
    {
        const double fallback = 11.0;

        var entry = zip.GetEntry("word/styles.xml");
        if (entry is null)
        {
            return fallback;
        }

        try
        {
            XDocument styles;
            using (var s = entry.Open())
            {
                styles = XDocument.Load(s);
            }

            var sz = styles.Root?
                .Element(W + "docDefaults")?
                .Element(W + "rPrDefault")?
                .Element(W + "rPr")?
                .Element(W + "sz");

            if (sz is not null && double.TryParse((string?)sz.Attribute(W + "val"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) && halfPoints > 0)
            {
                return halfPoints / 2.0;
            }
        }
        catch
        {
            // Битый styles.xml не должен ронять конвертацию — берём значение по умолчанию.
        }

        return fallback;
    }

    private static void ParseParagraph(
        XElement paragraph,
        List<DocxBlock> blocks,
        Dictionary<string, string> relationships,
        ZipArchive zip,
        double defaultSizePt)
    {
        var style = DetectStyle(paragraph);
        var isListItem = paragraph.Element(W + "pPr")?.Element(W + "numPr") is not null;
        var pending = new List<DocxRun>();

        void Flush()
        {
            if (pending.Count > 0)
            {
                blocks.Add(new ParagraphBlock(pending.ToList(), style, isListItem));
                pending.Clear();
            }
        }

        foreach (var element in EnumerateRunContainers(paragraph))
        {
            if (element.Name != W + "r")
            {
                continue;
            }

            var image = TryParseImage(element, relationships, zip);
            if (image is not null)
            {
                // Изображение разрывает абзац: сначала сбрасываем накопленный текст.
                Flush();
                blocks.Add(image);
                continue;
            }

            var run = ParseRun(element, defaultSizePt);
            if (run is not null && run.Text.Length > 0)
            {
                pending.Add(run);
            }
        }

        Flush();
    }

    /// <summary>
    /// Возвращает w:r, лежащие прямо в абзаце или внутри w:hyperlink.
    /// Специально не используем Descendants, чтобы не затянуть текст из текстовых полей.
    /// </summary>
    private static IEnumerable<XElement> EnumerateRunContainers(XElement paragraph)
    {
        foreach (var child in paragraph.Elements())
        {
            if (child.Name == W + "r")
            {
                yield return child;
            }
            else if (child.Name == W + "hyperlink")
            {
                foreach (var run in child.Elements(W + "r"))
                {
                    yield return run;
                }
            }
        }
    }

    private static DocxRun? ParseRun(XElement run, double defaultSizePt)
    {
        var props = run.Element(W + "rPr");
        var bold = props?.Element(W + "b") is not null;
        var italic = props?.Element(W + "i") is not null;
        var underline = props?.Element(W + "u") is not null;

        var sizePt = defaultSizePt;
        var sz = props?.Element(W + "sz");
        if (sz is not null && double.TryParse((string?)sz.Attribute(W + "val"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) && halfPoints > 0)
        {
            sizePt = halfPoints / 2.0;
        }

        var text = new System.Text.StringBuilder();

        foreach (var node in run.Elements())
        {
            if (node.Name == W + "t")
            {
                text.Append(node.Value);
            }
            else if (node.Name == W + "tab")
            {
                text.Append('\t');
            }
            else if (node.Name == W + "br")
            {
                text.Append('\n');
            }
        }

        if (text.Length == 0)
        {
            return null;
        }

        return new DocxRun(text.ToString(), bold, italic, underline, sizePt);
    }

    private static ParagraphStyle DetectStyle(XElement paragraph)
    {
        var props = paragraph.Element(W + "pPr");

        // w:outlineLvl — самый надёжный признак заголовка, работает при любой локализации Word.
        var outline = props?.Element(W + "outlineLvl");
        if (outline is not null &&
            int.TryParse((string?)outline.Attribute(W + "val"), out var level))
        {
            return LevelToStyle(level + 1);
        }

        var raw = ((string?)props?.Element(W + "pStyle")?.Attribute(W + "val") ?? string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        if (raw is "title")
        {
            return ParagraphStyle.Title;
        }

        // "Heading1" / "heading 1" / "标题1" / "Заголовок1"
        foreach (var prefix in new[] { "heading", "标题" })
        {
            if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var digits = new string(raw[prefix.Length..].Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var headingLevel))
            {
                return LevelToStyle(headingLevel);
            }
        }

        return ParagraphStyle.Body;
    }

    /// <summary>Уровень заголовка (1..6) -> стиль абзаца. Глубже 4-го уровня всё схлопывается в Heading4.</summary>
    private static ParagraphStyle LevelToStyle(int level) => level switch
    {
        1 => ParagraphStyle.Heading1,
        2 => ParagraphStyle.Heading2,
        3 => ParagraphStyle.Heading3,
        _ => ParagraphStyle.Heading4,
    };

    private static TableBlock ParseTable(XElement table, double defaultSizePt)
    {
        var rows = new List<IReadOnlyList<IReadOnlyList<DocxRun>>>();

        foreach (var row in table.Elements(W + "tr"))
        {
            var cells = new List<IReadOnlyList<DocxRun>>();

            foreach (var cell in row.Elements(W + "tc"))
            {
                var runs = new List<DocxRun>();

                foreach (var paragraph in cell.Descendants(W + "p"))
                {
                    foreach (var run in EnumerateRunContainers(paragraph))
                    {
                        var parsed = ParseRun(run, defaultSizePt);
                        if (parsed is not null)
                        {
                            runs.Add(parsed);
                        }
                    }

                    // Абзацы внутри ячейки разделяем переводом строки.
                    runs.Add(new DocxRun("\n", SizePt: defaultSizePt));
                }

                if (runs.Count > 0 && runs[^1].Text == "\n")
                {
                    runs.RemoveAt(runs.Count - 1);
                }

                cells.Add(runs);
            }

            rows.Add(cells);
        }

        return new TableBlock(rows);
    }

    private static ImageBlock? TryParseImage(
        XElement run,
        Dictionary<string, string> relationships,
        ZipArchive zip)
    {
        long cx = 0;
        long cy = 0;

        var extent = run.Descendants(WP + "extent").FirstOrDefault();
        if (extent is not null)
        {
            _ = long.TryParse((string?)extent.Attribute("cx"), out cx);
            _ = long.TryParse((string?)extent.Attribute("cy"), out cy);
        }

        // Современный путь: DrawingML.
        string? relationshipId = (string?)run.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed");

        // Старый путь: VML (картинки из Word 2003 и старше, а также вставленные как "рисунок").
        if (relationshipId is null)
        {
            var imagedata = run.Descendants(V + "imagedata").FirstOrDefault();
            relationshipId = (string?)imagedata?.Attribute(R + "id");

            if (relationshipId is not null && cx == 0 && imagedata?.Parent is { } shape)
            {
                ParseVmlStyle((string?)shape.Attribute("style"), out cx, out cy);
            }
        }

        if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var target))
        {
            return null;
        }

        var entry = zip.GetEntry(target);
        if (entry is null)
        {
            return null;
        }

        byte[] data;
        using (var s = entry.Open())
        using (var buffer = new MemoryStream())
        {
            s.CopyTo(buffer);
            data = buffer.ToArray();
        }

        return new ImageBlock(data, cx / EmuPerPoint, cy / EmuPerPoint);
    }

    /// <summary>
    /// Разбирает style="width:120pt;height:80pt" у VML-фигуры.
    /// Значения хранятся в пунктах, поэтому EMU не нужны.
    /// </summary>
    private static void ParseVmlStyle(string? style, out long cx, out long cy)
    {
        cx = 0;
        cy = 0;

        if (string.IsNullOrWhiteSpace(style))
        {
            return;
        }

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0].Trim().ToLowerInvariant();
            var value = kv[1].Trim().TrimEnd('p', 't', 'x');

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
            {
                continue;
            }

            if (key == "width")
            {
                cx = (long)(pt * EmuPerPoint);
            }
            else if (key == "height")
            {
                cy = (long)(pt * EmuPerPoint);
            }
        }
    }
}
