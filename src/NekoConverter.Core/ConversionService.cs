using System.Diagnostics;
using NekoConverter.Core.Documents;
using NekoConverter.Core.Engines.Audio;
using NekoConverter.Core.Engines.Data;
using NekoConverter.Core.Engines.Documents;
using NekoConverter.Core.Engines.Image;
using NekoConverter.Core.Engines.Subtitles;
using NekoConverter.Core.Engines.External;
using NekoConverter.Core.Engines.Mesh;
using NekoConverter.Core.Engines.Video;
using NekoConverter.Core.Formats;
using NekoConverter.Core.Packaging;

namespace NekoConverter.Core;

/// <param name="InputPath">Входной файл.</param>
/// <param name="OutputPath">Куда писать. null — рядом с исходным файлом.</param>
/// <param name="TargetFormatId">Целевой формат, например "png" или "mp3". null — значение по умолчанию для категории.</param>
/// <param name="Quality">Качество для форматов с потерями, 1–100.</param>
/// <param name="SampleRate">Частота дискретизации для звука. null — как в источнике.</param>
/// <param name="Channels">Число каналов для звука. null — как в источнике.</param>
/// <param name="BitsPerSample">Разрядность для звука. null — 16.</param>
/// <param name="MaxDimension">Ограничение большей стороны изображения в пикселях. null — не масштабировать.</param>
/// <param name="Fps">Частота кадров для MicroDVD-субтитров. null — 25.</param>
/// <param name="OutputDirectory">
/// Каталог результата. Используется, только если не задан OutputPath:
/// имя файла берётся из исходного, а расширение — из целевого формата.
/// </param>
public sealed record ConversionRequest(
    string InputPath,
    string? OutputPath,
    string? TargetFormatId,
    int Quality = 90,
    int? SampleRate = null,
    int? Channels = null,
    int? BitsPerSample = null,
    int? MaxDimension = null,
    double? Fps = null,
    string? OutputDirectory = null);

/// <param name="Engine">Каким движком выполнена работа: полезно для диагностики.</param>
public sealed record ConversionResult(string OutputPath, long Bytes, TimeSpan Elapsed, string Engine);

/// <summary>
/// Единая точка входа в движок конвертации.
/// Этой же точкой пользуются CLI, GUI и MCP-сервер, чтобы логика не дублировалась.
///
/// Категория определяется по таблице форматов, а не по коду: добавить формат можно
/// правкой formats.json.
/// </summary>
public static class ConversionService
{
    private static readonly Lock RegistryLock = new();
    private static CapabilityRegistry? _registry;

    /// <summary>
    /// Таблица возможностей. Кэшируется, но пересобирается после установки или
    /// удаления пакета — иначе новый формат не появился бы до перезапуска приложения.
    /// </summary>
    public static CapabilityRegistry Registry
    {
        get
        {
            lock (RegistryLock)
            {
                return _registry ??= LoadRegistry();
            }
        }
    }

    /// <summary>Сбрасывает кэш: вызывается после изменения набора пакетов.</summary>
    public static void ReloadRegistry()
    {
        lock (RegistryLock)
        {
            _registry = null;
        }
    }

    /// <summary>
    /// Менеджер пакетов: установка, удаление, поиск зависимостей.
    /// Второй аргумент — офлайн-набор рядом с приложением, если он есть.
    /// </summary>
    public static PackageManager Packages { get; } =
        new(DataDirectory, PackageManager.FindBundledDirectory(AppContext.BaseDirectory));

    private static PackageCatalog? _catalog;

    /// <summary>
    /// Каталог доступных пакетов для произвольной платформы.
    /// Нужен при сборке офлайн-набора под другую систему.
    /// </summary>
    public static PackageCatalog CatalogFor(string? platformId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalog.json");

        try
        {
            return PackageJson
                .ParseCatalog(PackageJson.ReadData(path, "NekoConverter.catalog.json"))
                .ForPlatform(platformId);
        }
        catch
        {
            return new PackageCatalog();
        }
    }

    /// <summary>
    /// Каталог доступных пакетов: лежит рядом с приложением, уже отфильтрован по платформе.
    /// </summary>
    public static PackageCatalog Catalog
    {
        get
        {
            lock (RegistryLock)
            {
                if (_catalog is not null)
                {
                    return _catalog;
                }

                var path = Path.Combine(AppContext.BaseDirectory, "catalog.json");

                try
                {
                    _catalog = PackageJson
                        .ParseCatalog(PackageJson.ReadData(path, "NekoConverter.catalog.json"))
                        .ForPlatform();
                }
                catch
                {
                    // Без каталога приложение работает: встроенные форматы никуда не деваются.
                    _catalog = new PackageCatalog();
                }

                return _catalog;
            }
        }
    }

