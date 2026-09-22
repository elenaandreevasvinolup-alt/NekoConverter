using PdfSharp.Fonts;

namespace NekoConverter.Core.Documents;

/// <summary>
/// Поставщик шрифта для PDFsharp на не-Windows платформах.
///
/// Зачем это нужно: PDFsharp умеет искать шрифты только в системном каталоге Windows,
/// поэтому на macOS/Linux без своего резолвера он падает с ошибкой "no appropriate font found".
///
/// Дополнительно решается главная боль китайского/русского текста: в PDF шрифт обязан быть
/// встроен, иначе вместо иероглифов будут пустые прямоугольники.
///
/// ВАЖНОЕ ОГРАНИЧЕНИЕ: поддерживаются только одиночные TTF-файлы.
/// Коллекции TTC (PingFang.ttc, msyh.ttc, NotoSansCJK-Regular.ttc) в PDFsharp 6.2.4
/// нельзя выбрать через IFontResolver — свойство CollectionNumber не является публичным.
/// Поэтому в списке кандидатов остаются только TTF.
///
/// Путь к шрифту можно переопределить переменной окружения NEKOCONVERTER_FONT.
/// </summary>
public sealed class SystemCjkFontResolver : IFontResolver
{
    /// <summary>Внутреннее имя семейства, под которым шрифт виден для XFont.</summary>
    public const string FaceName = "NekoCJK";

    private const string EnvVarName = "NEKOCONVERTER_FONT";

    private static readonly string[] Candidates =
    [
        // macOS: единственный системный TTF с полным набором CJK
        "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
        "/Library/Fonts/Arial Unicode.ttf",
        // Windows
        @"C:\Windows\Fonts\msyh.ttf",
        @"C:\Windows\Fonts\simsun.ttf",
        // Linux
        "/usr/share/fonts/truetype/noto/NotoSansCJKsc-Regular.otf",
        "/usr/share/fonts/truetype/arphic/uming.ttc",
    ];

    private static readonly Lazy<SystemCjkFontResolver> LazyInstance = new(Create);

    private readonly byte[] _fontData;

    private SystemCjkFontResolver(string path, byte[] fontData)
    {
        Path = path;
        _fontData = fontData;
    }

    /// <summary>Путь к реально использованному файлу шрифта.</summary>
    public string Path { get; }

    public static SystemCjkFontResolver Instance => LazyInstance.Value;

    /// <summary>Один раз регистрирует резолвер в PDFsharp. Вызывать до создания XFont.</summary>
    public static void EnsureInitialized()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = Instance;
        }
    }

    private static SystemCjkFontResolver Create()
    {
        var overridePath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new FileNotFoundException(
                    $"The font file from {EnvVarName} was not found: {overridePath}");
            }

            return Load(overridePath);
        }

        foreach (var candidate in Candidates)
        {
            if (File.Exists(candidate))
            {
                return Load(candidate);
            }
        }

        throw new FileNotFoundException(
            "No system TTF font with CJK support was found. " +
            $"Set the path manually via the {EnvVarName} environment variable.");
    }

    private static SystemCjkFontResolver Load(string path) =>
        new(path, File.ReadAllBytes(path));

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Отдаём один и тот же файл на все запросы, а начертания эмулируем:
        // жирный и курсив в системных CJK-шрифтах обычно отсутствуют как отдельные файлы.
        return new FontResolverInfo(FaceName, isBold, isItalic);
    }

    public byte[] GetFont(string faceName) => _fontData;
}
