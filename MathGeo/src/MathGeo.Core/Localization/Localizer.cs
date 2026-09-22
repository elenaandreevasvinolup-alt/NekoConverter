using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MathGeo.Core;

/// <summary>一个语言包的元信息。</summary>
public sealed record LocaleInfo(string Lang, string Name, bool Rtl, bool Reviewed);

/// <summary>
/// 本地化。
///
/// 一条铁律：**内核里不许出现面向用户的自然语言字符串**。
/// 诊断只带 code 和参数，文案在渲染时查表。这是 16 语言能成立的前提 ——
/// 如果内核把中文写进消息里，翻译就只能靠替换字符串，那种东西维护不到 16 种语言。
///
/// 语言文件是纯 JSON、扁平点号键、一个语言一个文件。打包前它就是一个普通文件，
/// 可以直接丢给译者改（不需要装任何工具、不需要重新编译）；打包后同一份内容进扩展包。
///
/// 加载顺序（后者覆盖前者）：
///   1. 程序集内嵌的内置语言（中英 + 其余 14 种的英文占位）
///   2. 可执行文件旁边的 Locales/ 目录 —— 译者改完不用重新编译就能看到效果
///   3. 扩展包（由上层调用 LoadFile 挂进来）
/// </summary>
public sealed class Localizer
{
    private const string ResourcePrefix = "MathGeo.Locale.";

    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fallback;

    private Localizer(string lang, Dictionary<string, string> strings, Dictionary<string, string> fallback)
    {
        Lang = lang;
        _strings = strings;
        _fallback = fallback;
    }

    public string Lang { get; }

    /// <summary>
    /// 已加载的键集合。测试和翻译工具用它做键完整性校验 ——
    /// "16 种语言键必须完全一致"这条约束只有能枚举键才检查得了。
    /// </summary>
    public IReadOnlyCollection<string> Keys => _strings.Keys;

    public bool Rtl { get; private init; }

    /// <summary>
    /// 当前语言。内核内部渲染诊断时用它。
    /// 是可变的全局状态，但对一个"内核 + 外壳"的产品来说，这比把 Localizer
    /// 顺着每一层调用传下去要现实得多。
    ///
    /// 初值跟随系统语言：中文环境下打开就是中文，否则退到英文。
    /// 这是"中国软件必须默认中文"和"英文全世界都看得懂"两条同时成立的落点。
    /// </summary>
    public static Localizer Current { get; private set; } = ResolveInitial();

    /// <summary>已安装的语言（来自内嵌资源 + 磁盘上的 Locales/）。</summary>
    public static IReadOnlyList<LocaleInfo> Installed => Discover();

    public string this[string key] => Get(key);

