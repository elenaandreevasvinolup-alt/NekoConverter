using System.Diagnostics;
using System.Globalization;

namespace NekoConverter.Core.Engines.Audio;

/// <summary>
/// Звуковой движок на базе системной утилиты macOS <c>afconvert</c> (за ней стоит CoreAudio).
///
/// Работает так же, как sips для изображений: даёт форматы, которых нет у нас,
/// и не стоит ни одного мегабайта в поставке.
///
/// Что проверено на macOS 14.8 ЗАМЕРАМИ (а не по справке):
///   кодирует — FLAC, AAC (в M4A и в ADTS), ALAC (в M4A и CAF), PCM (WAV/AIFF/CAF)
///   декодирует — MP3, FLAC, AAC, ALAC, WAV, AIFF, AU, CAF
///   НЕ кодирует — MP3 (в системе нет кодировщика), AC3, Opus, IMA4
///
/// Отсюда важное следствие для интерфейса: MP3 доступен только на чтение.
/// Это не ограничение нашего кода, а отсутствие кодировщика в самой macOS.
/// Для записи MP3 понадобится модуль с LAME.
/// </summary>
public static class AfconvertAudioEngine
{
    private const string AfconvertPath = "/usr/bin/afconvert";

    /// <summary>Сколько ждать ответа. AMR в системе умеет зависать намертво.</summary>
    private const int TimeoutMs = 120_000;

    /// <summary>Наш формат → (формат файла, формат данных) для afconvert.</summary>
    private static readonly Dictionary<string, (string FileFormat, string DataFormat, bool IsLossy)> Writable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["flac"] = ("flac", "flac", false),
            ["m4a"] = ("m4af", "aac", true),
            ["m4b"] = ("m4bf", "aac", true),
            ["aac"] = ("adts", "aac", true),
            ["alac"] = ("m4af", "alac", false),
            ["caf"] = ("caff", "LEI16", false),
            ["wav"] = ("WAVE", "LEI16", false),
            ["aiff"] = ("AIFF", "BEI16", false),
            ["au"] = ("NeXT", "BEI16", false),
        };

    /// <summary>Форматы, которые afconvert умеет читать.</summary>
    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "flac", "m4a", "m4b", "aac", "alac", "ac3", "ec3", "caf", "wav", "aiff", "aif", "aifc",
        "au", "snd", "mp4", "m4r", "3gp", "3g2", "sd2", "w64", "mp1", "mp2", "amr", "loas", "latm",
    };

    public static bool IsAvailable =>
        OperatingSystem.IsMacOS() && File.Exists(AfconvertPath);

    public static bool CanRead(string formatId) => Readable.Contains(formatId);

    public static bool CanWrite(string formatId) => Writable.ContainsKey(formatId);

    public static void Convert(
        string inputPath,
        string outputPath,
        string targetFormatId,
        int quality = 90,
        int? sampleRate = null,
        int? channels = null,
        int? bitsPerSample = null)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "The afconvert engine is only available on macOS. Install a codec module for this platform.");
        }

        if (!Writable.TryGetValue(targetFormatId, out var target))
        {
            throw new NotSupportedException($"afconvert cannot write the \"{targetFormatId}\" format.");
        }

        var dataFormat = AdjustForBitDepth(target.DataFormat, bitsPerSample);

        // Частота дискретизации задаётся прямо в спецификации формата данных: LEI16@22050.
        if (sampleRate is > 0)
        {
            dataFormat = $"{dataFormat}@{sampleRate.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        var args = new List<string>
        {
            "-f", target.FileFormat,
            "-d", dataFormat,
        };

        if (channels is > 0)
        {
            args.Add("-c");
            args.Add(channels.Value.ToString(CultureInfo.InvariantCulture));
        }

        // Битрейт имеет смысл только для форматов с потерями: для FLAC и ALAC он игнорируется.
        if (target.IsLossy)
        {
            args.Add("-b");
            args.Add(QualityToBitrate(quality).ToString(CultureInfo.InvariantCulture));
        }

        args.Add(inputPath);
        args.Add(outputPath);

        var (exitCode, stdout, stderr) = RunAfconvert(args);

        var written = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;

        // Пустой или крошечный файл — это молчаливый сбой (так ведёт себя AMR).
        if (exitCode != 0 || written < 128)
        {
            if (written < 128 && File.Exists(outputPath) &&
                !string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(inputPath), StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch
                {
                    // Пустой файл всё равно бесполезен.
                }
            }

            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"afconvert could not write {targetFormatId}: " +
                $"{(string.IsNullOrWhiteSpace(message) ? "the encoder is unavailable on this system" : message.Trim())}");
        }
    }

    /// <summary>Для PCM-целей разрядность задаётся кодом формата данных.</summary>
    private static string AdjustForBitDepth(string dataFormat, int? bitsPerSample)
    {
        if (bitsPerSample is not { } bits)
        {
            return dataFormat;
        }

        var prefix = dataFormat.StartsWith("BE", StringComparison.Ordinal) ? "BE" : "LE";

        return dataFormat switch
        {
            "LEI16" or "LEI24" or "LEI32" or "BEI16" or "BEI24" or "BEI32" => bits switch
            {
                8 => prefix + "I8",
                24 => prefix + "I24",
                32 => prefix + "I32",
                _ => prefix + "I16",
            },
            _ => dataFormat,
        };
    }

    /// <summary>
    /// Переводит нашу шкалу качества (1–100) в битрейт.
    /// Пользователь видит «высокое / среднее / малый размер», а не числа,
    /// но движку нужен конкретный битрейт.
    /// </summary>
    private static int QualityToBitrate(int quality) => quality switch
    {
        >= 95 => 320_000,
        >= 85 => 256_000,
        >= 70 => 192_000,
        >= 50 => 128_000,
        _ => 96_000,
    };

    private static (int ExitCode, string StdOut, string StdErr) RunAfconvert(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(AfconvertPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList вместо строки команды: пути с пробелами не проходят через shell.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start afconvert.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс мог завершиться сам между проверкой и Kill.
            }

            throw new TimeoutException(
                $"afconvert did not respond within {TimeoutMs / 1000} seconds. " +
                "Some macOS codecs can hang — try a different target format.");
        }

        return (process.ExitCode, stdout, stderr);
    }
}
