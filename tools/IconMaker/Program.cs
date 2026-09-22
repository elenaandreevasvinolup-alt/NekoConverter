using SkiaSharp;

namespace NekoConverter.Tools.IconMaker;

/// <summary>
/// Собирает иконку приложения из нарисованного вручную баннера.
///
/// Источник — banner.png в корне репозитория. Картинка не квадратная, поэтому
/// она обрезается по центру до квадрата и получает маску со скруглёнными углами:
/// без маски иконка выглядит чужеродно рядом с системными.
///
/// Что получается на выходе:
///   Assets/icon-*.png   — растры всех нужных размеров
///   Assets/AppIcon.icns — для macOS
///   Assets/app.ico      — для Windows
///
/// Запуск: dotnet run --project tools/IconMaker -- [выходной-каталог] [исходник]
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 32, 64, 128, 256, 512, 1024];

    /// <summary>
    /// Радиус скругления в долях от стороны. 0.2237 — пропорция системных
    /// иконок macOS; при другом значении иконка выглядит либо слишком квадратной,
    /// либо слишком круглой на фоне остальных.
    /// </summary>
    private const float CornerRadiusRatio = 0.2237f;

    /// <summary>
    /// Доля холста, которую занимает сама иконка. По сетке macOS иконка
    /// вписывается в 824×824 внутри холста 1024×1024: вокруг остаётся
    /// прозрачное поле примерно по 100 пикселей с каждой стороны.
    /// Без этого поля иконка выглядит крупнее системных.
    /// </summary>
    private const float ContentRatio = 0.8047f;

    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0
            ? args[0]
            : Path.Combine("src", "NekoConverter.App", "Assets");

        var sourcePath = args.Length > 1
            ? args[1]
            : "banner.png";

        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Исходник не найден: {sourcePath}");
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"  источник: {sourcePath}");

        using var master = RenderSquare(sourcePath, 1024);

        var pngPaths = new Dictionary<int, string>();

        foreach (var size in Sizes)
        {
            var path = Path.Combine(outputDirectory, $"icon-{size}.png");
            SaveScaled(master, size, path);
            pngPaths[size] = path;
            Console.WriteLine($"  PNG  {size,4}x{size,-4} {path}");
        }

        var icnsPath = Path.Combine(outputDirectory, "AppIcon.icns");
        BuildIcns(pngPaths, icnsPath);
        Console.WriteLine($"  ICNS       {icnsPath}");

        var icoPath = Path.Combine(outputDirectory, "app.ico");
        BuildIco(pngPaths, icoPath);
        Console.WriteLine($"  ICO        {icoPath}");

        return 0;
    }

    // ─────────────────────────── Подготовка ───────────────────────────

    /// <summary>
    /// Приводит картинку к квадрату: обрезает по центру и скругляет углы.
    /// Обрезка по центру, а не растяжение: растяжение искажает пропорции лица.
    /// </summary>
    private static SKBitmap RenderSquare(string sourcePath, int size)
    {
        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException($"Не удалось прочитать изображение: {sourcePath}");

        var side = Math.Min(source.Width, source.Height);
        var cropX = (source.Width - side) / 2;
        var cropY = (source.Height - side) / 2;

        var result = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);

            // Маска со скруглёнными углами: рисуем ею, а не поверх —
            // так края получаются гладкими, без каймы.
            // Иконка занимает не весь холст: по краям остаётся прозрачное поле.
            var inset = size * (1f - ContentRatio) / 2f;
            var destination = new SKRect(inset, inset, size - inset, size - inset);

            // Радиус считается от стороны самой иконки, а не от холста, иначе
            // на уменьшенном квадрате углы скруглились бы слабее системных.
            var radius = destination.Width * CornerRadiusRatio;

            using var rounded = new SKPath();
            rounded.AddRoundRect(destination, radius, radius);

            canvas.ClipPath(rounded, SKClipOperation.Intersect, antialias: true);

            var sourceRect = new SKRect(cropX, cropY, cropX + side, cropY + side);

            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, sourceRect, destination, new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        return result;
    }

    private static void SaveScaled(SKBitmap master, int size, string path)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var scaled = master.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Не удалось уменьшить изображение.");

        using var image = SKImage.FromBitmap(scaled);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Не удалось закодировать PNG.");

        using var file = File.Create(path);
        data.SaveTo(file);
    }

    // ─────────────────────────── Упаковка ───────────────────────────

    /// <summary>
    /// Собирает ICNS: заголовок «icns», затем блоки «тип + длина + данные».
    ///
    /// ВАЖНО: все длины в этом формате записываются в ПОРЯДКЕ ОТ СТАРШЕГО БАЙТА.
    /// Первая версия писала их через BinaryWriter, то есть от младшего, и файл
    /// молча получался нечитаемым: macOS просто не показывала иконку, без ошибки.
    /// Проверяется командой: iconutil -c iconset AppIcon.icns -o /tmp/out.iconset
    /// </summary>
    private static void BuildIcns(IReadOnlyDictionary<int, string> pngPaths, string path)
    {
        var types = new (int Size, string Type)[]
        {
            (16, "icp4"),
            (32, "icp5"),
            (64, "icp6"),
            (128, "ic07"),
            (256, "ic08"),
            (512, "ic09"),
            (1024, "ic10"),
        };

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("icns"u8.ToArray());
        writer.Write(0);   // общая длина, заполним в конце

        long total = 8;

        foreach (var (size, type) in types)
        {
            if (!pngPaths.TryGetValue(size, out var pngPath))
            {
                continue;
            }

            var payload = File.ReadAllBytes(pngPath);

            writer.Write(System.Text.Encoding.ASCII.GetBytes(type));
            WriteUInt32BigEndian(writer, payload.Length + 8);
            writer.Write(payload);

            total += payload.Length + 8;
        }

        stream.Position = 4;
        WriteUInt32BigEndian(writer, (int)total);
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    /// <summary>
    /// Собирает ICO. В отличие от ICNS, здесь порядок байт МЛАДШИЙ — форматы
    /// из разных миров, и путать их нельзя.
    /// Современные Windows понимают PNG внутри ICO, поэтому в BMP не переводим.
    /// </summary>
    private static void BuildIco(IReadOnlyDictionary<int, string> pngPaths, string path)
    {
        var sizes = new[] { 16, 32, 48, 64, 128, 256 };
        var entries = new List<(int Size, byte[] Data)>();

        foreach (var size in sizes)
        {
            var source = pngPaths.TryGetValue(size, out var exact)
                ? exact
                : pngPaths.OrderBy(kv => Math.Abs(kv.Key - size)).First().Value;

            entries.Add((size, File.ReadAllBytes(source)));
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        var offset = 6 + (entries.Count * 16);

        foreach (var (size, data) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);

            offset += data.Length;
        }

        foreach (var (_, data) in entries)
        {
            writer.Write(data);
        }
    }
}
