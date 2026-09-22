using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace NekoConverter.Core.Engines.Data;

/// <summary>
/// Чтение и запись XLSX.
///
/// XLSX — это обычный ZIP с XML внутри, ровно как DOCX. Поэтому здесь нет ни одной
/// внешней библиотеки: только System.IO.Compression и System.Xml.Linq.
/// Это тот же приём, что и в DocxParser, и он даёт формат бесплатно по размеру.
///
/// Что поддерживается:
///   чтение  — значения, общие строки (sharedStrings), встроенные строки, числа,
///             логические значения, формулы (берётся закешированный результат);
///   запись  — один лист, встроенные строки, автоширина не считается.
///
/// Что НЕ поддерживается: несколько листов при записи, стили, форматирование,
/// объединённые ячейки, даты как даты (дата читается как число, каковой она в XLSX и является).
///
/// Старый формат .xls (бинарный, до 2007) здесь не читается — для него нужен модуль.
/// </summary>
public static class XlsxConverter
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Читает первый лист книги в таблицу.</summary>
    public static DataTable Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        var sharedStrings = ReadSharedStrings(archive);
        var sheetPath = FindFirstSheetPath(archive);

        var sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidDataException($"Corrupted XLSX: worksheet \"{sheetPath}\" not found.");

        XDocument sheet;
        using (var stream = sheetEntry.Open())
        {
            sheet = XDocument.Load(stream);
        }

        var rows = new List<List<string>>();
        var maxColumns = 0;

        foreach (var row in sheet.Descendants(MainNs + "row"))
        {
            var cells = new List<string>();

            foreach (var cell in row.Elements(MainNs + "c"))
            {
                var columnIndex = ColumnIndexFromReference((string?)cell.Attribute("r"));
                while (cells.Count < columnIndex)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(ReadCellValue(cell, sharedStrings));
            }

            maxColumns = Math.Max(maxColumns, cells.Count);
            rows.Add(cells);
        }

        if (rows.Count == 0)
        {
            return new DataTable([], []);
        }

        // Первая строка считается заголовком — так же, как в CSV.
        var columns = rows[0];
        while (columns.Count < maxColumns)
        {
            columns.Add($"column{columns.Count + 1}");
        }

        var dataRows = new List<IReadOnlyList<string>>();

        foreach (var row in rows.Skip(1))
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }

            dataRows.Add(row.Take(maxColumns).ToList());
        }

        return new DataTable(columns, dataRows);
    }

    /// <summary>Записывает таблицу как книгу с одним листом.</summary>
    public static void Write(DataTable table, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Пишем во временный файл и подменяем: иначе при сбое останется битый архив.
        var temporary = fullPath + ".tmp";

        using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypesXml());
            AddEntry(archive, "_rels/.rels", RootRelsXml());
            AddEntry(archive, "xl/workbook.xml", WorkbookXml());
            AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AddEntry(archive, "xl/styles.xml", StylesXml());
            AddEntry(archive, "xl/worksheets/sheet1.xml", SheetXml(table));
        }

        File.Move(temporary, fullPath, overwrite: true);
    }

    // ─────────────────────────────── Чтение ───────────────────────────────

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream);
        }

        return document.Descendants(MainNs + "si")
            .Select(item => string.Concat(item.Descendants(MainNs + "t").Select(t => t.Value)))
            .ToList();
    }

    /// <summary>
    /// Путь к первому листу берём из связей книги, а не угадываем «sheet1.xml»:
    /// Excel нумерует файлы листов в порядке создания, и они не обязаны совпадать с порядком вкладок.
    /// </summary>
    private static string FindFirstSheetPath(ZipArchive archive)
    {
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is not null)
        {
            XDocument rels;
            using (var stream = relsEntry.Open())
            {
                rels = XDocument.Load(stream);
            }

            var first = rels.Root?
                .Elements(PackageRelNs + "Relationship")
                .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true);

            if (first is not null)
            {
                var target = (string?)first.Attribute("Target");
                if (!string.IsNullOrEmpty(target))
                {
                    return target.StartsWith('/')
                        ? target.TrimStart('/')
                        : "xl/" + target.TrimStart('.', '/');
                }
            }
        }

        return "xl/worksheets/sheet1.xml";
    }

    private static string ReadCellValue(XElement cell, List<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");

        // Формула: берём закешированное значение, если оно есть.
        var valueElement = cell.Element(MainNs + "v");
        var inlineElement = cell.Element(MainNs + "is");

        if (type == "inlineStr" && inlineElement is not null)
        {
            return string.Concat(inlineElement.Descendants(MainNs + "t").Select(t => t.Value));
        }

        var raw = valueElement?.Value ?? string.Empty;

        return type switch
        {
            "s" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                   && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty,
            "b" => raw == "1" ? "true" : "false",
            _ => raw,
        };
    }

    /// <summary>«C7» → 2 (индекс колонки с нуля).</summary>
    private static int ColumnIndexFromReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var index = 0;

        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                index = (index * 26) + (ch - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return Math.Max(0, index - 1);
    }

    /// <summary>0 → «A», 25 → «Z», 26 → «AA».</summary>
    private static string ColumnName(int index)
    {
        var name = new StringBuilder();
        var value = index + 1;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            name.Insert(0, (char)('A' + remainder));
            value = (value - 1) / 26;
        }

        return name.ToString();
    }

    // ─────────────────────────────── Запись ───────────────────────────────

    private static void AddEntry(ZipArchive archive, string name, XDocument content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        // Объявление пишем вручную: StreamWriter не умеет отдавать XDocument как есть.
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        writer.Write(content.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument ContentTypesXml() => new(
        new XElement(ContentTypesNs + "Types",
            new XElement(ContentTypesNs + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypesNs + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));

    private static XDocument RootRelsXml() => new(
        new XElement(PackageRelNs + "Relationships",
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    private static XDocument WorkbookXml() => new(
        new XElement(MainNs + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", RelNs.NamespaceName),
            new XElement(MainNs + "sheets",
                new XElement(MainNs + "sheet",
                    new XAttribute("name", "Sheet1"),
                    new XAttribute("sheetId", "1"),
                    new XAttribute(RelNs + "id", "rId1")))));

    private static XDocument WorkbookRelsXml() => new(
        new XElement(PackageRelNs + "Relationships",
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rId2"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml"))));

    /// <summary>Минимальный styles.xml: Excel без него считает книгу повреждённой.</summary>
    private static XDocument StylesXml() => new(
        new XElement(MainNs + "styleSheet",
            new XElement(MainNs + "fonts", new XAttribute("count", "1"),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "sz", new XAttribute("val", "11")),
                    new XElement(MainNs + "name", new XAttribute("val", "Calibri")))),
            new XElement(MainNs + "fills", new XAttribute("count", "1"),
                new XElement(MainNs + "fill",
                    new XElement(MainNs + "patternFill", new XAttribute("patternType", "none")))),
            new XElement(MainNs + "borders", new XAttribute("count", "1"), new XElement(MainNs + "border")),
            new XElement(MainNs + "cellStyleXfs", new XAttribute("count", "1"),
                new XElement(MainNs + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"))),
            new XElement(MainNs + "cellXfs", new XAttribute("count", "1"),
                new XElement(MainNs + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"),
                    new XAttribute("xfId", "0")))));

    private static XDocument SheetXml(DataTable table)
    {
        var rows = new List<XElement>();

        rows.Add(BuildRow(0, table.Columns));
        for (var i = 0; i < table.Rows.Count; i++)
        {
            rows.Add(BuildRow(i + 1, table.Rows[i]));
        }

        return new XDocument(
            new XElement(MainNs + "worksheet",
                new XElement(MainNs + "sheetData", rows)));
    }

    private static XElement BuildRow(int rowIndex, IReadOnlyList<string> values)
    {
        var cells = new List<XElement>();

        for (var column = 0; column < values.Count; column++)
        {
            var reference = ColumnName(column) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
            var value = values[column] ?? string.Empty;

            // Встроенные строки вместо sharedStrings: для одного листа это проще,
            // а Excel и Numbers читают такой файл без замечаний.
            cells.Add(new XElement(MainNs + "c",
                new XAttribute("r", reference),
                new XAttribute("t", "inlineStr"),
                new XElement(MainNs + "is",
                    new XElement(MainNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))));
        }

        return new XElement(MainNs + "row",
            new XAttribute("r", (rowIndex + 1).ToString(CultureInfo.InvariantCulture)),
            cells);
    }
}
