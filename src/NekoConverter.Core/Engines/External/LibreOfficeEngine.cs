using System.Diagnostics;

namespace NekoConverter.Core.Engines.External;

/// <summary>
/// Документы через LibreOffice в режиме командной строки.
///
/// Это единственный способ получить ВЫСОКУЮ точность вёрстки для форматов Office
/// и PostScript. Собственный движок приложения умеет только DOCX и только базовую
/// вёрстку, а LibreOffice — это тот же движок, что и в самом офисном пакете.
///
/// Почему не встроить: LibreOffice занимает около 500 МБ. Встраивать его в приложение
/// на 30 МБ бессмысленно, а вот воспользоваться уже установленным — разумно.
/// Поэтому здесь только поиск и вызов, ничего не скачивается.
/// </summary>
public static class LibreOfficeEngine
{
    private const int TimeoutMs = 15 * 60 * 1000;

    /// <summary>Форматы, которые LibreOffice читает (наш идентификатор → расширение фильтра).</summary>
    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "dot", "docx", "rtf", "odt", "txt", "html", "htm",
        "xls", "xlsx", "ods", "csv",
        "ppt", "pptx", "odp",
        "ps", "xps", "oxps", "wps",
        "docm", "xlsm", "pptm", "dotx", "xltx", "potx",
    };

    /// <summary>Целевые форматы, которые умеет писать LibreOffice.</summary>
    private static readonly Dictionary<string, string> TargetFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "pdf",
        ["docx"] = "docx:MS Word 2007 XML",
        ["doc"] = "doc:MS Word 97",
        ["odt"] = "odt:writer8",
        ["rtf"] = "rtf:Rich Text Format",
        ["txt"] = "txt:Text",
        ["html"] = "html:HTML (StarWriter)",
        ["xlsx"] = "xlsx:Calc MS Excel 2007 XML",
        ["ods"] = "ods:calc8",
        ["csv"] = "csv:Text - txt - csv (StarCalc)",
        ["pptx"] = "pptx:Impress MS PowerPoint 2007 XML",
        ["odp"] = "odp:impress8",
        ["xps"] = "xps:XPS",
    };

    public static bool IsAvailable => ExternalTools.Find("libreoffice") is not null;

    public static bool CanRead(string formatId) => Readable.Contains(formatId.TrimStart('.'));

    public static bool CanWrite(string formatId) => TargetFilters.ContainsKey(formatId.TrimStart('.'));

    public static void Convert(string inputPath, string outputPath, string targetFormatId)
    {
        var executable = ExternalTools.Find("libreoffice")
            ?? throw new PlatformNotSupportedException(
                "LibreOffice was not found. Install it if you need high layout fidelity.");

        if (!TargetFilters.TryGetValue(targetFormatId.TrimStart('.'), out var filter))
        {
            throw new NotSupportedException($"LibreOffice is not configured to write the \"{targetFormatId}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutput)
            ?? throw new InvalidOperationException("Could not determine the output directory.");

        Directory.CreateDirectory(outputDirectory);

        // LibreOffice не умеет писать в произвольный файл: он кладёт результат
        // в указанный каталог под именем исходного файла с новым расширением.
        // Поэтому конвертируем во временный каталог и потом переносим.
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(), $"nekoconv-lo-{Guid.NewGuid():N}");

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var arguments = new List<string>
            {
                "--headless",          // без окна: серверный режим
                "--norestore",         // не восстанавливать сеансы
                "--nolockcheck",
                "--nodefault",
                "--convert-to", filter,
                "--outdir", temporaryDirectory,
                Path.GetFullPath(inputPath),
            };

            var (exitCode, stderr) = Run(executable, arguments);

            // LibreOffice возвращает 0 даже при неудаче, поэтому проверяем результат,
            // а не код возврата.
            var produced = Directory.GetFiles(temporaryDirectory);

            if (produced.Length == 0)
            {
                throw new InvalidOperationException(
                    "LibreOffice did not create a file. " +
                    (string.IsNullOrWhiteSpace(stderr) ? "No reason given." : stderr.Trim()));
            }

            // Если LibreOffice всё же вернул ненулевой код, но файл есть — это не ошибка.
            _ = exitCode;

            File.Move(produced[0], fullOutput, overwrite: true);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch
            {
                // Временный каталог не критичен.
            }
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

            throw new TimeoutException("LibreOffice did not respond within 15 minutes.");
        }

        return (process.ExitCode, stderr);
    }
}
