using System.Diagnostics;
using System.Text;
using NekoConverter.Core.Engines.Audio;
using NekoConverter.Core.Engines.Image;
using NekoConverter.Core.Engines.Mesh;
using NekoConverter.Core.Formats;
using SkiaSharp;

namespace NekoConverter.Core.Preview;

/// <summary>Какие предпросмотры показывать. Каждый можно отключить отдельно.</summary>
[Flags]
public enum Previewers
{
    None = 0,

    /// <summary>Уменьшенная копия изображения.</summary>
    Thumbnail = 1,

    /// <summary>Первые строки текстовых форматов: субтитры, таблицы, разметка.</summary>
    Text = 2,

    /// <summary>Параметры звука: длительность, частота, каналы.</summary>
    Audio = 4,

    /// <summary>Параметры видео и контейнера.</summary>
    Media = 8,

    /// <summary>Статистика трёхмерной сетки: вершины, полигоны, материалы.</summary>
    Mesh = 16,

    All = Thumbnail | Text | Audio | Media | Mesh,
}

/// <summary>
/// Результат предпросмотра — только данные, без готовых фраз.
///
/// Формулировки собирает интерфейс: он знает язык, а ядро не знает и знать не должно.
/// Раньше подпись собиралась здесь, и в китайском интерфейсе появлялись русские
/// слова вроде «первые 12 из 13 строк».
/// </summary>
/// <param name="Kind">Каким предпросмотрщиком получено.</param>
/// <param name="Label">Название формата. Имя собственное, переводу не подлежит.</param>
/// <param name="SizeBytes">Размер файла.</param>
/// <param name="Width">Ширина изображения, если известно.</param>
/// <param name="Height">Высота изображения, если известно.</param>
/// <param name="ShownLines">Сколько строк показано.</param>
/// <param name="TotalLines">Сколько строк всего.</param>
/// <param name="Details">Нейтральные подробности: «44100 Hz · 2 ch · 0:01».</param>
/// <param name="Lines">Построчное содержимое.</param>
/// <param name="ThumbnailPng">Миниатюра в PNG.</param>
/// <param name="Error">Сообщение о сбое предпросмотра, если он был.</param>
public sealed record FilePreview(
    Previewers Kind,
    string Label,
    long SizeBytes,
    int? Width = null,
    int? Height = null,
    int? ShownLines = null,
    int? TotalLines = null,
    string? Details = null,
    IReadOnlyList<string> Lines = null!,
    byte[]? ThumbnailPng = null,
    string? Error = null)
{
    public IReadOnlyList<string> Content => Lines ?? [];

    public bool HasAnything => Content.Count > 0 || ThumbnailPng is not null || Details is { Length: > 0 };
}

/// <summary>
/// Предпросмотр исходного файла до преобразования.
///
/// Смысл не в том, чтобы заменить просмотрщик, а в том, чтобы человек убедился,
/// что выбрал нужный файл и понял, с чем имеет дело. Поэтому всё ограничено:
/// миниатюра небольшого размера и несколько первых строк.
///
/// Каждый предпросмотрщик можно отключить: на слабых машинах или при работе
/// с большими файлами разбор только мешает.
/// </summary>
public static class PreviewService
{
    private const int ThumbnailSide = 320;
    private const int MaxTextLines = 12;

