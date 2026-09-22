using SkiaSharp;

namespace NekoConverter.Cli;

/// <summary>
/// Диагностика: какие форматы реально декодирует и кодирует SkiaSharp в этой сборке.
/// Нужна потому, что список значений в enum SKEncodedImageFormat ничего не говорит
/// о том, включён ли соответствующий кодек в нативную библиотеку.
/// </summary>
internal static class Probe
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: nekoconv probe <目录>");
            return 1;
        }

        var directory = args[0];
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"目录不存在: {directory}");
            return 1;
        }

        Console.WriteLine($"SkiaSharp: {typeof(SKBitmap).Assembly.GetName().Version}");
        Console.WriteLine();
        Console.WriteLine("=== 解码 (decode) ===");

        var files = Directory.GetFiles(directory)
            .Where(f => !Path.GetFileName(f).StartsWith("out-", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).StartsWith("aot-", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).StartsWith("final.", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                using var stream = File.OpenRead(file);
                using var codec = SKCodec.Create(stream);
                if (codec is null)
                {
                    Console.WriteLine($"  ✗ {name,-18} 无法识别");
                    continue;
                }

                using var bitmap = SKBitmap.Decode(codec);
                if (bitmap is null)
                {
                    Console.WriteLine($"  ✗ {name,-18} 识别为 {codec.EncodedFormat} 但解码失败");
                    continue;
                }

                Console.WriteLine(
                    $"  ✓ {name,-18} {codec.EncodedFormat,-6} {bitmap.Width}x{bitmap.Height} " +
                    $"{bitmap.ColorType}/{bitmap.AlphaType} origin={codec.EncodedOrigin}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {name,-18} 异常: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 编码 (encode) ===");

        using var source = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(new SKColor(200, 60, 60));
        }

        foreach (var format in Enum.GetValues<SKEncodedImageFormat>())
        {
            try
            {
                using var pixmap = source.PeekPixels();
                using var buffer = new MemoryStream();
                var ok = pixmap.Encode(buffer, format, 90);
                Console.WriteLine(ok
                    ? $"  ✓ {format,-10} {buffer.Length,7:N0} байт"
                    : $"  ✗ {format,-10} 编码器返回失败");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {format,-10} {ex.GetType().Name}");
            }
        }

        return 0;
    }
}