    /// <summary>取一条文案。找不到就返回键名本身 —— 那比返回空白更容易定位问题。</summary>
    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    /// <summary>带占位符的文案。模板本身有问题时退回原文，绝不抛异常。</summary>
    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args.Length == 0) return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>切换当前语言。语言不存在时保持原样，不抛异常。</summary>
    public static Localizer Use(string lang)
    {
        var loaded = Load(lang);
        Current = loaded;
        return loaded;
    }

    public static Localizer Load(string lang)
    {
        var requested = Read(lang);
        var english = lang == "en" ? requested : Read("en");

        return new Localizer(lang, requested, english) { Rtl = ReadMeta(lang).Rtl };
    }

    /// <summary>把一个语言文件挂进来（扩展包、译者手改的文件都走这里）。</summary>
    public void LoadFile(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            Merge(File.ReadAllText(path), _strings);
        }
        catch (Exception)
        {
            // 一个坏的语言文件不该让整个界面变空白。忽略它，继续用已有文案。
        }
    }

    /// <summary>跟随系统语言挑一个初始语言。找不到就退到英文。</summary>
    private static Localizer ResolveInitial()
    {
        try
        {
            var installed = Installed;

            var culture = CultureInfo.CurrentUICulture;
            if (Match(installed, culture.Name) is { } exact) return Load(exact);

            // 退到语言主标签："zh-Hant-TW" → "zh-Hant"，"en-GB" → "en"
            if (Match(installed, culture.Parent?.Name) is { } parent) return Load(parent);
        }
        catch (Exception)
        {
            // 取系统语言失败不该让内核起不来。
        }

        return Load("en");
    }

    private static string? Match(IReadOnlyList<LocaleInfo> installed, string? lang)
        => string.IsNullOrEmpty(lang)
            ? null
            : installed.FirstOrDefault(l => string.Equals(l.Lang, lang, StringComparison.OrdinalIgnoreCase))?.Lang;

    // ————————————————————————— 读取 —————————————————————————

    private static Dictionary<string, string> Read(string lang)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // 先读内嵌的，再读磁盘的 —— 磁盘上的覆盖内嵌的，于是译者改完立刻生效。
        var embedded = ReadEmbedded(lang);
        if (embedded is not null) Merge(embedded, result);

        var path = FindOnDisk(lang);
        if (path is not null)
        {
            try { Merge(File.ReadAllText(path), result); }
            catch (Exception) { /* 同上：坏文件不致命 */ }
        }

        return result;
    }

    private static string? ReadEmbedded(string lang)
    {
        var assembly = typeof(Localizer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{ResourcePrefix}strings.{lang}.json");
        if (stream is null) return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? FindOnDisk(string lang)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Locales", $"strings.{lang}.json"),
            Path.Combine(AppContext.BaseDirectory, $"strings.{lang}.json"),
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    /// <summary>
    /// 用 JsonDocument 手工遍历，而不是反序列化成 Dictionary。
    /// 反序列化要一个源生成的解析器，而这里只需要读一层扁平字符串，
    /// 手工走一遍既 AOT 安全又少一层依赖。
    /// </summary>
    private static void Merge(string json, Dictionary<string, string> into)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            if (property.Name == "_meta") continue;   // 元信息不是文案
            into[property.Name] = property.Value.GetString() ?? string.Empty;
        }
    }

    private static LocaleInfo ReadMeta(string lang)
    {
        try
        {
            var json = ReadEmbedded(lang) ?? (FindOnDisk(lang) is { } path ? File.ReadAllText(path) : null);
            if (json is null) return new LocaleInfo(lang, lang, false, false);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("_meta", out var meta))
                return new LocaleInfo(lang, lang, false, false);

            return new LocaleInfo(
                GetString(meta, "lang") ?? lang,
                GetString(meta, "name") ?? lang,
                meta.TryGetProperty("rtl", out var rtl) && rtl.ValueKind == JsonValueKind.True,
                meta.TryGetProperty("reviewed", out var reviewed) && reviewed.ValueKind == JsonValueKind.True);
        }
        catch (Exception)
        {
            return new LocaleInfo(lang, lang, false, false);
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 扫描内嵌资源与磁盘目录，列出所有可用语言。
    /// 磁盘上的目录会覆盖内嵌的元信息，这样译者把 reviewed 改成 true 就能生效。
    /// </summary>
    private static List<LocaleInfo> Discover()
    {
        var languages = new SortedSet<string>(StringComparer.Ordinal);
        var assembly = typeof(Localizer).Assembly;

        foreach (var name in assembly.GetManifestResourceNames())
        {
            var lang = ExtractLanguage(name);
            if (lang is not null) languages.Add(lang);
        }

        var diskDirectory = Path.Combine(AppContext.BaseDirectory, "Locales");
        if (Directory.Exists(diskDirectory))
            foreach (var file in Directory.EnumerateFiles(diskDirectory, "strings.*.json"))
            {
                var lang = ExtractLanguage(Path.GetFileName(file));
                if (lang is not null) languages.Add(lang);
            }

        return [.. languages.Select(ReadMeta)];
    }

    private static string? ExtractLanguage(string name)
    {
        const string prefix = "strings.";
        const string suffix = ".json";

        var start = name.LastIndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = name.LastIndexOf(suffix, StringComparison.Ordinal);
        if (end <= start) return null;

        return name[(start + prefix.Length)..end];
    }
}

/// <summary>诊断消息的渲染入口。诊断本身只带 code 和参数。</summary>
public static class DiagnosticText
{
    /// <summary>诊断 code → 语言文件里的键。加前缀是为了和 ui./cli. 分开命名空间。</summary>
    public static string KeyFor(string code) => "diag." + code;

    public static string Format(string code, IReadOnlyList<string> args)
        => Localizer.Current.Format(KeyFor(code), [.. args.Cast<object?>()]);
}
