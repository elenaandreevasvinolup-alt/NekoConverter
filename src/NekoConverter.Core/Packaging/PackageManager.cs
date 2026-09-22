using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NekoConverter.Core.Packaging;

/// <summary>Откуда взят установленный пакет.</summary>
public enum PackageSource
{
    /// <summary>Установлен пользователем в его каталог данных.</summary>
    UserInstalled,

    /// <summary>
    /// Лежит рядом с приложением в папке deps — офлайн-набор.
    /// Такой пакет уже распакован, поэтому установка не нужна вовсе.
    /// </summary>
    Bundled,
}

/// <summary>Установленный пакет на диске.</summary>
/// <param name="Manifest">Манифест.</param>
/// <param name="Directory">Каталог пакета.</param>
/// <param name="SizeBytes">Фактический размер на диске.</param>
/// <param name="Source">Откуда взят пакет.</param>
public sealed record InstalledPackage(
    PackageManifest Manifest,
    string Directory,
    long SizeBytes,
    PackageSource Source = PackageSource.UserInstalled)
{
    public string Id => Manifest.Id ?? "core";

    /// <summary>Полный путь к исполняемому файлу пакета, если он объявлен.</summary>
    public string? ExecutablePath =>
        Manifest.Executable is { Length: > 0 } relative
            ? Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar))
            : null;
}

/// <summary>
/// Установка и удаление пакетов.
///
/// Раскладка на диске:
///   ~/Library/Application Support/NekoConverter/
///     ├── packages/          ← установленные пользователем
///     ├── cache/             ← скачанные архивы (чтобы не качать дважды)
///     └── settings.conf
///   <папка рядом с приложением>/
///     └── deps/              ← офлайн-набор: уже распакованные зависимости
///
/// Порядок поиска при установке — от самого быстрого к самому медленному:
///   1. Пакет уже есть в офлайн-наборе → ставить нечего, используем как есть.
///   2. Архив лежит в кэше → распаковываем локально, сеть не нужна.
///   3. Скачиваем, докачивая прерванную загрузку.
///
/// Установка атомарная: распаковка идёт в staging, и только потом каталог
/// переезжает на место. Прерванная установка не оставляет полурабочий пакет.
/// </summary>
public sealed class PackageManager
{
    private const string StagingDirectoryName = ".staging";
    private const string CacheDirectoryName = "cache";

    private readonly HttpClient _http;
    private readonly string _packagesDirectory;
    private readonly string _dataDirectory;

