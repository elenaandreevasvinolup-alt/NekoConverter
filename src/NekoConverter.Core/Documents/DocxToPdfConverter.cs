using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NekoConverter.Core.Documents;

/// <param name="MarginPt">Поля страницы в пунктах. 72 pt = 1 дюйм ≈ 2.54 см.</param>
/// <param name="BaseFontPt">Базовый размер шрифта, если в документе он не задан.</param>
public sealed record DocxToPdfOptions(double MarginPt = 72, double BaseFontPt = 11);

/// <summary>
/// Рендер DOCX в PDF с низкой точностью вёрстки (первая версия).
///
/// ЧЕСТНО О ГРАНИЦАХ: это НЕ полноценный движок вёрстки Word. Поддерживаются абзацы,
/// заголовки, жирный/курсив/подчёркивание, простые таблицы и картинки.
/// НЕ поддерживаются: колонки, обтекание текстом, текстовые поля, сноски, колонтитулы,
/// наследование стилей, объединение ячеек. Сложная вёрстка получится "плоской".
/// Для высокой точности нужен внешний движок (LibreOffice/Word) — см. план развития.
/// </summary>
public static class DocxToPdfConverter
{
    // A4 в пунктах: 210 x 297 мм.
    private const double PageWidthPt = 595.276;
    private const double PageHeightPt = 841.89;

    public static void ConvertFile(string docxPath, string pdfPath, DocxToPdfOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        var pdf = Convert(File.ReadAllBytes(docxPath), options);

        var fullOutput = Path.GetFullPath(pdfPath);
        var dir = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(fullOutput, pdf);
    }

    public static byte[] Convert(byte[] docxBytes, DocxToPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(docxBytes);
        return Convert(DocxParser.Parse(docxBytes), options);
    }

