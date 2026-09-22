using System.Diagnostics;

namespace NekoConverter.Core.Engines.Documents;

/// <summary>
/// Движок документов на Pandoc.
///
/// Как и FFmpeg, Pandoc НЕ входит в поставку: он ставится пакетом-зависимостью
/// (dep-pandoc) и запускается как отдельная программа. Поэтому лицензия Pandoc
/// (GPL) не затрагивает наш код, а приложение остаётся лёгким.
///
/// Что даёт Pandoc поверх встроенного движка:
///   встроенный умеет только DOCX → (PDF, Markdown, текст, HTML);
///   Pandoc умеет около десятка форматов в обе стороны, включая ODT, RTF, EPUB, LaTeX.
///
/// Форматы Pandoc не генерирует PDF сам: для этого ему нужен внешний LaTeX.
/// Поэтому PDF остаётся за встроенным движком.
/// </summary>
public static class PandocEngine
{
    private const int TimeoutMs = 10 * 60 * 1000;

    /// <summary>Наш идентификатор формата → имя формата в терминах Pandoc.</summary>
    private static readonly Dictionary<string, string> FormatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["md"] = "markdown",
        ["markdown"] = "markdown",
        ["html"] = "html",
        ["htm"] = "html",
        ["docx"] = "docx",
        ["odt"] = "odt",
        ["rtf"] = "rtf",
        ["tex"] = "latex",
        ["latex"] = "latex",
        ["rst"] = "rst",
        ["org"] = "org",
        ["epub"] = "epub",
        ["fb2"] = "fb2",
        ["docbook"] = "docbook",
        ["opml"] = "opml",
        ["pptx"] = "pptx",
        ["typst"] = "typst",
        ["csv"] = "csv",
        ["tsv"] = "tsv",
    };

    /// <summary>
    /// Форматы, которые Pandoc только пишет.
    /// «plain» — это обычный текст: как формат вывода существует, как формат ввода нет.
    /// </summary>
    private static readonly Dictionary<string, string> WriteOnlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["txt"] = "plain",
    };

    /// <summary>Форматы, которые читаются не тем именем, каким пишутся.</summary>
    private static readonly Dictionary<string, string> ReadAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // У Pandoc нет читателя «plain», но обычный текст прекрасно разбирает markdown.
        ["txt"] = "markdown",
    };

    public static bool CanRead(string formatId) =>
        FormatNames.ContainsKey(formatId) || ReadAliases.ContainsKey(formatId);

    public static bool CanWrite(string formatId) =>
        FormatNames.ContainsKey(formatId) || WriteOnlyNames.ContainsKey(formatId);

    public static void Convert(string executable, string inputPath, string outputPath, string targetFormatId)
    {
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            throw new PlatformNotSupportedException(
                "Pandoc is not installed. It takes one click in the Modules section.");
        }

        var sourceFormat = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var target = targetFormatId.TrimStart('.').ToLowerInvariant();

        var from = ReadAliases.TryGetValue(sourceFormat, out var alias)
            ? alias
            : FormatNames.GetValueOrDefault(sourceFormat, "markdown");

        if (!WriteOnlyNames.TryGetValue(target, out var to))
        {
            to = FormatNames.GetValueOrDefault(target)
                ?? throw new NotSupportedException($"Pandoc cannot write the \"{target}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var args = new List<string>
        {
            "-f", from,
            "-t", to,
            "-o", fullOutput,
        };

        // Для «цельных» форматов нужен полный документ, а не фрагмент:
        // иначе HTML получится без <head>, а DOCX — без стилей.
        if (to is "html" or "docx" or "odt" or "epub" or "rtf" or "latex")
        {
            args.Add("--standalone");
        }

        args.Add(inputPath);

        var (exitCode, stderr) = Run(executable, args);

        if (exitCode != 0 || !File.Exists(fullOutput) || new FileInfo(fullOutput).Length == 0)
        {
            if (File.Exists(fullOutput) && new FileInfo(fullOutput).Length == 0)
            {
                try
                {
                    File.Delete(fullOutput);
                }
                catch
                {
                    // Пустой файл бесполезен.
                }
            }

            throw new InvalidOperationException($"Pandoc could not complete the conversion: {LastLine(stderr)}");
        }
    }

    private static (int ExitCode, string StdErr) Run(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start \"{executable}\".");

        var stderr = process.StandardError.ReadToEnd();
        _ = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(TimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс мог завершиться сам.
            }

            throw new TimeoutException("Pandoc did not respond within 10 minutes — conversion aborted.");
        }

        return (process.ExitCode, stderr);
    }

    private static string LastLine(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "reason unknown" : lines[^1];
    }
}
