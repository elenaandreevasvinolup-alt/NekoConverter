using System.Diagnostics;

namespace NekoConverter.Core.Engines.External;

/// <summary>
/// Поиск программ, которые уже установлены в системе.
///
/// Это третья ступень после встроенных движков и пакетов: если у человека уже стоит
/// FFmpeg, LibreOffice или Calibre, скачивать ничего не нужно — просто пользуемся.
/// Приложение при этом не толстеет ни на байт.
///
/// Проверка кэшируется: обход списка путей при каждом преобразовании был бы
/// бессмысленной работой, а состав установленных программ за время работы не меняется.
/// </summary>
public static class ExternalTools
{
    /// <summary>
    /// Известные программы: движок → возможные пути и признак того, что файл
    /// действительно является нужной программой (для проверки запуском).
    /// </summary>
    private static readonly Dictionary<string, string[]> Candidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ffmpeg"] =
        [
            "/opt/homebrew/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "/usr/bin/ffmpeg",
            @"C:\ffmpeg\bin\ffmpeg.exe",
        ],

        ["pandoc"] =
        [
            "/opt/homebrew/bin/pandoc",
            "/usr/local/bin/pandoc",
            "/usr/bin/pandoc",
        ],

        // LibreOffice: на macOS это программа в /Applications, в Linux — soffice в PATH.
        ["libreoffice"] =
        [
            "/Applications/LibreOffice.app/Contents/MacOS/soffice",
            "/usr/local/bin/soffice",
            "/usr/bin/soffice",
            "/opt/libreoffice/program/soffice",
            @"C:\Program Files\LibreOffice\program\soffice.exe",
        ],

        // Calibre: консольный конвертер ebook-convert.
        ["calibre"] =
        [
            "/Applications/calibre.app/Contents/MacOS/ebook-convert",
            "/usr/local/bin/ebook-convert",
            "/usr/bin/ebook-convert",
        ],

        // DjVuLibre: ddjvu умеет отдавать страницы в растровые форматы.
        ["djvu"] =
        [
            "/opt/homebrew/bin/ddjvu",
            "/usr/local/bin/ddjvu",
            "/usr/bin/ddjvu",
        ],
    };

    private static readonly Lock CacheLock = new();
    private static Dictionary<string, string>? _cache;

    /// <summary>Путь к программе для движка, либо null, если её нет.</summary>
    public static string? Find(string engine)
    {
        lock (CacheLock)
        {
            _cache ??= Probe();
            return _cache.GetValueOrDefault(engine);
        }
    }

    /// <summary>Все найденные программы: движок → путь.</summary>
    public static IReadOnlyDictionary<string, string> Detected()
    {
        lock (CacheLock)
        {
            _cache ??= Probe();
            return _cache;
        }
    }

    /// <summary>Сбрасывает кэш. Нужен тестам и случаю, когда программу поставили при работающем приложении.</summary>
    public static void Reset()
    {
        lock (CacheLock)
        {
            _cache = null;
        }
    }

    private static Dictionary<string, string> Probe()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (engine, paths) in Candidates)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    found[engine] = path;
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Проверяет, что программа запускается и отвечает.
    /// Нужно для внешних программ: файл на месте ещё не значит, что он рабочий.
    /// </summary>
    public static bool CanRun(string path, string versionArgument = "--version")
    {
        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add(versionArgument);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();

            // Десяти секунд хватает любой из этих программ на вывод версии.
            return process.WaitForExit(10_000);
        }
        catch
        {
            return false;
        }
    }
}
