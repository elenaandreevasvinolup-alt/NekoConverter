using System.Text;
using System.Text.Encodings.Web;
using NekoConverter.Core.Documents;

namespace NekoConverter.Core.Engines.Documents;

/// <summary>
/// Извлечение содержимого DOCX в текст, Markdown и HTML.
///
/// Использует тот же разобранный документ, что и рендер PDF, поэтому структура
/// (заголовки, таблицы, списки) переносится, а не теряется.
/// </summary>
public static class DocxTextConverter
{
    public static readonly string[] SupportedTargets = ["txt", "md", "html"];

    public static void ConvertFile(string inputPath, string outputPath, string targetFormatId)
    {
        var document = DocxParser.Parse(inputPath);

        var text = targetFormatId.TrimStart('.').ToLowerInvariant() switch
        {
            "txt" => ToPlainText(document),
            "md" => ToMarkdown(document),
            "html" => ToHtml(document),
            _ => throw new NotSupportedException($"Extraction to \"{targetFormatId}\" is not supported."),
        };

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullOutput, text, new UTF8Encoding(false));
    }

    public static string ToPlainText(DocxDocument document)
    {
        var builder = new StringBuilder();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    if (paragraph.IsListItem)
                    {
                        builder.Append("• ");
                    }

                    builder.Append(paragraph.Text);
                    builder.Append("\n\n");
                    break;

                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        builder.Append(string.Join('\t', row.Select(CellText)));
                        builder.Append('\n');
                    }

                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    public static string ToMarkdown(DocxDocument document)
    {
        var builder = new StringBuilder();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    var level = paragraph.Style switch
                    {
                        ParagraphStyle.Title => 1,
                        ParagraphStyle.Heading1 => 1,
                        ParagraphStyle.Heading2 => 2,
                        ParagraphStyle.Heading3 => 3,
                        ParagraphStyle.Heading4 => 4,
                        _ => 0,
                    };

                    if (level > 0)
                    {
                        builder.Append('#', level).Append(' ');
                        builder.Append(EscapeMarkdown(paragraph.Text));
                        builder.Append("\n\n");
                    }
                    else
                    {
                        if (paragraph.IsListItem)
                        {
                            builder.Append("- ");
                        }

                        builder.Append(RunsToMarkdown(paragraph.Runs));
                        builder.Append("\n\n");
                    }

                    break;

                case TableBlock table:
                    WriteMarkdownTable(builder, table);
                    break;
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    public static string ToHtml(DocxDocument document)
    {
        var builder = new StringBuilder();
        var encoder = HtmlEncoder.Default;

        builder.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n</head>\n<body>\n");

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    var tag = paragraph.Style switch
                    {
                        ParagraphStyle.Title => "h1",
                        ParagraphStyle.Heading1 => "h1",
                        ParagraphStyle.Heading2 => "h2",
                        ParagraphStyle.Heading3 => "h3",
                        ParagraphStyle.Heading4 => "h4",
                        _ => paragraph.IsListItem ? "li" : "p",
                    };

                    builder.Append('<').Append(tag).Append('>');
                    builder.Append(RunsToHtml(paragraph.Runs, encoder));
                    builder.Append("</").Append(tag).Append(">\n");
                    break;

                case TableBlock table:
                    builder.Append("<table border=\"1\" cellpadding=\"4\">\n");
                    foreach (var row in table.Rows)
                    {
                        builder.Append("<tr>");
                        foreach (var cell in row)
                        {
                            builder.Append("<td>").Append(encoder.Encode(CellText(cell))).Append("</td>");
                        }

                        builder.Append("</tr>\n");
                    }

                    builder.Append("</table>\n");
                    break;
            }
        }

        builder.Append("</body>\n</html>\n");
        return builder.ToString();
    }

    private static void WriteMarkdownTable(StringBuilder builder, TableBlock table)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        var columns = table.Rows.Max(r => r.Count);

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            builder.Append('|');

            for (var column = 0; column < columns; column++)
            {
                var value = column < row.Count ? CellText(row[column]) : string.Empty;
                builder.Append(' ').Append(EscapeMarkdown(value)).Append(" |");
            }

            builder.Append('\n');

            // После строки заголовка Markdown требует разделитель.
            if (rowIndex == 0)
            {
                builder.Append('|');
                for (var column = 0; column < columns; column++)
                {
                    builder.Append(" --- |");
                }

                builder.Append('\n');
            }
        }

        builder.Append('\n');
    }

    private static string RunsToMarkdown(IReadOnlyList<DocxRun> runs)
    {
        var builder = new StringBuilder();

        foreach (var run in runs)
        {
            var text = EscapeMarkdown(run.Text);

            if (run.Bold)
            {
                text = $"**{text}**";
            }

            if (run.Italic)
            {
                text = $"*{text}*";
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string RunsToHtml(IReadOnlyList<DocxRun> runs, HtmlEncoder encoder)
    {
        var builder = new StringBuilder();

        foreach (var run in runs)
        {
            var text = encoder.Encode(run.Text);

            if (run.Bold)
            {
                text = $"<strong>{text}</strong>";
            }

            if (run.Italic)
            {
                text = $"<em>{text}</em>";
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string CellText(IReadOnlyList<DocxRun> runs) =>
        string.Concat(runs.Select(r => r.Text)).Replace('\n', ' ').Trim();

    private static string EscapeMarkdown(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
