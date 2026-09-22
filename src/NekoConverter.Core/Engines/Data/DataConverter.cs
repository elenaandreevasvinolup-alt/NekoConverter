using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace NekoConverter.Core.Engines.Data;

/// <summary>Табличное представление: CSV, TSV, JSON и XML приводятся к нему и обратно.</summary>
public sealed record DataTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Конвертация структурированных данных. Написано вручную поверх встроенных
/// System.Text.Json и System.Xml.Linq, поэтому размер и зависимости нулевые.
///
/// Все четыре формата сводятся к одной таблице: строки и колонки.
/// CSV/TSV → таблица, JSON-массив объектов → таблица, XML со повторяющимися узлами → таблица.
/// Обратно — в любой из четырёх.
///
/// Ограничение: вложенные структуры (объект внутри объекта) при переводе в таблицу
/// сериализуются в строку JSON. Это ожидаемо: таблица не умеет в иерархию.
/// </summary>
public static class DataConverter
{
    public static readonly string[] SupportedFormats = ["csv", "tsv", "json", "xml", "xlsx", "yaml"];

    public static void ConvertFile(string inputPath, string outputPath, string targetFormatId)
    {
        var sourceFormat = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var targetFormat = targetFormatId.TrimStart('.').ToLowerInvariant();

        var table = ReadFile(inputPath, sourceFormat);
        WriteFile(table, outputPath, targetFormat);
    }

    /// <summary>Читает источник в таблицу. XLSX — бинарный архив, остальные форматы текстовые.</summary>
    public static DataTable ReadFile(string path, string format) => format switch
    {
        "xlsx" or "xlsm" => XlsxConverter.Read(path),
        "yaml" or "yml" => YamlConverter.Read(File.ReadAllText(path)),
        _ => Read(File.ReadAllText(path), format),
    };

    public static void WriteFile(DataTable table, string path, string format)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (format is "xlsx" or "xlsm")
        {
            XlsxConverter.Write(table, fullPath);
            return;
        }

        if (format is "yaml" or "yml")
        {
            File.WriteAllText(fullPath, YamlConverter.Write(table), new UTF8Encoding(false));
            return;
        }