    public static FilePreview Describe(string path, CapabilityRegistry registry, Previewers enabled)
    {
        if (!File.Exists(path))
        {
            return new FilePreview(Previewers.None, "", 0);
        }

        var source = registry.ByPath(path);
        if (source is null)
        {
            return new FilePreview(Previewers.None, "", 0);
        }

        try
        {
            return source.Kind switch
            {
                FormatKind.Image when enabled.HasFlag(Previewers.Thumbnail) =>
                    ImagePreview(path, source),

                FormatKind.Subtitle when enabled.HasFlag(Previewers.Text) =>
                    TextPreview(path, source),

                FormatKind.Data when enabled.HasFlag(Previewers.Text) =>
                    TextPreview(path, source),

                FormatKind.Document when enabled.HasFlag(Previewers.Text) =>
                    DocumentPreview(path, source),

                FormatKind.Audio when enabled.HasFlag(Previewers.Audio) =>
                    AudioPreview(path, source),

                FormatKind.Video when enabled.HasFlag(Previewers.Media) =>
                    MediaPreview(path, source),

                FormatKind.Model3D when enabled.HasFlag(Previewers.Mesh) =>
                    MeshPreview(path, source),

                _ => new FilePreview(Previewers.None, "", 0),
            };
        }
        catch (Exception ex)
        {
            // Предпросмотр — вспомогательная вещь: его сбой не должен мешать
            // главному действию, поэтому сообщаем и идём дальше.
            return new FilePreview(Previewers.None, source.Label ?? source.Id, 0, Error: ex.Message);
        }
    }

    // ─────────────────────────── Изображения ───────────────────────────

    private static FilePreview ImagePreview(string path, FormatDescriptor source)
    {
        // Сначала пробуем Skia: он работает в процессе и быстрее.
        using var decoded = TryDecodeWithSkia(path);

        var width = decoded?.Width ?? 0;
        var height = decoded?.Height ?? 0;

        // Форматы вроде TIFF или HEIC Skia не читает — тогда миниатюру делает sips.
        var thumbnail = decoded is not null
            ? MakeThumbnail(decoded)
            : MakeThumbnailWithSips(path);

        return new FilePreview(
            Previewers.Thumbnail,
            source.Label ?? source.Id,
            new FileInfo(path).Length,
            width > 0 ? width : null,
            height > 0 ? height : null,
            ThumbnailPng: thumbnail);
    }

    private static SKBitmap? TryDecodeWithSkia(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? MakeThumbnail(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest == 0)
        {
            return null;
        }

        var scale = Math.Min(1.0, (double)ThumbnailSide / longest);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var scaled = source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (scaled is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(scaled);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);

        return data?.ToArray();
    }

