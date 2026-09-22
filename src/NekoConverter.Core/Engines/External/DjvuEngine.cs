using System.Diagnostics;

namespace NekoConverter.Core.Engines.External;

/// <summary>
/// DjVu через утилиту ddjvu из пакета DjVuLibre.
///
/// DjVu — формат отсканированных документов: он хранит изображение страницы,
/// разделённое на слои, и потому сжимает сканы в разы сильнее PDF с картинками.
/// Наших собственных средств для него нет, а библиотека DjVuLibre ставится отдельно
/// и весит немного.
///
/// Что делаем: разбираем DjVu обратно в изображение (или в PDF). Собирать DjVu
/// мы не умеем, и это осознанно: формат рассчитан на специальные кодировщики,
/// повторять их смысла нет.
/// </summary>
public static class DjvuEngine
{
    private const int TimeoutMs = 15 * 60 * 1000;

    /// <summary>Наш формат → имя формата в ddjvu.</summary>
    private static readonly Dictionary<string, string> TargetFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "png",
        ["jpeg"] = "jpeg",
        ["tiff"] = "tiff",
        ["bmp"] = "bmp",
        ["pbm"] = "pnm",
        ["ppm"] = "pnm",
        ["pdf"] = "pdf",
        ["ps"] = "ps",
    };

    public static bool IsAvailable => ExternalTools.Find("djvu") is not null;

    public static bool CanWrite(string formatId) => TargetFormats.ContainsKey(formatId.TrimStart('.'));

    public static void Convert(string inputPath, string outputPath, string targetFormatId)
    {
        var executable = ExternalTools.Find("djvu")
            ?? throw new PlatformNotSupportedException(
                "DjVuLibre was not found. Install it to work with DjVu files.");

        var target = targetFormatId.TrimStart('.').ToLowerInvariant();

        if (!TargetFormats.TryGetValue(target, out var ddjvuFormat))
        {
            throw new NotSupportedException($"DjVu cannot produce the \"{targetFormatId}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // -page=1: DjVu почти всегда многостраничный, а мы отдаём одну страницу.
        // Это осознанное ограничение первой версии: собирать многостраничный вывод
        // в один файл — отдельная задача для каждого целевого формата.
        var (exitCode, stderr) = Run(executable,
        [
            "-format=" + ddjvuFormat,
            "-page=1",
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

            throw new InvalidOperationException($"ddjvu could not parse the file: {LastLine(stderr)}");
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

            throw new TimeoutException("ddjvu did not respond within 15 minutes.");
        }

        return (process.ExitCode, stderr);
    }

    private static string LastLine(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "reason unknown" : lines[^1];
    }
}