        File.WriteAllText(fullPath, Write(table, format), new UTF8Encoding(false));
    }

    public static DataTable Read(string text, string format) => format switch
    {
        "csv" => ReadSeparated(text, ','),
        "tsv" => ReadSeparated(text, '\t'),
        "json" => ReadJson(text),
        "xml" => ReadXml(text),
        _ => throw new NotSupportedException($"Reading the \"{format}\" data format is not supported."),
    };

    public static string Write(DataTable table, string format) => format switch
    {
        "csv" => WriteSeparated(table, ','),
        "tsv" => WriteSeparated(table, '\t'),
        "json" => WriteJson(table),
        "xml" => WriteXml(table),
        _ => throw new NotSupportedException($"Writing the \"{format}\" data format is not supported."),
    };

    // ─────────────────────────────── CSV / TSV ───────────────────────────────

    private static DataTable ReadSeparated(string text, char delimiter)
    {
        var records = ParseSeparated(text, delimiter);
        if (records.Count == 0)
        {
            return new DataTable([], []);
        }

        var columns = records[0];
        var rows = new List<IReadOnlyList<string>>();

        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];

            // Строку короче заголовка дополняем, длиннее — обрезаем: таблица прямоугольная.
            if (record.Count < columns.Count)
            {
                var padded = new List<string>(record);
                while (padded.Count < columns.Count)
                {
                    padded.Add(string.Empty);
                }

                record = padded;
            }
            else if (record.Count > columns.Count)
            {
                record = record.Take(columns.Count).ToList();
            }

            rows.Add(record);
        }

        return new DataTable(columns, rows);
    }

    /// <summary>
    /// Разбор CSV по RFC 4180: кавычки, удвоенные кавычки внутри поля,
    /// перевод строки внутри поля.
    /// </summary>
    private static List<List<string>> ParseSeparated(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;

                case '\r':
                    break;

                case '\n':
                    current.Add(field.ToString());
                    field.Clear();
                    records.Add(current);
                    current = [];
                    break;

                default:
                    if (ch == delimiter)
                    {
                        current.Add(field.ToString());
                        field.Clear();
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            records.Add(current);
        }

        // Пустые хвостовые записи от финального перевода строки не нужны.
        return records.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Length > 0)).ToList();
    }

    private static string WriteSeparated(DataTable table, char delimiter)
    {
        var builder = new StringBuilder();

        builder.Append(string.Join(delimiter, table.Columns.Select(c => Escape(c, delimiter)))).Append('\n');

        foreach (var row in table.Rows)
        {
            builder.Append(string.Join(delimiter, row.Select(v => Escape(v, delimiter)))).Append('\n');
        }

        return builder.ToString();
    }

    private static string Escape(string value, char delimiter)
    {
        if (value.IndexOfAny([delimiter, '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    // ─────────────────────────────── JSON ───────────────────────────────

    private static DataTable ReadJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("JSON 转表格需要顶层是数组，例如 [{\"a\":1},{\"a\":2}]。");
        }

        var columns = new List<string>();
        var objects = new List<JsonElement>();

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            objects.Add(element);

            foreach (var property in element.EnumerateObject())
            {
                if (!columns.Contains(property.Name))
                {
                    columns.Add(property.Name);
                }
            }
        }

        var rows = new List<IReadOnlyList<string>>();

        foreach (var element in objects)
        {
            var row = new List<string>(columns.Count);

            foreach (var column in columns)
            {
                row.Add(element.TryGetProperty(column, out var value) ? Stringify(value) : string.Empty);
            }

            rows.Add(row);
        }

        return new DataTable(columns, rows);
    }

    private static string Stringify(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        // Объект или массив в таблицу не влезает — оставляем как строку JSON.
        _ => value.GetRawText(),
    };

    /// <summary>
    /// Пишем JSON вручную, без JsonSerializer.
    /// Причина: в Native AOT рефлексивная сериализация отключена, а сериализация
    /// значений типа object вообще требует известных на этапе компиляции типов.
    /// Ручная запись даёт ещё и предсказуемое форматирование с сохранением
    /// не-ASCII символов как есть.
    /// </summary>
    private static string WriteJson(DataTable table)
    {
        var builder = new StringBuilder();
        builder.Append("[\n");

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            builder.Append("  {\n");

            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = c < row.Count ? row[c] : string.Empty;
                builder.Append("    ").Append(Quote(table.Columns[c])).Append(": ").Append(InferLiteral(value));
                builder.Append(c < table.Columns.Count - 1 ? ",\n" : "\n");
            }

            builder.Append(r < table.Rows.Count - 1 ? "  },\n" : "  }\n");
        }

        builder.Append("]\n");
        return builder.ToString();
    }

    /// <summary>
    /// Числа и булевы значения пишем без кавычек, остальное — строкой.
    /// Проверка на обратимость обязательна: «007» и «1.50» должны остаться строками,
    /// иначе почтовые индексы и версии превратятся в 7 и 1.5.
    /// </summary>
    private static string InferLiteral(string raw)
    {
        if (raw.Length == 0)
        {
            return "\"\"";
        }

        if (raw is "true" or "false")
        {
            return raw;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) &&
            integer.ToString(CultureInfo.InvariantCulture) == raw)
        {
            return raw;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) &&
            real.ToString(CultureInfo.InvariantCulture) == raw)
        {
            return raw;
        }

        return Quote(raw);
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    // ─────────────────────────────── XML ───────────────────────────────

    private static DataTable ReadXml(string text)
    {
        var document = XDocument.Parse(text);
        var root = document.Root
            ?? throw new InvalidDataException("XML 文档为空。");

        // Ищем самый многочисленный дочерний узел — считаем его строкой таблицы.
        var rowElement = root.Elements()
            .GroupBy(e => e.Name)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        if (rowElement is null)
        {
            throw new InvalidDataException("The XML has no child nodes, so no table can be built.");
        }

        var columns = new List<string>();
        foreach (var row in root.Elements(rowElement))
        {
            foreach (var child in row.Elements())
            {
                if (!columns.Contains(child.Name.LocalName))
                {
                    columns.Add(child.Name.LocalName);
                }
            }
        }

        var rows = new List<IReadOnlyList<string>>();

        foreach (var row in root.Elements(rowElement))
        {
            var values = new List<string>(columns.Count);

            foreach (var column in columns)
            {
                var child = row.Elements().FirstOrDefault(e => e.Name.LocalName == column);
                values.Add(child?.Value ?? string.Empty);
            }

            rows.Add(values);
        }

        return new DataTable(columns, rows);
    }

    private static string WriteXml(DataTable table)
    {
        var root = new XElement("table");

        foreach (var row in table.Rows)
        {
            var rowElement = new XElement("row");

            for (var i = 0; i < table.Columns.Count && i < row.Count; i++)
            {
                // Имя колонки может быть невалидным именем XML-узла — тогда уходим в атрибут.
                var name = SanitizeXmlName(table.Columns[i]);
                if (name is null)
                {
                    rowElement.Add(new XElement("field",
                        new XAttribute("name", table.Columns[i]),
                        row[i]));
                }
                else
                {
                    rowElement.Add(new XElement(name, row[i]));
                }
            }

            root.Add(rowElement);
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return document.Declaration + "\n" + document;
    }

    private static string? SanitizeXmlName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);

        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('_');
            }
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