    public PackageManager(string dataDirectory, string? bundledDirectory = null, HttpClient? httpClient = null)
    {
        _dataDirectory = dataDirectory;
        _packagesDirectory = Path.Combine(dataDirectory, "packages");
        BundledDirectory = bundledDirectory;

        // Языковые пакеты ставятся в каталог переводов: их читает Localizer,
        // а не механизм модулей.
        LocaleDirectory = Path.Combine(dataDirectory, "Locale");

        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(60) };

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NekoConverter/1.0");
        }
    }

    public string PackagesDirectory => _packagesDirectory;

    public string CacheDirectory => Path.Combine(_dataDirectory, CacheDirectoryName);

    /// <summary>Каталог языковых пакетов.</summary>
    public string LocaleDirectory { get; }

    /// <summary>Каталог офлайн-набора рядом с приложением. null — набора нет.</summary>
    public string? BundledDirectory { get; }

    private string StagingDirectory => Path.Combine(_packagesDirectory, StagingDirectoryName);

    /// <summary>
    /// Ищет офлайн-набор рядом с приложением.
    ///
    /// Два варианта размещения: рядом с исполняемым файлом (сборка каталогом)
    /// и на три уровня выше (внутри .app это выводит в папку, где лежит сам бандл).
    /// </summary>
    public static string? FindBundledDirectory(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "deps"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "deps")),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // ─────────────────────────── Пакеты ───────────────────────────

    /// <summary>
    /// Сканирует и пользовательские пакеты, и офлайн-набор.
    /// Битый манифест пропускается, а не роняет приложение.
    /// </summary>
    public IReadOnlyList<InstalledPackage> ScanInstalled()
    {
        var result = new List<InstalledPackage>();

        ScanDirectory(_packagesDirectory, PackageSource.UserInstalled, result);

        if (BundledDirectory is not null)
        {
            ScanDirectory(BundledDirectory, PackageSource.Bundled, result);
        }

        return result;
    }

    private static void ScanDirectory(string root, PackageSource source, List<InstalledPackage> result)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "package.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = PackageJson.LoadManifest(manifestPath);
                if (string.IsNullOrEmpty(manifest.Id))
                {
                    continue;
                }

                // Пользовательская установка важнее комплектной: если пакет есть и там и там,
                // показываем тот, которым реально пользуемся.
                if (result.Any(p => string.Equals(p.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new InstalledPackage(manifest, directory, DirectorySize(directory), source));
            }
            catch
            {
                // Повреждённый манифест не должен мешать остальным пакетам работать.
            }
        }
    }

    public bool IsInstalled(string packageId) =>
        ScanInstalled().Any(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Убирает недоделанные установки от прошлых запусков.</summary>
    public void CleanStaging()
    {
        if (Directory.Exists(StagingDirectory))
        {
            TryDeleteDirectory(StagingDirectory);
        }
    }

    /// <summary>Движки, доступные благодаря установленным пакетам и офлайн-набору.</summary>
    public HashSet<string> ProvidedEngines()
    {
        var engines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in ScanInstalled())
        {
            foreach (var engine in package.Manifest.ProvidesEngines)
            {
                engines.Add(engine);
            }
        }

        return engines;
    }

    /// <summary>
    /// Ищет исполняемый файл, который даёт указанный движок.
    /// Именно так пакет форматов находит оригинальный FFmpeg.
    /// </summary>
    public string? ResolveExecutable(string engine)
    {
        foreach (var package in ScanInstalled())
        {
            if (!package.Manifest.ProvidesEngines.Contains(engine, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (package.ExecutablePath is { } path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    // ─────────────────────────── Установка ───────────────────────────

    /// <summary>
    /// Устанавливает пакет вместе со всеми зависимостями.
    /// Зависимости ставятся первыми: если FFmpeg не встанет, пакет форматов не будет установлен.
    /// </summary>
    public async Task InstallAsync(
        PackageManifest package,
        PackageCatalog catalog,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var dependencyId in package.Requires)
        {
            if (IsInstalled(dependencyId))
            {
                continue;
            }

            var dependency = catalog.Packages.FirstOrDefault(p =>
                string.Equals(p.Id, dependencyId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Package \"{package.DisplayName()}\" requires \"{dependencyId}\", but it is not in the catalog.");

            progress?.Report(new InstallProgress(
                InstallStage.Resolving, dependencyId, Message: $"Dependency first: {dependency.DisplayName()}"));

            await InstallSingleAsync(dependency, progress, cancellationToken).ConfigureAwait(false);
        }

        await InstallSingleAsync(package, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallSingleAsync(
        PackageManifest package,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var id = package.Id
            ?? throw new InvalidOperationException("The package has no identifier.");

        // ── Шаг 0: пакет уже лежит в офлайн-наборе ──
        // Это самый быстрый путь: ничего не качаем и не копируем, просто пользуемся.
        if (BundledDirectory is not null &&
            Directory.Exists(Path.Combine(BundledDirectory, id)))
        {
            progress?.Report(new InstallProgress(
                InstallStage.Done, id, Message: "Already present in the offline bundle — nothing to install"));

            return;
        }

        // ── Шаг 0б: пакет без файлов (только манифест) ──
        //
        // Проверяем именно наличие ЛЮБОГО адреса, а не только основного:
        // у языкового пакета заполнены sources, а downloadUrl пуст, и раньше
        // он из-за этого попадал в эту ветку и «устанавливался» без файлов.
        if (package.AllSources().Count == 0)
        {
            Directory.CreateDirectory(_packagesDirectory);
            var simpleDirectory = Path.Combine(_packagesDirectory, id);
            Directory.CreateDirectory(simpleDirectory);

            File.WriteAllText(
                Path.Combine(simpleDirectory, "package.json"),
                PackageJson.SerializeManifest(package));

            progress?.Report(new InstallProgress(InstallStage.Done, id, Message: "Done"));
            return;
        }

        Directory.CreateDirectory(_packagesDirectory);
        CleanStaging();

        var stagingRoot = Path.Combine(StagingDirectory, id);
        Directory.CreateDirectory(stagingRoot);

        try
        {
            // ── Шаг 1: архив из кэша или из сети ──
            var cachedArchive = Path.Combine(CacheDirectory, CacheFileName(package));
            var archivePath = await ObtainArchiveAsync(package, stagingRoot, progress, cancellationToken)
                .ConfigureAwait(false);

            var targetDirectory = Path.Combine(stagingRoot, "content");
            Directory.CreateDirectory(targetDirectory);

            progress?.Report(new InstallProgress(InstallStage.Extracting, id, Message: "Extracting"));
            Extract(package, archivePath, targetDirectory);

            if (!string.Equals(archivePath, cachedArchive, StringComparison.Ordinal))
            {
                File.Delete(archivePath);
            }

            // Архивы часто содержат один вложенный каталог (ffmpeg-9.0.2/bin/ffmpeg).
            FlattenSingleRootDirectory(targetDirectory);
            Prune(package, targetDirectory);

            File.WriteAllText(
                Path.Combine(targetDirectory, "package.json"),
                PackageJson.SerializeManifest(package));

            progress?.Report(new InstallProgress(InstallStage.Finishing, id, Message: "Installing"));

            // Языковой пакет распаковывается прямо в каталог переводов:
            // его содержимое — файлы strings.xx.json, а не папка с движком.
            var finalDirectory = string.Equals(package.InstallTo, "locale", StringComparison.OrdinalIgnoreCase)
                ? LocaleDirectory
                : Path.Combine(_packagesDirectory, id);

            Directory.CreateDirectory(finalDirectory);

            if (string.Equals(package.InstallTo, "locale", StringComparison.OrdinalIgnoreCase))
            {
                // Копируем файлы поверх: переводы соседствуют друг с другом.
                foreach (var file in Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);

                    // Манифест в каталог переводов не кладём: там только strings.*.json.
                    if (string.Equals(name, "package.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    File.Copy(file, Path.Combine(finalDirectory, name), overwrite: true);
                }

                // Отметку об установке храним в обычном каталоге пакетов: содержимое
                // языкового пакета лежит отдельно, а состояние должно учитываться
                // тем же механизмом, что и у остальных пакетов.
                var markerDirectory = Path.Combine(_packagesDirectory, id);
                Directory.CreateDirectory(markerDirectory);

                File.WriteAllText(
                    Path.Combine(markerDirectory, "package.json"),
                    PackageJson.SerializeManifest(package));
            }
            else
            {
                if (Directory.Exists(finalDirectory))
                {
                    TryDeleteDirectory(finalDirectory);
                }

                Directory.Move(targetDirectory, finalDirectory);
            }

            progress?.Report(new InstallProgress(InstallStage.Done, id, Message: "Done"));
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
    }

    /// <summary>Удаляет пакет. Постоянные (зависимости) удаляются только по явному требованию.</summary>
    public void Uninstall(string packageId, bool force = false)
    {
        var package = ScanInstalled().FirstOrDefault(p =>
            string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Package \"{packageId}\" is not installed.");

        if (package.Source == PackageSource.Bundled)
        {
            throw new InvalidOperationException(
                $"\"{package.Manifest.DisplayName()}\" is part of the offline bundle next to the app. " +
                "To remove it, delete the deps folder.");
        }

        if (package.Manifest.Permanent && !force)
        {
            throw new InvalidOperationException(
                $"\"{package.Manifest.DisplayName()}\" is a dependency. " +
                "It is installed once and is not removed together with format packages.");
        }

        if (!force)
        {
            var dependent = ScanInstalled().FirstOrDefault(p =>
                !string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase) &&
                p.Manifest.Requires.Contains(packageId, StringComparer.OrdinalIgnoreCase));

            if (dependent is not null)
            {
                throw new InvalidOperationException(
                    $"\"{dependent.Manifest.DisplayName()}\" uses \"{package.Manifest.DisplayName()}\". " +
                    "Remove the format package first.");
            }
        }

        TryDeleteDirectory(package.Directory);
    }

    /// <summary>
    /// Готовит офлайн-набор: скачивает и распаковывает пакеты в указанный каталог.
    /// Используется при сборке дистрибутива, чтобы пользователь не качал ничего из сети.
    /// </summary>
    public async Task FetchToAsync(
        PackageManifest package,
        PackageCatalog catalog,
        string destinationRoot,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var dependencyId in package.Requires)
        {
            var dependency = catalog.Packages.FirstOrDefault(p =>
                string.Equals(p.Id, dependencyId, StringComparison.OrdinalIgnoreCase));

            if (dependency is not null)
            {
                await FetchSingleToAsync(dependency, destinationRoot, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await FetchSingleToAsync(package, destinationRoot, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task FetchSingleToAsync(
        PackageManifest package,
        string destinationRoot,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var id = package.Id ?? throw new InvalidOperationException("The package has no identifier.");
        var finalDirectory = Path.Combine(destinationRoot, id);

        if (Directory.Exists(finalDirectory))
        {
            progress?.Report(new InstallProgress(InstallStage.Done, id, Message: "Already downloaded"));
            return;
        }

        if (package.AllSources().Count == 0)
        {
            Directory.CreateDirectory(finalDirectory);
            File.WriteAllText(
                Path.Combine(finalDirectory, "package.json"),
                PackageJson.SerializeManifest(package));

            progress?.Report(new InstallProgress(InstallStage.Done, id, Message: "Manifest written"));
            return;
        }

        Directory.CreateDirectory(destinationRoot);

        var staging = Path.Combine(destinationRoot, $".staging-{id}");
        if (Directory.Exists(staging))
        {
            TryDeleteDirectory(staging);
        }

        Directory.CreateDirectory(staging);

        try
        {
            var archivePath = await ObtainArchiveAsync(package, staging, progress, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new InstallProgress(InstallStage.Extracting, id, Message: "Extracting"));
            Extract(package, archivePath, finalDirectory);

            // Архив из кэша удалять нельзя: им ещё будут пользоваться.
            if (!string.Equals(
                    archivePath,
                    Path.Combine(CacheDirectory, CacheFileName(package)),
                    StringComparison.Ordinal))
            {
                File.Delete(archivePath);
            }
            FlattenSingleRootDirectory(finalDirectory);
            Prune(package, finalDirectory);

            File.WriteAllText(
                Path.Combine(finalDirectory, "package.json"),
                PackageJson.SerializeManifest(package));

            progress?.Report(new InstallProgress(InstallStage.Done, id, Message: "Done"));
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                TryDeleteDirectory(staging);
            }
        }
    }

    // ─────────────────────────── Загрузка ───────────────────────────

    /// <summary>
    /// Возвращает путь к архиву пакета, по возможности не трогая сеть.
    ///
    /// Порядок: готовый архив в кэше → докачка из сети → запись в кэш.
    /// Кэш здесь не оптимизация ради красоты: при сборке офлайн-набора и при
    /// повторной установке он экономит десятки мегабайт трафика.
    /// </summary>
    private async Task<string> ObtainArchiveAsync(
        PackageManifest package,
        string stagingDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var id = package.Id!;
        var cached = Path.Combine(CacheDirectory, CacheFileName(package));

        if (File.Exists(cached) && ChecksumMatches(package, cached))
        {
            progress?.Report(new InstallProgress(
                InstallStage.Downloading, id, new FileInfo(cached).Length, package.SizeBytes,
                "Taken from cache — no network needed"));

            return cached;
        }

        var downloaded = Path.Combine(stagingDirectory, "download.bin");

        progress?.Report(new InstallProgress(
            InstallStage.Downloading, id, 0, package.SizeBytes, $"Downloading {package.DisplayName()}"));

        await DownloadAsync(package, downloaded, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new InstallProgress(InstallStage.Verifying, id, Message: "Verifying integrity"));
        VerifyChecksum(package, downloaded);

        TryCache(package, downloaded, cached);

        return downloaded;
    }

    private static string CacheFileName(PackageManifest package)
    {
        var version = string.IsNullOrEmpty(package.Version) ? "unknown" : package.Version;
        return $"{package.Id}-{version}.bin";
    }

    private bool ChecksumMatches(PackageManifest package, string path)
    {
        if (string.IsNullOrWhiteSpace(package.Sha256))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(actual, package.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void TryCache(PackageManifest package, string archivePath, string cachePath)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.Copy(archivePath, cachePath, overwrite: true);
        }
        catch
        {
            // Кэш — оптимизация, а не обязанность: если не вышло, просто не кэшируем.
        }
    }

    /// <summary>
    /// Скачивает архив с докачкой.
    ///
    /// Докачка важна именно здесь: у пользователя медленный канал до зарубежных
    /// источников, и обрыв на 90 % не должен означать начинать заново.
    /// Если сервер не поддерживает Range, загрузка честно начинается с нуля.
    /// </summary>
    /// <summary>
    /// Скачивает архив, перебирая основной адрес и зеркала.
    ///
    /// Перебор важен именно здесь: в части сетей зарубежные площадки недоступны,
    /// и без зеркал установка просто не проходит. Ошибка последней попытки
    /// и становится итоговой.
    /// </summary>
    private async Task DownloadAsync(
        PackageManifest package,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sources = package.AllSources();

        if (sources.Count == 0)
        {
            throw new InvalidOperationException($"Package \"{package.Id}\" has no download address.");
        }

        // Замеряем все адреса и идём по ним от быстрейшего к медленному.
        // Именно так решается задача «в Китае не открывается»: не угадывать
        // заранее, а выбрать по факту.
        if (sources.Count > 1)
        {
            progress?.Report(new InstallProgress(
                InstallStage.Downloading, package.Id!, Message: "Choosing the fastest source"));

            var ranked = await SourceProbe.RankAsync(sources, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            sources = ranked.Select(r => r.Url).ToList();

            if (ranked.FirstOrDefault(r => r.Reachable) is { } fastest)
            {
                progress?.Report(new InstallProgress(
                    InstallStage.Downloading, package.Id!,
                    Message: $"Source: {fastest.Host} ({fastest.Latency!.Value.TotalMilliseconds:F0} ms)"));
            }
        }

        Exception? lastError = null;

        for (var attempt = 0; attempt < sources.Count; attempt++)
        {
            var url = sources[attempt];

            try
            {
                if (attempt > 0)
                {
                    progress?.Report(new InstallProgress(
                        InstallStage.Downloading, package.Id!, Message: $"Trying mirror: {new Uri(url).Host}"));

                    // Обрывок от неудачной попытки не должен попасть в докачку.
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }

                await DownloadFromAsync(package, url, destination, progress, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"Could not download \"{package.DisplayName()}\" from any address " +
            $"({sources.Count} tried). Last error: {lastError?.Message}", lastError);
    }

    private async Task DownloadFromAsync(
        PackageManifest package,
        string url,
        string destination,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // Сервер может не поддержать докачку: тогда 200 вместо 206, и начинаем заново.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        if (!resuming)
        {
            existing = 0;
        }

        response.EnsureSuccessStatusCode();

        var total = package.SizeBytes > 0
            ? package.SizeBytes
            : (response.Content.Headers.ContentLength ?? 0) + existing;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81_920];
        long received = existing;
        var lastReported = existing;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            // Не дёргаем интерфейс на каждые 80 КБ: раз в мегабайт вполне достаточно.
            if (received - lastReported >= 1_048_576)
            {
                lastReported = received;
                progress?.Report(new InstallProgress(
                    InstallStage.Downloading, package.Id!, received, total, $"Downloading {package.DisplayName()}"));
            }
        }

        progress?.Report(new InstallProgress(
            InstallStage.Downloading, package.Id!, received, total, $"Downloading {package.DisplayName()}"));
    }

    private static void VerifyChecksum(PackageManifest package, string path)
    {
        if (string.IsNullOrWhiteSpace(package.Sha256))
        {
            // Не блокируем установку, но факт отсутствия суммы должен быть виден.
            Console.Error.WriteLine(
                $"WARNING: package \"{package.Id}\" has no sha256 checksum.");
            return;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var expected = package.Sha256.Trim().ToLowerInvariant();

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for \"{package.Id}\".\n" +
                $"  expected: {expected}\n" +
                $"  actual:   {actual}\n" +
                "The file is corrupted or tampered with — installation cancelled.");
        }
    }

    private static void Extract(PackageManifest package, string archivePath, string destination)
    {
        var archive = (package.Archive ?? string.Empty).Trim().ToLowerInvariant();

        switch (archive)
        {
            case "zip":
                ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
                break;

            case "tar.gz" or "tgz":
                using (var file = File.OpenRead(archivePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
                }

                break;

            case "" or "none":
                var name = package.Executable is { Length: > 0 } executable
                    ? Path.GetFileName(executable)
                    : package.Id + ".bin";

                File.Copy(archivePath, Path.Combine(destination, name), overwrite: true);
                break;

            default:
                throw new NotSupportedException($"Archive format \"{archive}\" is not supported.");
        }
    }

    // ─────────────────────────── Вспомогательное ───────────────────────────

    /// <summary>
    /// Удаляет из распакованного пакета то, что объявлено лишним.
    /// Считается после выравнивания каталогов, чтобы пути в манифесте были предсказуемыми.
    /// </summary>
    private static void Prune(PackageManifest package, string directory)
    {
        foreach (var relative in package.Prune)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var target = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            catch
            {
                // Не смогли удалить — не повод отменять установку.
            }
        }
    }

    /// <summary>
    /// Архивы из интернета часто содержат один вложенный каталог
    /// (например ffmpeg-9.0.2/bin/ffmpeg). Поднимаем содержимое на уровень выше,
    /// чтобы пути в манифесте были предсказуемыми.
    /// </summary>
    public static void FlattenSingleRootDirectory(string directory)
    {
        var entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length != 1 || !Directory.Exists(entries[0]))
        {
            return;
        }

        var inner = entries[0];
        foreach (var entry in Directory.GetFileSystemEntries(inner))
        {
            var name = Path.GetFileName(entry);
            var target = Path.Combine(directory, name);

            if (Directory.Exists(entry))
            {
                Directory.Move(entry, target);
            }
            else
            {
                File.Move(entry, target);
            }
        }

        Directory.Delete(inner);
    }

    private static long DirectorySize(string directory)
    {
        long total = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }
        }
        catch
        {
            // Размер — справочная величина, ошибка чтения не критична.
        }

        return total;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Каталог может быть занят другим процессом — не роняем приложение из-за этого.
        }
    }
}
