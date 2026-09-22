using System.Diagnostics;
using System.Globalization;

namespace NekoConverter.Core.Engines.Video;

/// <summary>
/// Движок звука и видео на FFmpeg.
///
/// FFmpeg НЕ входит в поставку приложения. Он ставится отдельно как пакет-зависимость
/// (dep-ffmpeg) и лежит в пользовательском каталоге пакетов. Приложение остаётся
/// лёгким, а лицензия FFmpeg не затрагивает наш код: мы запускаем его как отдельную
/// программу, а не компонуем с ним.
///
/// Кодеки не задаются жёстко: для каждого контейнера вызывается умолчание FFmpeg
/// (mp4 → H.264 + AAC, webm → VP9 + Opus и так далее). Так меньше поводов ошибиться
/// и не нужно сопровождать таблицу соответствий.
/// </summary>
public static class FfmpegEngine
{
    /// <summary>FFmpeg не любит висеть вечно; на больших файлах времени нужно много.</summary>
    private const int TimeoutMs = 60 * 60 * 1000;

    /// <summary>
    /// План кодирования для каждого целевого формата: контейнер и кодеки.
    ///
    /// Кодеки заданы ЯВНО, а не отданы на усмотрение FFmpeg. Причина конкретная:
    /// умолчания FFmpeg для части контейнеров указывают на кодеки, которых в сборке
    /// может не быть, и тогда преобразование падает с «Nothing was written into output file».
    /// Так вели себя matroska, ogg, 3gp и mpeg, пока кодеки не были прописаны руками.
    ///
    /// Каждая пара ниже проверена реальным преобразованием на сборке evermeet.cx 9.0.2.
    /// Имя контейнера тоже не всегда совпадает с расширением:
    /// mkv → matroska, wma/wmv → asf, mpg → mpeg, aac → adts, m4a/alac → ipod,
    /// ts/m2ts/mts → mpegts, ogv/ogm → ogg.
    /// </summary>
    /// <param name="Container">Имя muxer-а для ключа -f.</param>
    /// <param name="VideoCodec">null — видео не нужно.</param>
    /// <param name="AudioCodec">null — звук не нужен.</param>
    /// <param name="ForceSampleRate">Некоторые кодеки требуют строгой частоты (AMR — 8000).</param>
    /// <param name="ForceChannels">Некоторые кодеки требуют строгого числа каналов (AMR — моно).</param>
    private sealed record CodecPlan(
        string Container,
        string? VideoCodec,
        string? AudioCodec,
        int? ForceSampleRate = null,
        int? ForceChannels = null);

    private static readonly Dictionary<string, CodecPlan> Plans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp4"] = new("mp4", "libx264", "aac"),
        ["m4v"] = new("mp4", "libx264", "aac"),
        ["mov"] = new("mov", "libx264", "aac"),
        ["mkv"] = new("matroska", "libx264", "aac"),
        ["mka"] = new("matroska", null, "aac"),
        ["webm"] = new("webm", "libvpx-vp9", "libopus"),
        ["avi"] = new("avi", "mpeg4", "libmp3lame"),
        ["wmv"] = new("asf", "wmv2", "wmav2"),
        ["wma"] = new("asf", null, "wmav2"),
        ["flv"] = new("flv", "libx264", "aac"),
        ["f4v"] = new("flv", "libx264", "aac"),
        ["mpegps"] = new("mpeg", "mpeg2video", "mp2"),
        ["mpg"] = new("mpeg", "mpeg2video", "mp2"),
        ["mpeg"] = new("mpeg", "mpeg2video", "mp2"),
        ["vob"] = new("vob", "mpeg2video", "mp2"),
        // Ключи — идентификаторы из нашей таблицы форматов, а не расширения:
        // у mpegts и mpegps расширений несколько, и они сведены в одну строку.
        ["mpegts"] = new("mpegts", "libx264", "aac"),
        ["ts"] = new("mpegts", "libx264", "aac"),
        ["m2ts"] = new("mpegts", "libx264", "aac"),
        ["mts"] = new("mpegts", "libx264", "aac"),
        ["3gp"] = new("3gp", "libx264", "aac"),
        ["3g2"] = new("3g2", "libx264", "aac"),
        ["ogv"] = new("ogg", "libtheora", "libvorbis"),
        ["ogm"] = new("ogg", "libtheora", "libvorbis"),
        ["gif"] = new("gif", "gif", null),

