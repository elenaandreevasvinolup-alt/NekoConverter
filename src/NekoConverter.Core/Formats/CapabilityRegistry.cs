using System.Text.Json.Serialization;
using NekoConverter.Core.Engines.External;
using NekoConverter.Core.Packaging;

namespace NekoConverter.Core.Formats;

/// <summary>Категория конвертации. Определяет, какой движок обрабатывает файл.</summary>
public enum FormatKind
{
    Image,
    Audio,
    Video,
    Document,
    Data,
    Subtitle,
    Model3D,
}

/// <summary>
/// Описание одного формата.
///
/// ВАЖНО: таблица форматов — это ДАННЫЕ, а не код. Добавить формат можно
/// правкой formats.json без перекомпиляции, как блоки в NekoScriptGraph.
/// </summary>
/// <param name="Id">Идентификатор формата, обычно расширение ("png").</param>
/// <param name="Kind">Категория.</param>
/// <param name="Extensions">Расширения файлов, включая альтернативные.</param>
/// <param name="CanRead">Можно ли читать этот формат.</param>
/// <param name="CanWrite">Можно ли писать этот формат (то есть выбирать его как целевой).</param>
/// <param name="Engine">Движок: skia / sips / pcm / subtitle / data / ffmpeg / ...</param>
/// <param name="ModuleId">null — встроено в ядро; иначе идентификатор модуля.</param>
/// <param name="Label">Человекочитаемое имя для интерфейса.</param>
/// <param name="Note">Короткая подпись: "有损压缩", "无损", "未压缩" и т.п.</param>
/// <param name="ReadEngines">Движки чтения (минимум один, если CanRead).</param>
/// <param name="WriteEngines">Движки записи (минимум один, если CanWrite).</param>
public sealed record FormatDescriptor(
    string Id,
    FormatKind Kind,
    IReadOnlyList<string> Extensions,
    bool CanRead,
    bool CanWrite,
    string Engine,
    string? ModuleId,
    string? Label,
    string? Note,
    IReadOnlyList<string> ReadEngines,
    IReadOnlyList<string> WriteEngines);

