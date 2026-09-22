using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NekoConverter.Core.Engines.Data;

/// <summary>
/// YAML через библиотеку YamlDotNet.
///
/// Используется НЕ высокоуровневый Deserialize, а низкоуровневое дерево YamlStream.
/// Причина конкретная: Deserialize строит объекты через отражение, а в AOT-сборке
/// отражение для типов, которых нет в статическом анализе, не работает. Обход узлов
/// дерева отражения не требует и потому одинаково работает и в обычной, и в AOT-сборке.
///
/// YAML — это надмножество JSON, поэтому преобразование идёт через ту же таблицу,
/// что и остальные форматы данных: YAML → таблица → CSV/TSV/JSON/XML/XLSX.
/// </summary>
public static class YamlConverter
{
    /// <summary>Читает YAML в таблицу. Ожидается список отображений, отображение или список значений.</summary>
    public static DataTable Read(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));

        if (stream.Documents.Count == 0)
        {
            return new DataTable([], []);
        }

        var root = stream.Documents[0].RootNode;

        return root switch
        {
            YamlSequenceNode sequence => FromSequence(sequence),
            YamlMappingNode mapping => FromMapping(mapping),
            YamlScalarNode scalar => new DataTable(["value"], [[scalar.Value ?? string.Empty]]),
            _ => new DataTable([], []),
        };
    }

    /// <summary>Пишет таблицу как список отображений — самый привычный вид YAML для табличных данных.</summary>
    public static string Write(DataTable table)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var row in table.Rows)
        {
            // Первая строка списка начинается с «- », остальные выравниваются по ней.
            var first = true;

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = i < row.Count ? row[i] : string.Empty;
                var prefix = first ? "- " : "  ";
                first = false;

                builder.Append(prefix)
                    .Append(QuoteIfNeeded(table.Columns[i]))
                    .Append(": ")
                    .Append(QuoteIfNeeded(value))
                    .Append('\n');
            }

            // Пустая строка между элементами списка: так читаемее.
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static DataTable FromSequence(YamlSequenceNode sequence)
    {
        // Список отображений — обычный табличный вид: колонки собираются из ключей.
        var mappings = sequence.Children.OfType<YamlMappingNode>().ToList();

        if (mappings.Count > 0)
        {
            var columns = new List<string>();

            foreach (var mapping in mappings)
            {
                foreach (var key in mapping.Children.Keys)
                {
                    var name = key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString() ?? string.Empty;
                    if (name.Length > 0 && !columns.Contains(name))
                    {
                        columns.Add(name);
                    }
                }
            }

            var rows = mappings
                .Select(m => (IReadOnlyList<string>)columns
                    .Select(c => m.Children.TryGetValue(new YamlScalarNode(c), out var v) ? ToText(v) : string.Empty)
                    .ToList())
                .ToList();

            return new DataTable(columns, rows);
        }

        // Список простых значений — одна колонка.
        var values = sequence.Children.Select(ToText).ToList();
        return new DataTable(["value"], values.Select(v => (IReadOnlyList<string>)[v]).ToList());
    }

    private static DataTable FromMapping(YamlMappingNode mapping)
    {
        var columns = new List<string>();
        var values = new List<string>();

        foreach (var (key, value) in mapping.Children)
        {
            columns.Add(key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString() ?? string.Empty);
            values.Add(ToText(value));
        }

        return new DataTable(columns, [values]);
    }

    /// <summary>Узел в строку. Вложенные структуры остаются как есть: таблица не умеет иерархию.</summary>
    private static string ToText(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value ?? string.Empty,
        _ => node.ToString()?.Trim() ?? string.Empty,
    };

    /// <summary>
    /// Берём значение в кавычки, если без них YAML прочитал бы его иначе:
    /// числа, булевы значения, пустая строка и служебные символы.
    /// </summary>
    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        // Числа: кавычки нужны только тогда, когда без них значение прочиталось бы
        // ИНАЧЕ. «28» читается как 28 и обратно даёт «28» — кавычки лишние.
        // А «007» и «1.50» без кавычек потеряют ведущий ноль и хвостовой ноль,
        // поэтому их закрываем.
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture) == value ? value : Quote(value);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            return real.ToString(CultureInfo.InvariantCulture) == value ? value : Quote(value);
        }

        var needsQuotes =
            value is "true" or "false" or "null" or "~" or "yes" or "no" or "on" or "off"
            || value.StartsWith('-') || value.StartsWith('?') || value.StartsWith(':')
            || value.StartsWith('[') || value.StartsWith('{') || value.StartsWith('*')
            || value.StartsWith('&') || value.StartsWith('!') || value.StartsWith('|')
            || value.StartsWith('>') || value.StartsWith('%') || value.StartsWith('@')
            || value.Contains(": ") || value.Contains(" #") || value.Contains('\n');

        if (!needsQuotes)
        {
            return value;
        }

        return Quote(value);
    }

    private static string Quote(string value)
    {

        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
    }
}