    /// <summary>
    /// Каталог пользовательских данных приложения.
    /// Пакеты лежат здесь, а НЕ внутри .app: правка содержимого пакета ломала бы подпись.
    /// </summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(
            OperatingSystem.IsMacOS() ? Environment.SpecialFolder.UserProfile : Environment.SpecialFolder.ApplicationData),
            OperatingSystem.IsMacOS() ? "Library/Application Support/NekoConverter" : "NekoConverter");

    /// <summary>Каталог установленных пакетов.</summary>
    public static string PackagesDirectory => Path.Combine(DataDirectory, "packages");

    /// <summary>Форматы по умолчанию, если пользователь не выбрал целевой.</summary>
    private static readonly Dictionary<FormatKind, string> DefaultTargets = new()
    {
        [FormatKind.Image] = "png",
        [FormatKind.Audio] = "wav",
        [FormatKind.Subtitle] = "srt",
        [FormatKind.Data] = "json",
        [FormatKind.Document] = "pdf",
    };

    private static CapabilityRegistry LoadRegistry()
    {
        var baseDirectory = AppContext.BaseDirectory;
        // Путь может не существовать (например, на Android) — тогда сработает
        // встроенный ресурс, и это нормальный сценарий, а не ошибка.
        var coreFormats = Path.Combine(baseDirectory, "formats.json");

        return CapabilityRegistry.Load(coreFormats, PackagesDirectory, Packages.BundledDirectory);
    }

    public static ConversionResult Run(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);

        if (!File.Exists(request.InputPath))
        {
            throw new FileNotFoundException("Input file not found.", request.InputPath);
        }

        var registry = Registry;
        var source = registry.ByPath(request.InputPath)
            ?? throw new NotSupportedException(
                $"Unknown file format: {Path.GetExtension(request.InputPath)}");

        if (!source.CanRead)
        {
            throw new NotSupportedException($"Format \"{source.Label ?? source.Id}\" is write-only.");
        }

        if (!registry.IsAvailable(source))
        {
            throw new NotSupportedException(
                $"Reading \"{source.Label ?? source.Id}\" requires the \"{source.Engine}\" module. " +
                "Install it in the Modules section.");
        }

        var targetId = request.TargetFormatId ?? DefaultTargets.GetValueOrDefault(source.Kind, source.Id);
        var target = registry.ByExtension(targetId)
            ?? throw new NotSupportedException($"Unknown target format: {targetId}");

        var outputPath = ResolveOutputPath(request, target);

        // Конкретный формат, который выбрал пользователь.
        //
        // Это НЕ то же самое, что идентификатор строки таблицы: одна строка может
        // покрывать несколько расширений (mobi, azw3, lit — всё это «электронные книги»).
        // Движку нужно конкретное расширение, а таблице — строка. Раньше сюда попадал
        // идентификатор строки, и Calibre получал «ebook» вместо «mobi».
        var concreteTargetId = request.TargetFormatId ?? target.Extensions[0];

        var stopwatch = Stopwatch.StartNew();
        var engine = Dispatch(request, source, target, concreteTargetId, outputPath);
        stopwatch.Stop();

        var info = new FileInfo(outputPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException("Conversion finished without producing an output file.");
        }

        return new ConversionResult(outputPath, info.Length, stopwatch.Elapsed, engine);
    }

    private static string Dispatch(
        ConversionRequest request,
        FormatDescriptor source,
        FormatDescriptor target,
        string concreteTargetId,
        string outputPath)
    {
        switch (source.Kind)
        {
            case FormatKind.Image:
                ImagePipeline.Convert(
                    request.InputPath, outputPath, concreteTargetId, request.Quality, Registry, request.MaxDimension);

                return target.Engine == "sips" || source.Engine == "sips" ? "sips/skia" : "skia";

            case FormatKind.Audio:
                return DispatchAudio(request, source, target, concreteTargetId, outputPath);

            case FormatKind.Subtitle:
                SubtitleConverter.ConvertFile(
                    request.InputPath, outputPath, concreteTargetId, request.Fps ?? 25.0);
                return "subtitle";

            case FormatKind.Data:
                DataConverter.ConvertFile(request.InputPath, outputPath, concreteTargetId);
                return "data";

            case FormatKind.Video:
                return DispatchFfmpeg(request, target, concreteTargetId, outputPath);

            case FormatKind.Model3D:
                MeshConverter.Convert(request.InputPath, outputPath, concreteTargetId);
                return "assimp";

            case FormatKind.Document:
                return DispatchDocument(request, source, target, concreteTargetId, outputPath);

            default:
                throw new NotSupportedException(
                    $"The \"{source.Kind}\" category requires a module. Install it in the Modules section.");
        }
    }

    /// <summary>
    /// Выбор звукового движка.
    ///
    /// Логика та же, что у изображений: движок выбирается по паре «источник → цель»,
    /// а не по полю engine в таблице.
    ///   1. Оба конца — несжатый PCM: используем свой движок, он работает в процессе и не тратит
    ///      время на запуск afconvert (19 мс против ~60 мс).
    ///   2. Иначе — afconvert, если он умеет и читать источник, и писать цель.
    ///   3. Иначе — нужен модуль.
    /// </summary>
    private static string DispatchAudio(
        ConversionRequest request,
        FormatDescriptor source,
        FormatDescriptor target,
        string concreteTargetId,
        string outputPath)
    {
        var sourceId = Path.GetExtension(request.InputPath).TrimStart('.').ToLowerInvariant();

        var sourceIsPcm = PcmAudioConverter.IsPcmFormat(sourceId);
        var targetIsPcm = PcmAudioConverter.IsPcmFormat(concreteTargetId);

        if (sourceIsPcm && targetIsPcm)
        {
            var audioFormat = new AudioFormat(
                request.SampleRate ?? 0,
                request.Channels ?? 0,
                request.BitsPerSample ?? 16);

            // Нули означают «как в источнике» — движок подставит значения из файла.
            PcmAudioConverter.ConvertFile(
                request.InputPath,
                outputPath,
                concreteTargetId,
                request.SampleRate is null && request.Channels is null && request.BitsPerSample is null
                    ? null
                    : audioFormat);

            return "pcm";
        }

        if (AfconvertAudioEngine.IsAvailable &&
            AfconvertAudioEngine.CanRead(sourceId) &&
            AfconvertAudioEngine.CanWrite(concreteTargetId))
        {
            AfconvertAudioEngine.Convert(
                request.InputPath,
                outputPath,
                concreteTargetId,
                request.Quality,
                request.SampleRate,
                request.Channels,
                request.BitsPerSample);

            return "afconvert";
        }

        // Свой движок умеет писать цель, но не читает источник — тогда источник читает afconvert.
        if (AfconvertAudioEngine.IsAvailable && targetIsPcm && AfconvertAudioEngine.CanRead(sourceId))
        {
            AfconvertAudioEngine.Convert(
                request.InputPath, outputPath, concreteTargetId, request.Quality,
                request.SampleRate, request.Channels, request.BitsPerSample);

            return "afconvert";
        }

        // Последний шанс: FFmpeg умеет всё остальное звуковое.
        if (TryFfmpeg(request, concreteTargetId, outputPath, out var engine))
        {
            return engine;
        }

        throw new NotSupportedException(
            $"Converting \"{source.Label ?? source.Id}\" to \"{target.Label ?? target.Id}\" " +
            "requires a module. Install it in the Modules section.");
    }

    /// <summary>
    /// Ищет исполняемый файл движка: сначала среди пакетов, затем среди программ,
    /// уже установленных в системе. Второй путь не требует ни загрузки, ни места.
    /// </summary>
    private static string? ResolveEngineExecutable(string engine) =>
        Packages.ResolveExecutable(engine) ?? ExternalTools.Find(engine);

    /// <summary>
    /// Запускает FFmpeg, если зависимость установлена.
    /// Возвращает false, когда FFmpeg недоступен — тогда вызывающий код решает, что делать.
    /// </summary>
    private static bool TryFfmpeg(
        ConversionRequest request,
        string concreteTargetId,
        string outputPath,
        out string engine)
    {
        engine = "ffmpeg";

        if (!Registry.IsEngineAvailable("ffmpeg"))
        {
            return false;
        }

        var executable = ResolveEngineExecutable("ffmpeg");
        if (executable is null)
        {
            return false;
        }

        FfmpegEngine.Convert(
            executable,
            request.InputPath,
            outputPath,
            concreteTargetId,
            request.Quality,
            request.MaxDimension,
            request.SampleRate,
            request.Channels);

        return true;
    }

    private static string DispatchFfmpeg(
        ConversionRequest request,
        FormatDescriptor target,
        string concreteTargetId,
        string outputPath)
    {
        if (TryFfmpeg(request, concreteTargetId, outputPath, out var engine))
        {
            return engine;
        }

        throw new NotSupportedException(
            $"\"{target.Label ?? target.Id}\" requires FFmpeg. " +
            "Install the dependency in the Modules section — it is a single click.");
    }

    /// <summary>
    /// Документы. Порядок предпочтения:
    ///   1. Встроенный движок — только DOCX на входе и PDF/текстовые форматы на выходе.
    ///      Он не требует зависимостей и работает мгновенно.
    ///   2. Pandoc — всё остальное: ODT, RTF, EPUB, LaTeX и обратная запись в DOCX.
    /// </summary>
    private static string DispatchDocument(
        ConversionRequest request,
        FormatDescriptor source,
        FormatDescriptor target,
        string concreteTargetId,
        string outputPath)
    {
        var sourceId = Path.GetExtension(request.InputPath).TrimStart('.').ToLowerInvariant();

        if (sourceId == "docx" && concreteTargetId is "pdf" or "txt" or "md" or "html")
        {
            if (concreteTargetId == "pdf")
            {
                DocxToPdfConverter.ConvertFile(request.InputPath, outputPath);
                return "builtin-docx-pdf";
            }

            DocxTextConverter.ConvertFile(request.InputPath, outputPath, concreteTargetId);
            return "builtin-docx-text";
        }

        if (Registry.IsEngineAvailable("pandoc") &&
            PandocEngine.CanRead(sourceId) &&
            PandocEngine.CanWrite(concreteTargetId) &&
            ResolveEngineExecutable("pandoc") is { } pandoc)
        {
            PandocEngine.Convert(pandoc, request.InputPath, outputPath, concreteTargetId);
            return "pandoc";
        }

        // LibreOffice: единственный путь к высокой точности вёрстки и к старым
        // бинарным форматам Office. Ставится отдельно, в поставку не входит.
        if (LibreOfficeEngine.IsAvailable &&
            LibreOfficeEngine.CanRead(sourceId) &&
            LibreOfficeEngine.CanWrite(concreteTargetId))
        {
            LibreOfficeEngine.Convert(request.InputPath, outputPath, concreteTargetId);
            return "libreoffice";
        }

        // DjVu: разбор сканированных документов обратно в изображение или PDF.
        if (DjvuEngine.IsAvailable &&
            sourceId == "djvu" &&
            DjvuEngine.CanWrite(concreteTargetId))
        {
            DjvuEngine.Convert(request.InputPath, outputPath, concreteTargetId);
            return "djvu";
        }

        // Calibre: закрытые форматы электронных книг.
        if (CalibreEngine.IsAvailable &&
            CalibreEngine.CanRead(sourceId) &&
            CalibreEngine.CanWrite(concreteTargetId))
        {
            CalibreEngine.Convert(request.InputPath, outputPath, concreteTargetId);
            return "calibre";
        }

        var needed = Registry.AvailableWriteEngines(target).Count == 0
            ? string.Join(" / ", target.WriteEngines)
            : string.Join(" / ", source.ReadEngines);

        throw new NotSupportedException(
            $"Converting \"{source.Label ?? source.Id}\" to \"{target.Label ?? target.Id}\" " +
            $"requires the \"{needed}\" engine. Install it in the Modules section.");
    }

    /// <summary>
    /// Пакетная обработка. Вынесена сюда, чтобы CLI, интерфейс и MCP
    /// пользовались одним и тем же путём, а не своими циклами.
    /// </summary>
    public static Task<Batch.BatchSummary> RunBatchAsync(
        Batch.BatchRequest request,
        IProgress<Batch.BatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Batch.BatchRunner.RunAsync(request, progress, cancellationToken);

    /// <summary>
    /// Куда писать результат. Приоритет: явный путь, затем заданный каталог,
    /// затем — рядом с исходным файлом.
    /// </summary>
    private static string ResolveOutputPath(ConversionRequest request, FormatDescriptor target)
    {
        if (!string.IsNullOrEmpty(request.OutputPath))
        {
            return request.OutputPath;
        }

        var defaultPath = SuggestOutputPath(request.InputPath, target);

        if (string.IsNullOrEmpty(request.OutputDirectory))
        {
            return defaultPath;
        }

        // Каталог может ещё не существовать — создаём заранее,
        // чтобы ошибка была понятной, а не «файл не найден» на середине записи.
        Directory.CreateDirectory(request.OutputDirectory);

        return Path.Combine(request.OutputDirectory, Path.GetFileName(defaultPath));
    }

    /// <summary>Имя выходного файла по умолчанию: рядом с исходным, с новым расширением.</summary>
    public static string SuggestOutputPath(string inputPath, FormatDescriptor target)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = target.Extensions.Count > 0 ? target.Extensions[0] : target.Id;

        return Path.Combine(directory, $"{name}.{extension}");
    }

    /// <summary>Удобный вариант, когда есть только идентификатор формата.</summary>
    public static string SuggestOutputPath(string inputPath, string targetFormatId) =>
        Registry.ByExtension(targetFormatId) is { } target
            ? SuggestOutputPath(inputPath, target)
            : Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".",
                $"{Path.GetFileNameWithoutExtension(inputPath)}.{targetFormatId}");
}