/// <summary>Одна строка таблицы форматов внутри манифеста.</summary>
public sealed class FormatEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "image";

    [JsonPropertyName("ext")]
    public List<string> Extensions { get; set; } = [];

    [JsonPropertyName("read")]
    public bool CanRead { get; set; } = true;

    [JsonPropertyName("write")]
    public bool CanWrite { get; set; }

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "skia";

    /// <summary>
    /// Движки, которые умеют ЧИТАТЬ формат. Пусто — берётся engine.
    /// Нужны там, где чтение и запись обслуживают разные движки:
    /// DOCX читается встроенным кодом, а пишется только через Pandoc.
    /// </summary>
    [JsonPropertyName("readEngines")]
    public List<string>? ReadEngines { get; set; }

    /// <summary>Движки, которые умеют ПИСАТЬ формат. Пусто — берётся engine.</summary>
    [JsonPropertyName("writeEngines")]
    public List<string>? WriteEngines { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>
/// Единая таблица возможностей: ядро плюс все установленные модули.
/// Интерфейс строится только по ней, поэтому "показываем лишь то, что реально есть"
/// выполняется автоматически.
/// </summary>
public sealed class CapabilityRegistry
{
    private readonly List<FormatDescriptor> _all = [];
    private readonly Dictionary<string, FormatDescriptor> _byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FormatDescriptor> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PackageManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _availableEngines;
    private readonly List<string> _duplicateExtensions = [];

    private CapabilityRegistry(HashSet<string> availableEngines)
    {
        _availableEngines = availableEngines;
    }

    public IReadOnlyList<FormatDescriptor> All => _all;

    /// <summary>Манифесты: ключ null-эквивалент "core" для ядра.</summary>
    public IReadOnlyDictionary<string, PackageManifest> Manifests => _manifests;

    /// <summary>
    /// Расширения, объявленные более чем в одной строке таблицы.
    ///
    /// Это не безобидная мелочь: поиск формата идёт по первому совпадению, поэтому
    /// «xlsx», случайно попавший и в документы, и в данные, молча уводит формат
    /// в неверную категорию. Ошибка была настоящей и нашлась именно так — теперь
    /// список доступен снаружи, и CLI показывает его сразу.
    /// </summary>
    public IReadOnlyList<string> DuplicateExtensions => _duplicateExtensions;

    /// <summary>Загружает ядро и, если есть, каталог модулей рядом с приложением.</summary>
    /// <summary>
    /// Собирает таблицу возможностей: ядро плюс все установленные пакеты.
    ///
    /// Движок считается доступным, если он встроен в приложение ИЛИ его приносит
    /// установленный пакет. Именно поэтому после установки dep-ffmpeg форматы
    /// с движком ffmpeg сразу становятся доступными, без перекомпиляции.
    /// </summary>
    public static CapabilityRegistry Load(
        string coreFormatsPath,
        string? packagesDirectory = null,
        string? bundledDirectory = null)
    {
        var installed = new List<PackageManifest>();

        CollectManifests(packagesDirectory, installed);
        CollectManifests(bundledDirectory, installed);

        var engines = DetectAvailableEngines();
        foreach (var manifest in installed)
        {
            foreach (var engine in manifest.ProvidesEngines)
            {
                engines.Add(engine);
            }
        }

        var registry = new CapabilityRegistry(engines);
        registry.AddManifest(LoadCoreManifest(coreFormatsPath));

        foreach (var manifest in installed)
        {
            // Пакет бесполезен, пока не установлены его зависимости.
            var dependenciesSatisfied = manifest.Requires.All(required =>
                installed.Any(other => string.Equals(other.Id, required, StringComparison.OrdinalIgnoreCase)));

            if (dependenciesSatisfied)
            {
                registry.AddManifest(manifest);
            }
        }

        return registry;
    }

    /// <summary>Собирает манифесты пакетов из каталога. Битый манифест пропускается.</summary>
    private static void CollectManifests(string? directory, List<PackageManifest> target)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(sub).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var manifestPath = Path.Combine(sub, "package.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = LoadManifest(manifestPath);

                // Первый найденный выигрывает: пользовательская установка важнее комплектной.
                if (!target.Any(m => string.Equals(m.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    target.Add(manifest);
                }
            }
            catch
            {
                // Повреждённый манифест пакета не должен ломать ядро.
            }
        }
    }

    private static PackageManifest LoadManifest(string path)
    {
        var json = File.ReadAllText(path);

        // Через исходный генератор, а не рефлексию: иначе в AOT-сборке будет исключение.
        return PackageJson.ParseManifest(json);
    }

    /// <summary>Загружает таблицу форматов ядра: с диска, а на Android — из ресурса.</summary>
    private static PackageManifest LoadCoreManifest(string path) =>
        PackageJson.ParseManifest(PackageJson.ReadData(path, "NekoConverter.formats.json"));

    private void AddManifest(PackageManifest manifest)
    {
        var key = manifest.Id ?? "core";
        _manifests[key] = manifest;

        foreach (var entry in manifest.Formats)
        {
            var kind = Enum.TryParse<FormatKind>(entry.Kind, ignoreCase: true, out var parsed)
                ? parsed
                : FormatKind.Image;

            var readEngines = entry.ReadEngines is { Count: > 0 } ? entry.ReadEngines : [entry.Engine];
            var writeEngines = entry.WriteEngines is { Count: > 0 } ? entry.WriteEngines : [entry.Engine];

            var descriptor = new FormatDescriptor(
                entry.Id,
                kind,
                entry.Extensions.Count > 0 ? entry.Extensions : [entry.Id],
                entry.CanRead,
                entry.CanWrite,
                entry.Engine,
                manifest.Id,
                entry.Label,
                entry.Note,
                readEngines,
                writeEngines);

            _all.Add(descriptor);
            _byId.TryAdd(descriptor.Id, descriptor);

            foreach (var extension in descriptor.Extensions)
            {
                // Первое вхождение выигрывает: ядро загружается до модулей.
                if (!_byExtension.TryAdd(extension, descriptor))
                {
                    _duplicateExtensions.Add($"{extension} ({_byExtension[extension].Id} / {descriptor.Id})");
                }
            }
        }
    }

    /// <summary>
    /// Какие движки реально доступны в этой сборке и на этой платформе.
    /// Движок sips существует только на macOS — на других платформах его форматы
    /// автоматически становятся "недоступны" и предлагаются как модуль.
    /// </summary>
    private static HashSet<string> DetectAvailableEngines()
    {
        // Список встроенных движков вынесен в BuiltinEngineSet: интерфейсу нужен
        // тот же перечень, чтобы отличать «работает сразу» от «нужен пакет».
        var engines = new HashSet<string>(BuiltinEngineSet, StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/sips"))
        {
            engines.Add("sips");
        }

        // CoreAudio: те же форматы, что и sips, но для звука.
        if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/afconvert"))
        {
            engines.Add("afconvert");
        }

        // Программы, которые уже стоят в системе. Если они есть, соответствующие
        // форматы становятся доступны без скачивания пакетов: приложение ничего
        // не весит, а человек ничего не ждёт.
        foreach (var engine in ExternalTools.Detected().Keys)
        {
            engines.Add(engine);
        }

        return engines;
    }

    public bool IsEngineAvailable(string engine) => _availableEngines.Contains(engine);

    /// <summary>
    /// Движки, встроенные в приложение, — в отличие от тех, что появляются
    /// только после установки пакета-зависимости.
    /// Нужно интерфейсу: он делит список движков на «работает сразу» и «нужен пакет».
    /// </summary>
    public static IReadOnlySet<string> BuiltinEngines => BuiltinEngineSet;

    private static readonly HashSet<string> BuiltinEngineSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "skia", "sips", "pcm", "afconvert", "subtitle", "data", "builtin", "assimp",
    };

    /// <summary>Движок встроен в приложение, а не приходит с пакетом.</summary>
    public static bool IsBuiltinEngine(string engine) => BuiltinEngineSet.Contains(engine);

    /// <summary>
    /// Формат доступен, если доступен хотя бы один из его движков.
    /// Для точных проверок есть CanReadNow и CanWriteNow.
    /// </summary>
    public bool IsAvailable(FormatDescriptor format) =>
        CanReadNow(format) || CanWriteNow(format) || IsEngineAvailable(format.Engine);

    /// <summary>Формат можно прочитать прямо сейчас.</summary>
    public bool CanReadNow(FormatDescriptor format) =>
        format.CanRead && format.ReadEngines.Any(IsEngineAvailable);

    /// <summary>Формат можно записать прямо сейчас.</summary>
    public bool CanWriteNow(FormatDescriptor format) =>
        format.CanWrite && format.WriteEngines.Any(IsEngineAvailable);

    /// <summary>Движки чтения, доступные в данный момент.</summary>
    public IReadOnlyList<string> AvailableReadEngines(FormatDescriptor format) =>
        format.ReadEngines.Where(IsEngineAvailable).ToList();

    /// <summary>Движки записи, доступные в данный момент.</summary>
    public IReadOnlyList<string> AvailableWriteEngines(FormatDescriptor format) =>
        format.WriteEngines.Where(IsEngineAvailable).ToList();

    /// <summary>
    /// Поиск формата по расширению, а если такого нет — по идентификатору.
    /// Так работают и «--to sub», и «--to microdvd».
    /// </summary>
    public FormatDescriptor? ByExtension(string extension)
    {
        var ext = extension.TrimStart('.');
        if (_byExtension.TryGetValue(ext, out var descriptor))
        {
            return descriptor;
        }

        return _byId.TryGetValue(ext, out var byId) ? byId : null;
    }

    public FormatDescriptor? ByPath(string path) => ByExtension(Path.GetExtension(path));

    public IEnumerable<FormatDescriptor> For(FormatKind kind) => _all.Where(f => f.Kind == kind);

    /// <summary>Форматы, которые можно выбрать как целевые: умеем писать и движок доступен.</summary>
    public IEnumerable<FormatDescriptor> WritableTargets(FormatKind kind) =>
        For(kind).Where(CanWriteNow);

    /// <summary>Форматы, которые мы умеем читать, но движка нет — их предложим как модуль.</summary>
    public IEnumerable<FormatDescriptor> Unavailable(FormatKind kind) =>
        For(kind).Where(f => f.CanRead && !IsAvailable(f));

    /// <summary>Определяет категорию файла по расширению. null — формат неизвестен.</summary>
    public FormatKind? KindOf(string path) => ByPath(path)?.Kind;
}
