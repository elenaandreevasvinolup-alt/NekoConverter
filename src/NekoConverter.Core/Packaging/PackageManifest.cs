using System.Text.Json.Serialization;
using NekoConverter.Core.Formats;

namespace NekoConverter.Core.Packaging;

/// <summary>Тип пакета.</summary>
public static class PackageKind
{
    /// <summary>Пакет форматов: то, что даёт приложению новые возможности.</summary>
    public const string Format = "format";

    /// <summary>
    /// Пакет-зависимость: оригинальное стороннее ПО (FFmpeg, Pandoc и т. п.).
    /// Такие пакеты помечаются постоянными и не предлагаются к удалению в интерфейсе.
    /// </summary>
    public const string Dependency = "dependency";
}

/// <summary>
/// Манифест пакета. Один и тот же формат файла используется:
///   • ядром (formats.json рядом с исполняемым файлом),
///   • установленным пакетом (package.json в его каталоге),
///   • каталогом доступных пакетов (catalog.json).
///
/// Как и таблица форматов, это ДАННЫЕ: новый пакет не требует перекомпиляции.
/// </summary>
public sealed class PackageManifest
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    /// <summary>Идентификатор пакета. У ядра — null.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = PackageKind.Format;

    /// <summary>Название по локалям: { "zh-Hans": "音频 / 视频", "en": "Audio / Video" }.</summary>
    [JsonPropertyName("name")]
    public Dictionary<string, string> Name { get; set; } = [];

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public Dictionary<string, string> Description { get; set; } = [];

    /// <summary>
    /// Постоянный пакет не предлагается к удалению: это зависимости, которые
    /// ставятся один раз и нужны нескольким пакетам форматов сразу.
    /// </summary>
    [JsonPropertyName("permanent")]
    public bool Permanent { get; set; }

    /// <summary>Идентификаторы пакетов, которые должны быть установлены до этого.</summary>
    [JsonPropertyName("requires")]
    public List<string> Requires { get; set; } = [];

    /// <summary>Движки, которые появляются в системе вместе с этим пакетом.</summary>
    [JsonPropertyName("providesEngines")]
    public List<string> ProvidesEngines { get; set; } = [];

    /// <summary>Движки, без которых пакет бесполезен (например, внешняя программа).</summary>
    [JsonPropertyName("requiresEngines")]
    public List<string> RequiresEngines { get; set; } = [];

    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>Откуда взято оригинальное ПО. Нужно для соблюдения условий лицензий.</summary>
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    // ─────────── Только для каталога и установки ───────────

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Дополнительные адреса того же архива.
    /// Оставлено для совместимости; предпочтительнее заполнять sources.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<string> Mirrors { get; set; } = [];

    /// <summary>
    /// Все адреса одного и того же архива с подписями.
    ///
    /// Нужны из-за сетей, где зарубежные площадки работают нестабильно:
    /// приложение замеряет все адреса сразу и берёт тот, что ответил быстрее,
    /// а при обрыве переходит к следующему. Контрольная сумма общая —
    /// архив один и тот же, меняется только путь к нему.
    /// </summary>
    [JsonPropertyName("sources")]
    public List<DownloadSource> Sources { get; set; } = [];

    /// <summary>
    /// Куда распаковывать: packages (по умолчанию) или locale.
    /// Языковые пакеты ставятся в каталог переводов, а не в каталог движков.
    /// </summary>
    [JsonPropertyName("installTo")]
    public string InstallTo { get; set; } = "packages";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>Контрольная сумма архива. Если пусто — проверка пропускается с предупреждением.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Формат архива: zip или tar.gz. Пусто — файл копируется как есть.</summary>
    [JsonPropertyName("archive")]
    public string? Archive { get; set; }

    /// <summary>
    /// Что удалить после распаковки: пути относительно корня пакета.
    ///
    /// Нужно из-за размеров. Сборка FFmpeg для Windows содержит ffplay.exe (102 МБ)
    /// и ffprobe.exe (100 МБ), которыми мы не пользуемся, и ещё 13 МБ документации.
    /// Без этой чистки офлайн-набор распухает вдвое.
    /// </summary>
    [JsonPropertyName("prune")]
    public List<string> Prune { get; set; } = [];

    /// <summary>Путь к исполняемому файлу внутри пакета, относительно его каталога.</summary>
    [JsonPropertyName("executable")]
    public string? Executable { get; set; }

    /// <summary>
    /// Платформы, для которых годится пакет (например ["osx-x64","osx-arm64"]).
    /// Пусто — годится для всех. Позволяет держать в одном каталоге две сборки
    /// FFmpeg под одним идентификатором и выбирать нужную автоматически.
    /// </summary>
    [JsonPropertyName("platforms")]
    public List<string> Platforms { get; set; } = [];

    /// <summary>Идентификаторы форматов, которые появятся после установки (для витрины).</summary>
    [JsonPropertyName("providesFormats")]
    public List<string> ProvidesFormats { get; set; } = [];

    // ─────────── Форматы (у ядра и у пакетов форматов) ───────────

    [JsonPropertyName("formats")]
    public List<FormatEntry> Formats { get; set; } = [];

    /// <summary>
    /// Имя файла архива на площадке. Если не задано, строится как «id-version.zip» —
    /// именно так называются файлы, которые готовит tools/make_release.py.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>Имя файла архива с учётом значения по умолчанию.</summary>
    public string ResolvedFileName =>
        !string.IsNullOrWhiteSpace(FileName)
            ? FileName
            : $"{Id}-{Version}.zip";

    /// <summary>Все адреса архива: основной, зеркала и именованные источники.</summary>
    public IReadOnlyList<string> AllSources()
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(DownloadUrl))
        {
            list.Add(DownloadUrl);
        }

        list.AddRange(Mirrors.Where(m => !string.IsNullOrWhiteSpace(m)));
        list.AddRange(Sources.Where(s => !string.IsNullOrWhiteSpace(s.Url)).Select(s => s.Url));

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Подходит ли пакет текущей платформе.</summary>
    public bool MatchesCurrentPlatform() => MatchesPlatform(CurrentPlatformId());

    /// <summary>Подходит ли пакет указанной платформе.</summary>
    public bool MatchesPlatform(string platformId)
    {
        if (Platforms.Count == 0)
        {
            return true;
        }

        return Platforms.Contains(platformId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Идентификатор текущей платформы в формате RID: osx-x64, osx-arm64, win-x64.</summary>
    public static string CurrentPlatformId()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "unknown",
        };

        var system = OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsWindows() ? "win"
            : "linux";

        return $"{system}-{architecture}";
    }

    /// <summary>Локализованное название с откатом на английский, затем на идентификатор.</summary>
    public string DisplayName(string locale = "zh-Hans")
    {
        if (Name.TryGetValue(locale, out var localized) && localized.Length > 0)
        {
            return localized;
        }

        if (Name.TryGetValue("en", out var english) && english.Length > 0)
        {
            return english;
        }

        return Id ?? "core";
    }
}

