using SkiaSharp;

namespace NekoConverter.Core.Engines.Image;

/// <summary>
/// Движок изображений на SkiaSharp: работает внутри процесса, поэтому это самый быстрый путь
/// и основной для PNG/JPEG/WebP.
///
/// ВАЖНО: возможности SkiaSharp в рантайме НЕ совпадают со списком значений enum
/// SKEncodedImageFormat. Проверено на 3.119.4 под macOS:
///   читает  — PNG, JPEG, GIF, BMP, WebP
///   пишет   — PNG, JPEG, WebP
///   НЕ читает — TIFF, HEIC, AVIF; НЕ пишет — BMP, GIF, ICO, AVIF, HEIF
/// Поэтому длинная линейка отдана движку sips, а не «дописана» здесь.
/// </summary>
public static class SkiaImageEngine
{
    private static readonly Dictionary<string, SKEncodedImageFormat> Writable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = SKEncodedImageFormat.Png,
        ["jpeg"] = SKEncodedImageFormat.Jpeg,
        ["webp"] = SKEncodedImageFormat.Webp,
    };

    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpeg", "jpg", "gif", "bmp", "webp",
    };

    public static bool CanWrite(string formatId) => Writable.ContainsKey(formatId);

    public static bool CanRead(string formatId) => Readable.Contains(formatId);

    public static void ConvertFile(
        string inputPath,
        string outputPath,
        string targetFormatId,
        int quality = 90,
        int? maxDimension = null)
    {
        var encoded = Convert(File.ReadAllBytes(inputPath), targetFormatId, quality, maxDimension);

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullOutput, encoded);
    }

    /// <summary>Перекодирует байты изображения в целевой формат.</summary>
    public static byte[] Convert(byte[] input, string targetFormatId, int quality = 90, int? maxDimension = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!Writable.TryGetValue(targetFormatId, out var skiaFormat))
        {
            throw new NotSupportedException($"Skia cannot write the \"{targetFormatId}\" format.");
        }

        using var codec = SKCodec.Create(new SKMemoryStream(input))
            ?? throw new InvalidDataException("Unrecognized image format (decoding failed).");

        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("Image decoding failed.");

        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        SKBitmap? scaled = null;
        SKBitmap? flattened = null;

        try
        {
            var target = oriented;

            if (maxDimension is > 0)
            {
                scaled = Resize(oriented, maxDimension.Value);
                target = scaled;
            }

            // JPEG без альфа-канала: без подложки прозрачные области станут чёрными.
            if (skiaFormat == SKEncodedImageFormat.Jpeg)
            {
                flattened = FlattenOnto(target, SKColors.White);
                target = flattened;
            }

            var effectiveQuality = skiaFormat == SKEncodedImageFormat.Png
                ? 100
                : Math.Clamp(quality, 1, 100);

            // Кодируем через SKPixmap: SKImage.Encode в SkiaSharp 3.x возвращает null
            // для растровых битмапов, а SKPixmap.Encode отдаёт честный результат.
            using var pixmap = target.PeekPixels()
                ?? throw new InvalidOperationException("Could not read the image pixels.");

            using var buffer = new MemoryStream();
            if (!pixmap.Encode(buffer, skiaFormat, effectiveQuality))
            {
                throw new InvalidOperationException("图片编码失败。");
            }

            return buffer.ToArray();
        }
        finally
        {
            scaled?.Dispose();
            flattened?.Dispose();
        }
    }

    /// <summary>
    /// Уменьшает изображение так, чтобы большая сторона не превышала maxDimension.
    /// Пропорции сохраняются. Если картинка уже меньше, возвращается копия как есть.
    /// </summary>
    private static SKBitmap Resize(SKBitmap source, int maxDimension)
    {
        var longestSide = Math.Max(source.Width, source.Height);

        if (longestSide <= maxDimension)
        {
            return source.Copy();
        }

        var scale = (double)maxDimension / longestSide;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);

        // Cubic (Mitchell) — разумный компромисс между резкостью и ringing
        // для уменьшения фотографий.
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Could not resize the image.");
    }

    private static SKBitmap FlattenOnto(SKBitmap source, SKColor background)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var destination = new SKBitmap(info);

        using var canvas = new SKCanvas(destination);
        canvas.Clear(background);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();

        return destination;
    }

    /// <summary>
    /// Разворачивает пиксели согласно EXIF Orientation.
    /// 1/3/6/8 (норма, 180°, 90° по часовой, 90° против) покрывают почти все фото с телефонов.
    /// 2/4/5/7 (зеркала и транспонирование) реализованы по выкладке, на реальных образцах не проверялись.
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var destWidth = swapsAxes ? source.Height : source.Width;
        var destHeight = swapsAxes ? source.Width : source.Height;

        var destination = new SKBitmap(new SKImageInfo(destWidth, destHeight, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(destination);

        // В Skia преобразования применяются к точке в обратном порядке вызова:
        // порядок ниже — от внешнего преобразования к внутреннему.
        switch (origin)
        {
            case SKEncodedOrigin.TopLeft:
                break;
            case SKEncodedOrigin.TopRight: // зеркало по горизонтали
                canvas.Translate(destWidth, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180°
                canvas.Translate(destWidth, destHeight);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // зеркало по вертикали
                canvas.Translate(0, destHeight);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // транспонирование
                canvas.Translate(destWidth, 0);
                canvas.Scale(-1, 1);
                canvas.Translate(destWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // 90° по часовой
                canvas.Translate(destWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // антитранспонирование
                canvas.Translate(0, destHeight);
                canvas.Scale(1, -1);
                canvas.Translate(destWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° против часовой
                canvas.Translate(0, destHeight);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();

        return destination;
    }
}