    public static byte[] Convert(DocxDocument document, DocxToPdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Резолвер шрифтов должен быть установлен до создания первого XFont.
        SystemCjkFontResolver.EnsureInitialized();

        using var renderer = new Renderer(options ?? new DocxToPdfOptions());

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    renderer.DrawParagraph(paragraph);
                    break;
                case TableBlock table:
                    renderer.DrawTable(table);
                    break;
                case ImageBlock image:
                    renderer.DrawImage(image);
                    break;
            }
        }

        return renderer.Finish();
    }

    private sealed class Renderer : IDisposable
    {
        private readonly PdfDocument _document = new();
        private readonly DocxToPdfOptions _options;
        private readonly Dictionary<(double Size, bool Bold, bool Italic), XFont> _fontCache = [];
        private readonly List<MemoryStream> _imageStreams = [];

        private XGraphics? _graphics;
        private double _y;

        public Renderer(DocxToPdfOptions options)
        {
            _options = options;
            StartPage();
        }

        private double ContentWidth => PageWidthPt - (2 * _options.MarginPt);

        private double BottomLimit => PageHeightPt - _options.MarginPt;

        public void Dispose()
        {
            _graphics?.Dispose();
            foreach (var stream in _imageStreams)
            {
                stream.Dispose();
            }

            _document.Dispose();
        }

        public byte[] Finish()
        {
            EndPage();

            using var buffer = new MemoryStream();
            _document.Save(buffer, false);
            return buffer.ToArray();
        }

        private void StartPage()
        {
            var page = _document.AddPage();
            page.Size = PageSize.A4;
            _graphics = XGraphics.FromPdfPage(page);
            _y = _options.MarginPt;
        }

        private void EndPage()
        {
            _graphics?.Dispose();
            _graphics = null;
        }

        private void EnsureSpace(double height)
        {
            if (_y + height > BottomLimit)
            {
                EndPage();
                StartPage();
            }
        }

        private XFont Font(double sizePt, bool bold, bool italic)
        {
            var key = (Math.Round(sizePt, 1), bold, italic);
            if (_fontCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var style = (bold, italic) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular,
            };

            var font = new XFont(
                SystemCjkFontResolver.FaceName,
                key.Item1,
                style,
                new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));

            _fontCache[key] = font;
            return font;
        }

        /// <summary>Коэффициент размера и вертикальные отступы для каждого стиля абзаца.</summary>
        private static (double Scale, double SpaceBeforePt, double SpaceAfterPt) MetricsFor(ParagraphStyle style) => style switch
        {
            ParagraphStyle.Title => (2.00, 0, 18),
            ParagraphStyle.Heading1 => (1.45, 18, 10),
            ParagraphStyle.Heading2 => (1.27, 14, 8),
            ParagraphStyle.Heading3 => (1.14, 10, 6),
            ParagraphStyle.Heading4 => (1.05, 8, 4),
            _ => (1.00, 0, 6),
        };

        public void DrawParagraph(ParagraphBlock block)
        {
            var (scale, spaceBefore, spaceAfter) = MetricsFor(block.Style);
            var isHeading = block.Style is ParagraphStyle.Title or ParagraphStyle.Heading1
                or ParagraphStyle.Heading2 or ParagraphStyle.Heading3 or ParagraphStyle.Heading4;

            var rawSize = block.Runs.Count > 0 ? block.Runs.Max(r => r.SizePt) : _options.BaseFontPt;
            var size = Math.Clamp(rawSize * scale, 6, 48);

            var runs = new List<DocxRun>();
            if (block.IsListItem)
            {
                runs.Add(new DocxRun("• ", SizePt: size));
            }

            runs.AddRange(block.Runs.Select(r => r with { SizePt = size, Bold = r.Bold || isHeading }));

            _y += spaceBefore;

            var lineHeight = size * 1.45;
            var lines = WrapLines(runs, ContentWidth);

            foreach (var line in lines)
            {
                EnsureSpace(lineHeight);

                var x = _options.MarginPt;
                foreach (var (run, text) in line)
                {
                    var font = Font(size, run.Bold, run.Italic);
                    var width = _graphics!.MeasureString(text, font).Width;

                    _graphics.DrawString(
                        text,
                        font,
                        XBrushes.Black,
                        new XRect(x, _y, width + 1, lineHeight),
                        XStringFormats.TopLeft);

                    if (run.Underline)
                    {
                        var underlineY = _y + (lineHeight * 0.88);
                        _graphics.DrawLine(new XPen(XColor.FromArgb(0, 0, 0), 0.6), x, underlineY, x + width, underlineY);
                    }

                    x += width;
                }

                _y += lineHeight;
            }

            _y += spaceAfter;
        }

        public void DrawTable(TableBlock block)
        {
            if (block.Rows.Count == 0)
            {
                return;
            }

            var columnCount = block.Rows.Max(r => r.Count);
            if (columnCount == 0)
            {
                return;
            }

            const double padding = 4;
            const double cellLineFactor = 1.35;
            var columnWidth = ContentWidth / columnCount;
            var borderPen = new XPen(XColor.FromArgb(0x99, 0x99, 0x99), 0.5);

            foreach (var row in block.Rows)
            {
                var cellLines = new List<List<List<(DocxRun Run, string Text)>>>();
                var cellFontSizes = new List<double>();
                double rowHeight = 0;

                for (var c = 0; c < columnCount; c++)
                {
                    var cellRuns = c < row.Count ? row[c].ToList() : [];
                    if (cellRuns.Count == 0)
                    {
                        cellRuns.Add(new DocxRun(string.Empty, SizePt: _options.BaseFontPt));
                    }

                    var cellSize = Math.Clamp(cellRuns.Max(r => r.SizePt), 6, 24);
                    var lines = WrapLines(cellRuns, columnWidth - (2 * padding));

                    cellLines.Add(lines);
                    cellFontSizes.Add(cellSize);

                    var cellHeight = (lines.Count * cellSize * cellLineFactor) + (2 * padding);
                    rowHeight = Math.Max(rowHeight, cellHeight);
                }

                EnsureSpace(rowHeight);

                var rowTop = _y;

                for (var c = 0; c < columnCount; c++)
                {
                    var cellX = _options.MarginPt + (c * columnWidth);
                    var cellRect = new XRect(cellX, rowTop, columnWidth, rowHeight);

                    _graphics!.DrawRectangle(borderPen, cellRect);

                    var size = cellFontSizes[c];
                    var lineHeight = size * cellLineFactor;
                    var textY = rowTop + padding;

                    foreach (var line in cellLines[c])
                    {
                        var x = cellX + padding;
                        foreach (var (run, text) in line)
                        {
                            var font = Font(size, run.Bold, run.Italic);
                            var width = _graphics.MeasureString(text, font).Width;
                            _graphics.DrawString(
                                text,
                                font,
                                XBrushes.Black,
                                new XRect(x, textY, width + 1, lineHeight),
                                XStringFormats.TopLeft);
                            x += width;
                        }

                        textY += lineHeight;
                    }
                }

                _y = rowTop + rowHeight;
            }

            _y += 8;
        }

        public void DrawImage(ImageBlock block)
        {
            if (block.Data.Length == 0)
            {
                return;
            }

            try
            {
                var stream = new MemoryStream(block.Data);
                _imageStreams.Add(stream);
                var image = XImage.FromStream(stream);

                var width = block.WidthPt;
                var height = block.HeightPt;

                if (width <= 0 || height <= 0)
                {
                    // В DOCX нет wp:extent — считаем, что картинка задана в 96 dpi.
                    width = image.PixelWidth * 72.0 / 96.0;
                    height = image.PixelHeight * 72.0 / 96.0;
                }

                if (width > ContentWidth)
                {
                    var k = ContentWidth / width;
                    width *= k;
                    height *= k;
                }

                EnsureSpace(height);
                _graphics!.DrawImage(image, new XRect(_options.MarginPt, _y, width, height));
                _y += height + 6;
            }
            catch
            {
                // Формат картинки может не поддерживаться PDFsharp — пропускаем, не роняя конвертацию.
            }
        }

        /// <summary>
        /// Разбивает фрагменты на строки. Иероглифы переносятся по любому символу,
        /// латинские слова — только по пробелам.
        /// </summary>
        private List<List<(DocxRun Run, string Text)>> WrapLines(IReadOnlyList<DocxRun> runs, double maxWidth)
        {
            var lines = new List<List<(DocxRun Run, string Text)>>();
            var current = new List<(DocxRun Run, string Text)>();
            double currentWidth = 0;

            void PushLine()
            {
                lines.Add(current);
                current = [];
                currentWidth = 0;
            }

            void Append(DocxRun run, string text)
            {
                if (text.Length == 0)
                {
                    return;
                }

                if (current.Count > 0 && current[^1].Run == run)
                {
                    current[^1] = (run, current[^1].Text + text);
                }
                else
                {
                    current.Add((run, text));
                }
            }

            foreach (var run in runs)
            {
                var segments = run.Text.Split('\n');
                for (var i = 0; i < segments.Length; i++)
                {
                    if (i > 0)
                    {
                        PushLine();
                    }

                    var font = Font(run.SizePt, run.Bold, run.Italic);

                    foreach (var token in Tokenize(segments[i]))
                    {
                        foreach (var piece in FitToWidth(token, font, maxWidth))
                        {
                            var width = _graphics!.MeasureString(piece, font).Width;

                            if (currentWidth + width > maxWidth && current.Count > 0)
                            {
                                PushLine();
                            }

                            Append(run, piece);
                            currentWidth += width;
                        }
                    }
                }
            }

            if (current.Count > 0)
            {
                lines.Add(current);
            }

            if (lines.Count == 0)
            {
                lines.Add([]);
            }

            return lines;
        }

        /// <summary>Режет слишком длинный токен (например URL) по символам, чтобы он влез в строку.</summary>
        private IEnumerable<string> FitToWidth(string token, XFont font, double maxWidth)
        {
            if (_graphics!.MeasureString(token, font).Width <= maxWidth)
            {
                yield return token;
                yield break;
            }

            var buffer = new StringBuilder();
            foreach (var ch in token)
            {
                var candidate = buffer.ToString() + ch;
                if (buffer.Length > 0 && _graphics.MeasureString(candidate, font).Width > maxWidth)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
            }
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            var buffer = new StringBuilder();

            foreach (var ch in text)
            {
                if (IsBreakable(ch))
                {
                    if (buffer.Length > 0)
                    {
                        yield return buffer.ToString();
                        buffer.Clear();
                    }

                    yield return ch.ToString();
                }
                else
                {
                    buffer.Append(ch);
                }
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
            }
        }

        /// <summary>Символ, после которого можно переносить строку.</summary>
        private static bool IsBreakable(char ch) =>
            ch >= 0x2E80 || ch is ' ' or '\t';
    }
}
