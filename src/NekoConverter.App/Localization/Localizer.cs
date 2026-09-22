using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NekoConverter.App.Localization;

/// <summary>
/// Локализация интерфейса.
///
/// Языковые файлы ищутся в трёх местах, в порядке приоритета:
///   1. Каталог пользовательских данных — сюда можно положить свой перевод,
///      и он подхватится при следующем запуске без пересборки приложения.
///   2. Каталог приложения — то, что поставляется вместе с ним.
///   3. Встроенный ресурс сборки — нужен Android, где приложение лежит внутри APK.
///
/// Набор языков определяется тем, что реально найдено на диске, а не списком в коде:
/// положили strings.nl.json — в настройках появится нидерландский.
///
/// Имя языка и направление текста берутся из самого файла, ключами «_name» и «_rtl».
/// Так переводчик сам указывает, как называть его язык, и для нового языка
/// не нужно править код.
/// </summary>
public static class Localizer
{
    /// <summary>Язык по умолчанию. Английский: он понятен шире остальных.</summary>
    public const string DefaultCode = "en";

    /// <summary>Запасной язык, встроенный в сборку. Используется, если файла нет.</summary>
    public const string FallbackCode = "en";

    private const string NameKey = "_name";
    private const string RtlKey = "_rtl";

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _current = [];

    /// <summary>Доступные языки в порядке отображения: сначала английский, затем по алфавиту.</summary>
    public static IReadOnlyList<LanguageInfo> Languages { get; private set; } = [];

    public static string CurrentCode { get; private set; } = DefaultCode;

    public static bool IsRightToLeft { get; private set; }

    /// <summary>Где лежат пользовательские переводы. Путь задаёт приложение при запуске.</summary>
    public static string? UserLocaleDirectory { get; set; }

    /// <param name="Code">Код языка, например «zh-Hans».</param>
    /// <param name="NativeName">Название на самом языке.</param>
    /// <param name="RightToLeft">Правостороннее письмо (арабский, иврит).</param>
    /// <param name="Source">Откуда взят перевод — для диагностики.</param>
    public sealed record LanguageInfo(string Code, string NativeName, bool RightToLeft, string Source);

    /// <summary>Строка по ключу. Если ключа нет — возвращается сам ключ, а не пустота:
    /// так пропуск перевода сразу видно в интерфейсе и не превращается в дыру.</summary>
    public static string Get(string key) =>
        _current.TryGetValue(key, out var value) ? value : key;

    /// <summary>Строка с подстановкой: {0}, {1} и так далее.</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // Ошибка в шаблоне перевода не должна ронять интерфейс.
            return template;
        }
    }

    /// <summary>
    /// Перечитывает список языков с диска. Вызывается при запуске и после того,
    /// как пользователь положил новый файл перевода.
    /// </summary>
    public static void RefreshAvailableLanguages()
    {
        var found = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in CandidateDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "strings.*.json"))
            {
                var code = ExtractCode(file);
                if (code is null || found.ContainsKey(code))
                {
                    continue;
                }

                var strings = Load(code);
                if (strings.Count == 0)
                {
                    continue;
                }

                var name = strings.GetValueOrDefault(NameKey, code);
                var rtl = strings.TryGetValue(RtlKey, out var rtlText) &&
                          bool.TryParse(rtlText, out var parsed) && parsed;

                found[code] = new LanguageInfo(code, name, rtl, directory);
            }
        }

        // Встроенные языки: они есть всегда, даже если файлы не скопировались.
        foreach (var code in EmbeddedCodes())
        {
            if (!found.ContainsKey(code))
            {
                var strings = Load(code);
                var name = strings.GetValueOrDefault(NameKey, code);
                var rtl = strings.TryGetValue(RtlKey, out var rtlText) &&
                          bool.TryParse(rtlText, out var parsed) && parsed;

                found[code] = new LanguageInfo(code, name, rtl, "embedded");
            }
        }

        Languages = found.Values
            .OrderBy(l => l.Code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l.NativeName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Загружает язык и подменяет словарь ресурсов приложения.
    /// Возвращает false, если языка нет — тогда остаётся прежний.
    /// </summary>
    public static bool Apply(string code)
    {
        var strings = Load(code);
        if (strings.Count == 0)
        {
            return false;
        }

        _current = strings;
        CurrentCode = code;
        IsRightToLeft = strings.TryGetValue(RtlKey, out var rtlText) &&
                        bool.TryParse(rtlText, out var rtl) && rtl;

        if (Application.Current is { } app)
        {
            app.Resources.MergedDictionaries.Clear();

            var dictionary = new ResourceDictionary();
            foreach (var (key, value) in strings)
            {
                // Служебные ключи в словарь ресурсов не кладём: это метаданные языка,
                // а не строки интерфейса.
                if (key.StartsWith('_'))
                {
                    continue;
                }

                dictionary["Loc." + key] = value;
            }

            app.Resources.MergedDictionaries.Add(dictionary);
        }

        return true;
    }

    /// <summary>Направление текста для текущего языка.</summary>
    public static FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    // ─────────────────────────── Загрузка ───────────────────────────

    private static IEnumerable<string> CandidateDirectories()
    {
        // Пользовательский каталог идёт первым: его перевод важнее поставляемого.
        if (!string.IsNullOrEmpty(UserLocaleDirectory))
        {
            yield return UserLocaleDirectory;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "Locale");
    }

    private static string? ExtractCode(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dot = name.IndexOf('.');

        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : null;
    }

    private static Dictionary<string, string> Load(string code)
    {
        if (Cache.TryGetValue(code, out var cached))
        {
            return cached;
        }

        var json = ReadLocaleFile(code) ?? ReadLocaleFile(FallbackCode);
        if (json is null)
        {
            return [];
        }

        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, LocalizationJsonContext.Default.DictionaryStringString);
        }
        catch
        {
            parsed = null;
        }

        if (parsed is null || parsed.Count == 0)
        {
            return [];
        }

        Cache[code] = parsed;
        return parsed;
    }

    /// <summary>Пользовательский каталог, затем каталог приложения, затем встроенный ресурс.</summary>
    private static string? ReadLocaleFile(string code)
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, $"strings.{code}.json");

            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch
            {
                // Не читается — пробуем следующее место.
            }
        }

        var assembly = typeof(Localizer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"NekoConverter.Locale.strings.{code}.json");

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Коды языков, встроенных в сборку. Нужны, если файлы на диске отсутствуют.</summary>
    private static IEnumerable<string> EmbeddedCodes()
    {
        var assembly = typeof(Localizer).Assembly;
        const string prefix = "NekoConverter.Locale.strings.";
        const string suffix = ".json";

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return name[prefix.Length..^suffix.Length];
            }
        }
    }
}

/// <summary>Контекст сериализации для Native AOT.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
