using System.Diagnostics;
using System.Globalization;

namespace NekoConverter.Core.Engines.Image;

/// <summary>
/// Движок изображений на базе системной утилиты macOS <c>sips</c> (за ней стоит ImageIO).
///
/// Зачем он нужен: ImageIO покрывает длинную линейку форматов, включая ВСЕ RAW камер
/// (CR2/CR3/NEF/ARW/RAF/ORF/RW2/PEF/DNG/...), HEIC, AVIF, JPEG XL, TIFF, JPEG 2000, PSD,
/// OpenEXR, DDS, ICO, ICNS и SVG. Это ровно те форматы, которых нет в SkiaSharp,
/// и достаются они БЕСПЛАТНО: 0 МБ в поставке, потому что утилита уже есть в системе.
///
/// Цена: запуск процесса ~45 мс. Поэтому PNG/JPEG/WebP идут через Skia внутри процесса,
/// а сюда попадает только длинная линейка.
///
/// ВАЖНО: списки ниже получены ЗАМЕРАМИ на macOS 14.8, а не взяты из справки sips.
/// Справка врёт в двух местах:
///   dds  — помечен Writable, но запись даёт файл нулевого размера (молчаливый сбой);
///   ktx2 — помечен Writable, но запись падает с ошибкой.
/// Поэтому проверка на пустой файл здесь обязательна.
/// </summary>
public static class SipsImageEngine
{
    private const string SipsPath = "/usr/bin/sips";

    /// <summary>Форматы, которые sips умеет ЧИТАТЬ (проверено, плюс список RAW из ImageIO).</summary>
    private static readonly HashSet<string> ReadableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        // обычные
        "png", "jpeg", "jpg", "gif", "bmp", "webp", "tiff", "tga", "dds", "ico", "cur", "icns",
        "pbm", "pgm", "ppm", "pnm", "pict", "pct", "sgi", "pvr", "mpo", "ktx", "ktx2", "astc",
        "pdf", "svg",
        // современные
        "heic", "heif", "heics", "avif", "avci", "jxl", "jp2", "j2k", "psd", "psb", "exr",
        "hdr", "pic",
        // RAW камер
        "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "raf", "orf", "rw2", "raw",
        "pef", "ptx", "dng", "rwl", "3fr", "fff", "iiq", "mrw", "mos", "srw", "dcr", "erf", "dxo",
    };

    /// <summary>Форматы, которые sips умеет ПИСАТЬ, и соответствующий аргумент -s format.</summary>
    private static readonly Dictionary<string, string> WritableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "png",
        ["jpeg"] = "jpeg",
        ["gif"] = "gif",
        ["bmp"] = "bmp",
        ["tiff"] = "tiff",
        ["heic"] = "heic",
        ["heics"] = "heics",
        ["jp2"] = "jp2",
        ["psd"] = "psd",
        ["exr"] = "exr",
        ["tga"] = "tga",
        ["ico"] = "ico",
        ["icns"] = "icns",
        ["pbm"] = "pbm",
        ["pvr"] = "pvr",
        ["ktx"] = "ktx",
        ["astc"] = "astc",
        ["pdf"] = "pdf",
    };

    public static bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(SipsPath);

    public static bool CanRead(string formatId) => ReadableFormats.Contains(formatId);

    public static bool CanWrite(string formatId) => WritableFormats.ContainsKey(formatId);

    /// <summary>Конвертирует файл напрямую: sips сам декодирует источник и кодирует цель.</summary>
    public static void Convert(
        string inputPath,
        string outputPath,
        string targetFormatId,
        int? jpegQuality = null,
        int? maxDimension = null)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "The sips engine is only available on macOS. Install a codec module for this platform.");
        }

        if (!WritableFormats.TryGetValue(targetFormatId, out var token))
        {
            throw new NotSupportedException($"sips cannot write the \"{targetFormatId}\" format.");
        }

        var args = new List<string>();

        // -Z уменьшает так, чтобы большая сторона была не больше указанной, сохраняя пропорции.
        if (maxDimension is > 0)
        {
            args.Add("-Z");
            args.Add(maxDimension.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-s");
        args.Add("format");
        args.Add(token);

        // formatOptions задаёт качество для форматов с потерями (в первую очередь JPEG).
        if (jpegQuality is { } quality && targetFormatId.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-s");
            args.Add("formatOptions");
            args.Add(Math.Clamp(quality, 1, 100).ToString(CultureInfo.InvariantCulture));
        }

        args.Add(inputPath);
        args.Add("--out");
        args.Add(outputPath);

        var (exitCode, stdout, stderr) = RunSips(args);

        var written = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;

        // Пустой файл — это молчаливый сбой sips (так ведёт себя dds). Ловим его явно,
        // иначе пользователь получит битый результат без единого сообщения об ошибке.
        if (exitCode != 0 || written == 0)
        {
            if (written == 0 && File.Exists(outputPath) &&
                !string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(inputPath), StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch
                {
                    // Не критично: пустой файл всё равно бесполезен.
                }
            }

            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "sips finished without a message (the format is probably not writable).";
            }

            throw new InvalidOperationException($"sips could not write {targetFormatId}: {message.Trim()}");
        }
    }

    /// <summary>Приводит файл к PNG — универсальному промежуточному формату между движками.</summary>
    public static void ToPng(string inputPath, string outputPath) =>
        Convert(inputPath, outputPath, "png");

    private static (int ExitCode, string StdOut, string StdErr) RunSips(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(SipsPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList, а не строка команды: имена файлов не проходят через shell,
        // поэтому пробелы и кавычки в путях безопасны.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start sips.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс мог завершиться сам между проверкой и Kill.
            }

            throw new TimeoutException("sips did not respond within 60 seconds.");
        }

        return (process.ExitCode, stdout, stderr);
    }
}