    /// <summary>Миниатюра для форматов, которые Skia не читает: конвертируем через sips.</summary>
    private static byte[]? MakeThumbnailWithSips(string path)
    {
        if (!SipsImageEngine.IsAvailable)
        {
            return null;
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"nekoconv-preview-{Guid.NewGuid():N}.png");

        try
        {
            SipsImageEngine.Convert(path, temporary, "png");

            using var decoded = TryDecodeWithSkia(temporary);
            return decoded is null ? null : MakeThumbnail(decoded);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // Временный файл не критичен.
            }
        }
    }

    // ─────────────────────────── Текст ───────────────────────────

    private static FilePreview TextPreview(string path, FormatDescriptor source)
    {
        var lines = ReadFirstLines(path, MaxTextLines);

        return new FilePreview(
            Previewers.Text,
            source.Label ?? source.Id,
            new FileInfo(path).Length,
            ShownLines: lines.Count,
            TotalLines: CountLines(path),
            Lines: lines);
    }

    private static FilePreview DocumentPreview(string path, FormatDescriptor source)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        // DOCX — это ZIP с XML: достаём абзацы тем же разбором, что и при преобразовании.
        if (extension is "docx" or "docm")
        {
            var document = Documents.DocxParser.Parse(path);

            var lines = document.Blocks
                .OfType<Documents.ParagraphBlock>()
                .Select(p => p.Text.Trim())
                .Where(text => text.Length > 0)
                .Take(MaxTextLines)
                .ToList();

            var paragraphs = document.Blocks.Count(b => b is Documents.ParagraphBlock);
            var tables = document.Blocks.OfType<Documents.TableBlock>().Count();

            // Подробности нейтральны: числа и общепринятые сокращения.
            var details = tables > 0
                ? $"paragraphs {paragraphs} · tables {tables}"
                : $"paragraphs {paragraphs}";

            return new FilePreview(
                Previewers.Text,
                source.Label ?? source.Id,
                new FileInfo(path).Length,
                ShownLines: lines.Count,
                TotalLines: paragraphs,
                Details: details,
                Lines: lines);
        }

        // Остальные документы читаем как текст: для предпросмотра этого достаточно.
        return TextPreview(path, source);
    }

    private static List<string> ReadFirstLines(string path, int count)
    {
        var lines = new List<string>();

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (lines.Count < count && reader.ReadLine() is { } line)
        {
            lines.Add(line.Length > 160 ? line[..160] + "…" : line);
        }

        return lines;
    }

    private static int CountLines(string path)
    {
        try
        {
            var count = 0;
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is not null)
            {
                count++;

                // Для предпросмотра точное число не нужно, а на гигабайтном файле
                // считать до конца — плохая идея.
                if (count > 100_000)
                {
                    return count;
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    // ─────────────────────────── Звук ───────────────────────────

    private static FilePreview AudioPreview(string path, FormatDescriptor source)
    {
        // Несжатый звук разбираем своим движком: он не требует внешних программ.
        if (PcmAudioConverter.IsPcmFormat(Path.GetExtension(path).TrimStart('.')))
        {
            var audio = PcmAudioConverter.ReadFile(path);

            // Единицы измерения международные: Hz, ch. Переводить их не нужно.
            var details = $"{audio.SampleRate} Hz · {audio.Channels} ch · {FormatDuration(audio.Duration)}";

            return new FilePreview(
                Previewers.Audio,
                source.Label ?? source.Id,
                new FileInfo(path).Length,
                Details: details);
        }

        // Сжатый звук разбирает FFmpeg, если он установлен.
        return MediaPreview(path, source);
    }

    // ─────────────────────────── Медиа ───────────────────────────

    private static FilePreview MediaPreview(string path, FormatDescriptor source)
    {
        var probe = FindFfprobe();

        if (probe is null)
        {
            // Без FFmpeg остаётся сообщить хотя бы размер.
            return new FilePreview(Previewers.Media, source.Label ?? source.Id, new FileInfo(path).Length);
        }

        var startInfo = new ProcessStartInfo(probe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Просим только нужные поля, чтобы не разбирать большой JSON.
        foreach (var argument in new[]
                 {
                     "-v", "error",
                     "-show_entries",
                     "format=duration,size,bit_rate:stream=codec_type,codec_name,width,height,sample_rate,channels",
                     "-of", "default=noprint_wrappers=1",
                     path,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);

        if (process is null || !process.WaitForExit(15_000))
        {
            try
            {
                process?.Kill(entireProcessTree: true);
            }
            catch
            {
                // Процесс мог завершиться сам.
            }

            return new FilePreview(Previewers.Media, source.Label ?? source.Id, new FileInfo(path).Length);
        }

        var output = process.StandardOutput.ReadToEnd();

        // Вывод ffprobe — это строки «ключ=значение». Показываем их как есть:
        // переводить названия кодеков на человеческий язык смысла нет.
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('=') && !line.EndsWith('='))
            .Take(MaxTextLines)
            .ToList();

        return new FilePreview(
            Previewers.Media,
            source.Label ?? source.Id,
            new FileInfo(path).Length,
            Lines: lines);
    }

    private static string? FindFfprobe()
    {
        // ffprobe лежит рядом с ffmpeg — сначала смотрим туда же, куда смотрит движок.
        var ffmpeg = ConversionService.Packages.ResolveExecutable("ffmpeg")
                     ?? Engines.External.ExternalTools.Find("ffmpeg");

        if (ffmpeg is not null)
        {
            var candidate = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Engines.External.ExternalTools.Find("ffprobe");
    }

    // ─────────────────────────── 3D ───────────────────────────

    private static FilePreview MeshPreview(string path, FormatDescriptor source)
    {
        // Описание сетки приходит из Assimp и состоит из чисел и латинских
        // сокращений — переводить там нечего.
        var description = MeshConverter.Describe(path);

        return new FilePreview(
            Previewers.Mesh,
            source.Label ?? source.Id,
            new FileInfo(path).Length,
            Lines: [description]);
    }

    // ─────────────────────────── Общее ───────────────────────────

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes}:{value.Seconds:D2}";
}
