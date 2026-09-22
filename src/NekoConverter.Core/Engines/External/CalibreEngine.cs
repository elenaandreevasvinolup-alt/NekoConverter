using System.Diagnostics;

namespace NekoConverter.Core.Engines.External;

/// <summary>
/// Электронные книги через Calibre.
///
/// Форматы вроде MOBI, AZW3, LRF и LIT не читает больше никто: это либо закрытые
/// форматы Amazon, либо наследие старых читалок. Calibre — единственная программа,
/// которая их понимает, и весит она сотни мегабайт.
///
/// Как и с LibreOffice: ничего не скачиваем и не встраиваем, только пользуемся
/// тем, что уже стоит в системе.
/// </summary>
public static class CalibreEngine
{
    private const int TimeoutMs = 15 * 60 * 1000;

    /// <summary>Форматы, которые Calibre читает.</summary>
    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
    {
        "epub", "mobi", "azw", "azw3", "kf8", "prc", "lit", "lrf", "pdb",
        "fb2", "cbz", "cbr", "htmlz", "txt", "rtf", "docx", "odt", "html", "pdf",
        // Справка Windows: её понимает только Calibre.
        "chm",
    };

    /// <summary>Форматы, которые Calibre пишет.</summary>
    private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
    {
        "epub", "mobi", "azw3", "fb2", "lit", "lrf", "pdb", "txt", "rtf", "docx", "pdf", "htmlz", "cbz",
    };

    public static bool IsAvailable => ExternalTools.Find("calibre") is not null;

    public static bool CanRead(string formatId) => Readable.Contains(formatId.TrimStart('.'));

    public static bool CanWrite(string formatId) => Writable.Contains(formatId.TrimStart('.'));

    public static void Convert(string inputPath, string outputPath, string targetFormatId)
    {
        var executable = ExternalTools.Find("calibre")
            ?? throw new PlatformNotSupportedException(
                "Calibre was not found. Install it to work with e-books.");

        var target = targetFormatId.TrimStart('.').ToLowerInvariant();

        if (!Writable.Contains(target))
        {
            throw new NotSupportedException($"Calibre is not configured to write the \"{target}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Calibre определяет целевой формат по расширению выходного файла,
        // поэтому имя результата обязано заканчиваться на нужное расширение.
        var (exitCode, stderr) = Run(executable,
        [
            Path.GetFullPath(inputPath),
            fullOutput,
        ]);

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

            throw new InvalidOperationException(
                $"Calibre could not complete the conversion: {LastLine(stderr)}");
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

            throw new TimeoutException("Calibre did not respond within 15 minutes.");
        }

        return (process.ExitCode, stderr);
    }

    private static string LastLine(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "reason unknown" : lines[^1];
    }
}