/// <summary>
/// Базовые адреса площадок, где лежат архивы.
///
/// Заполняются ОДИН раз на весь каталог, а не в каждом пакете: имена файлов
/// строятся по одному правилу, и дублировать три ссылки для каждого пакета
/// было бы источником опечаток. Достаточно вписать свой аккаунт и версию.
///
/// Все три площадки бесплатны и независимы, поэтому служат друг другу резервом:
/// приложение замеряет их одновременно и берёт быстрейшую.
/// </summary>
public sealed class ReleaseBases
{
    /// <summary>GitHub Releases. Основная площадка, работает везде.</summary>
    [JsonPropertyName("github")]
    public string? GitHub { get; set; }

    /// <summary>Gitee (码云). Китайский аналог GitHub, из Китая открывается напрямую.</summary>
    [JsonPropertyName("gitee")]
    public string? Gitee { get; set; }

    /// <summary>
    /// jsDelivr: бесплатный CDN поверх файлов репозитория GitHub.
    /// Есть узлы в Китае, но файлы крупнее 20 МБ он не отдаёт.
    /// </summary>
    [JsonPropertyName("jsdelivr")]
    public string? JsDelivr { get; set; }

    public IEnumerable<(string Url, string Label)> Expand(string fileName)
    {
        if (!string.IsNullOrWhiteSpace(GitHub))
        {
            yield return (Combine(GitHub, fileName), "GitHub");
        }

        if (!string.IsNullOrWhiteSpace(Gitee))
        {
            yield return (Combine(Gitee, fileName), "Gitee");
        }

        if (!string.IsNullOrWhiteSpace(JsDelivr))
        {
            yield return (Combine(JsDelivr, fileName), "jsDelivr");
        }
    }

    private static string Combine(string baseUrl, string fileName) =>
        baseUrl.TrimEnd('/') + "/" + fileName;
}

/// <param name="Url">Адрес архива.</param>
/// <param name="Label">Подпись для человека: «GitHub», «国内镜像» и так далее.</param>
public sealed class DownloadSource
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>Каталог доступных пакетов. Может лежать рядом с приложением или скачиваться по URL.</summary>
public sealed class PackageCatalog
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; } = 1;

    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    /// <summary>Общие базовые адреса площадок для всех пакетов каталога.</summary>
    [JsonPropertyName("releaseBase")]
    public ReleaseBases? ReleaseBase { get; set; }

    [JsonPropertyName("packages")]
    public List<PackageManifest> Packages { get; set; } = [];

    /// <summary>
    /// Оставляет только пакеты, подходящие указанной платформе.
    /// Благодаря этому в каталоге могут лежать две сборки FFmpeg под одним
    /// идентификатором, и нужная выбирается сама.
    /// </summary>
    /// <param name="platformId">
    /// Идентификатор вида win-x64. null — текущая платформа.
    /// Явное значение нужно при сборке офлайн-набора для другой платформы.
    /// </param>
    public PackageCatalog ForPlatform(string? platformId = null)
    {
        var target = platformId ?? PackageManifest.CurrentPlatformId();

        var selected = Packages.Where(p => p.MatchesPlatform(target)).ToList();

        // Разворачиваем шаблоны площадок в конкретные ссылки: дальше по коду
        // пакет работает с обычным списком адресов и о шаблонах не знает.
        if (ReleaseBase is { } bases)
        {
            foreach (var package in selected)
            {
                foreach (var (url, label) in bases.Expand(package.ResolvedFileName))
                {
                    package.Sources.Add(new DownloadSource { Url = url, Label = label });
                }
            }
        }

        return new PackageCatalog
        {
            Schema = Schema,
            Updated = Updated,
            ReleaseBase = ReleaseBase,
            Packages = selected,
        };
    }
}

/// <summary>Этап установки, о котором сообщается в интерфейс.</summary>
public enum InstallStage
{
    Resolving,
    Downloading,
    Verifying,
    Extracting,
    Finishing,
    Done,
    Failed,
}

/// <param name="Stage">Текущий этап.</param>
/// <param name="PackageId">Какой пакет обрабатывается.</param>
/// <param name="BytesReceived">Скачано байт.</param>
/// <param name="TotalBytes">Всего байт, 0 если неизвестно.</param>
/// <param name="Message">Человекочитаемое пояснение.</param>
public sealed record InstallProgress(
    InstallStage Stage,
    string PackageId,
    long BytesReceived = 0,
    long TotalBytes = 0,
    string? Message = null)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes, 0, 1) : 0;
}