        ["mp3"] = new("mp3", null, "libmp3lame"),
        ["m4a"] = new("ipod", null, "aac"),
        ["aac"] = new("adts", null, "aac"),
        ["flac"] = new("flac", null, "flac"),
        ["alac"] = new("ipod", null, "alac"),
        ["ogg"] = new("ogg", null, "libvorbis"),
        ["oga"] = new("ogg", null, "libvorbis"),
        ["opus"] = new("opus", null, "libopus"),
        ["ac3"] = new("ac3", null, "ac3"),
        ["dts"] = new("dts", null, "dca"),
        ["wv"] = new("wv", null, "wavpack"),
        ["tta"] = new("tta", null, "tta"),

        // Несжатый звук: сюда попадаем, только когда источник не PCM
        // (иначе сработал бы наш собственный движок pcm).
        ["wav"] = new("wav", null, "pcm_s16le"),
        ["aiff"] = new("aiff", null, "pcm_s16be"),
        ["au"] = new("au", null, "pcm_s16be"),

        // AMR существует только в 8 кГц моно — иначе кодировщик отказывается работать.
        ["amr"] = new("amr", null, "libopencore_amrnb", ForceSampleRate: 8000, ForceChannels: 1),
    };

    /// <summary>Кодеки без потерь: им битрейт не задаётся.</summary>
    private static readonly HashSet<string> LosslessAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "wavpack", "tta",
    };

    /// <summary>
    /// Верхние границы битрейта для звуковых кодеков.
    ///
    /// Это не перестраховка: при выходе за границу кодировщик не понижает значение сам,
    /// а отказывается работать целиком («encoder setup failed»), и файл получается пустым.
    /// Проверено на живых значениях: libvorbis не берёт 320 кбит/с, у libopus предел 256,
    /// у AMR-NB — 12.2. Кодеков без ограничения здесь просто нет.
    /// </summary>
    private static readonly Dictionary<string, int> MaxAudioBitrate = new(StringComparer.OrdinalIgnoreCase)
    {
        // libvorbis не берёт 256k на моно: предел зависит от каналов и частоты.
        // 192k проходит и на моно, и на стерео.
        ["libvorbis"] = 192_000,
        ["libopus"] = 256_000,
        ["wmav2"] = 192_000,
        ["mp2"] = 384_000,
        ["libopencore_amrnb"] = 12_200,
        ["libgsm"] = 13_000,
    };

    /// <summary>
    /// Дополнительные ключи для конкретных кодировщиков.
    /// dca (DTS) в FFmpeg помечен экспериментальным и без -strict просто отказывается работать.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExtraCodecArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dca"] = ["-strict", "-2"],
    };

    /// <summary>Видеокодеки, понимающие -crf. Остальным нужен -q:v.</summary>
    private static readonly HashSet<string> CrfVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "libx264", "libx265", "libvpx", "libvpx-vp9", "libaom-av1",
    };

    public static bool CanWrite(string formatId) => Plans.ContainsKey(formatId.TrimStart('.'));

    /// <summary>
    /// Выполняет преобразование.
    /// Путь к исполняемому файлу передаётся снаружи: движок не ищет зависимости сам
    /// и не держит глобального состояния — так его проще тестировать.
    /// </summary>
    public static void Convert(
        string executable,
        string inputPath,
        string outputPath,
        string targetFormatId,
        int quality = 90,
        int? maxDimension = null,
        int? sampleRate = null,
        int? channels = null)
    {
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            throw new PlatformNotSupportedException(
                "FFmpeg is not installed. It takes one click in the Modules section " +
                "and is about 26 MB.");
        }

        if (!Plans.TryGetValue(targetFormatId.TrimStart('.'), out var plan))
        {
            throw new NotSupportedException($"FFmpeg is not configured to write the \"{targetFormatId}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",                 // перезаписывать выходной файл без вопросов
            "-i", inputPath,
        };

        if (plan.VideoCodec is null)
        {
            args.Add("-vn");      // видео в звуковом контейнере не нужно
        }
        else
        {
            args.Add("-c:v");
            args.Add(plan.VideoCodec);

            // Качество видео: CRF там, где он поддерживается, иначе qscale.
            if (CrfVideoCodecs.Contains(plan.VideoCodec))
            {
                args.Add("-crf");
                args.Add(Math.Clamp(51 - (int)Math.Round(quality / 100.0 * 33), 0, 51)
                    .ToString(CultureInfo.InvariantCulture));

                // yuv420p — самый совместимый формат пикселей: без него часть плееров
                // откажется открывать файл.
                if (plan.VideoCodec.StartsWith("libx26", StringComparison.Ordinal))
                {
                    args.Add("-pix_fmt");
                    args.Add("yuv420p");
                }
            }
            else
            {
                args.Add("-q:v");
                args.Add(Math.Clamp(31 - (int)Math.Round(quality / 100.0 * 29), 2, 31)
                    .ToString(CultureInfo.InvariantCulture));
            }
        }

        if (plan.AudioCodec is not null)
        {
            args.Add("-c:a");
            args.Add(plan.AudioCodec);

            if (ExtraCodecArguments.TryGetValue(plan.AudioCodec, out var extra))
            {
                args.AddRange(extra);
            }

            // PCM-кодеки и перечисленные без потерь: битрейт им не задаётся вообще.
            var isLossless = LosslessAudioCodecs.Contains(plan.AudioCodec)
                || plan.AudioCodec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase);

            if (!isLossless)
            {
                var bitrate = QualityToBitrate(quality);

                // Не даём кодеку получить значение, которое он не примет.
                if (MaxAudioBitrate.TryGetValue(plan.AudioCodec, out var maxBitrate))
                {
                    bitrate = Math.Min(bitrate, maxBitrate);
                }

                args.Add("-b:a");
                args.Add(bitrate.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (maxDimension is > 0 && plan.VideoCodec is not null)
        {
            // Уменьшаем по большей стороне с сохранением пропорций; -2 требует чётности.
            args.Add("-vf");
            args.Add($"scale='if(gt(iw,ih),{maxDimension},-2)':'if(gt(iw,ih),-2,{maxDimension})'");
        }

        // Явные требования формата перекрывают пожелания пользователя.
        var effectiveSampleRate = plan.ForceSampleRate ?? sampleRate;
        var effectiveChannels = plan.ForceChannels ?? channels;

        if (effectiveSampleRate is > 0)
        {
            args.Add("-ar");
            args.Add(effectiveSampleRate.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (effectiveChannels is > 0)
        {
            args.Add("-ac");
            args.Add(effectiveChannels.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-f");
        args.Add(plan.Container);

        args.Add(fullOutput);

        // Отладочный вывод: NEKOCONVERTER_DEBUG_FFMPEG=1 показывает точную команду.
        // Без этого подбирать кодеки и ключи приходится вслепую.
        if (Environment.GetEnvironmentVariable("NEKOCONVERTER_DEBUG_FFMPEG") is { Length: > 0 })
        {
            Console.Error.WriteLine($"[ffmpeg] {executable} {string.Join(' ', args)}");
        }

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

            throw new InvalidOperationException(
                $"FFmpeg could not complete the conversion: {Trim(stderr)}");
        }
    }

    private static int QualityToBitrate(int quality) => quality switch
    {
        >= 95 => 320_000,
        >= 85 => 256_000,
        >= 70 => 192_000,
        >= 50 => 128_000,
        _ => 96_000,
    };

    private static (int ExitCode, string StdErr) Run(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList вместо строки: пути с пробелами и кавычками не проходят через shell.
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

            throw new TimeoutException("FFmpeg did not respond within an hour — conversion aborted.");
        }

        return (process.ExitCode, stderr);
    }

    /// <summary>FFmpeg пишет много строк; в интерфейс отдаём только последнюю осмысленную.</summary>
    private static string Trim(string stderr)
    {
        var lines = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length == 0 ? "reason unknown" : lines[^1];
    }
}
