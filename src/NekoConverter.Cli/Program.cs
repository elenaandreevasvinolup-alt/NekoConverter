using NekoConverter.Core;
using NekoConverter.Core.Documents;
using NekoConverter.Core.Formats;
using NekoConverter.Core.Batch;
using NekoConverter.Core.Packaging;

// Консольный интерфейс ядра конвертации.
// Нужен для отладки и проверки без GUI, а позже станет точкой входа для MCP-сервера.

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "convert":
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 1;
            }

            string? targetFormat = null;
            string? output = null;
            var quality = 90;
            int? sampleRate = null;
            int? channels = null;
            int? bits = null;
            int? maxDimension = null;
            double? fps = null;

            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--to":
                        targetFormat = NextArg(args, ref i);
                        break;
                    case "-o":
                        output = NextArg(args, ref i);
                        break;
                    case "--quality":
                        quality = int.Parse(NextArg(args, ref i));
                        break;
                    case "--rate":
                        sampleRate = int.Parse(NextArg(args, ref i));
                        break;
                    case "--channels":
                        channels = int.Parse(NextArg(args, ref i));
                        break;
                    case "--bits":
                        bits = int.Parse(NextArg(args, ref i));
                        break;
                    case "--max":
                        maxDimension = int.Parse(NextArg(args, ref i));
                        break;
                    case "--fps":
                        fps = double.Parse(NextArg(args, ref i), System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw new ArgumentException($"Неизвестный аргумент: {args[i]}");
                }
            }

            var result = ConversionService.Run(new ConversionRequest(
                args[1], output, targetFormat, quality, sampleRate, channels, bits, maxDimension, fps));

            Console.WriteLine(
                $"OK: {result.OutputPath} ({result.Bytes:N0} байт, {result.Elapsed.TotalMilliseconds:F0} мс, движок {result.Engine})");
            return 0;
        }

        case "formats":
        {
            var registry = ConversionService.Registry;
            var filter = args.Length > 1 && Enum.TryParse<FormatKind>(args[1], true, out var kind)
                ? kind
                : (FormatKind?)null;

            Console.WriteLine($"{"формат",-12} {"категория",-10} {"чтение",-7} {"запись",-7} {"движок",-10} доступен");
            Console.WriteLine(new string('-', 68));

            foreach (var format in registry.All
                         .Where(f => filter is null || f.Kind == filter)
                         .OrderBy(f => f.Kind)
                         .ThenBy(f => f.Id))
            {
                var available = registry.IsAvailable(format) ? "да" : $"нет (модуль {format.ModuleId ?? format.Engine})";
                Console.WriteLine(
                    $"{format.Id,-12} {format.Kind,-10} {(format.CanRead ? "да" : "—"),-7} " +
                    $"{(format.CanWrite ? "да" : "—"),-7} {format.Engine,-10} {available}");
            }

            Console.WriteLine();
            Console.WriteLine($"Всего форматов: {registry.All.Count}; " +
                              $"доступно сразу: {registry.All.Count(registry.IsAvailable)}");

            if (registry.DuplicateExtensions.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("ВНИМАНИЕ: расширения объявлены дважды, поиск идёт по первому:");
                foreach (var duplicate in registry.DuplicateExtensions)
                {
                    Console.WriteLine($"  {duplicate}");
                }
            }

            return 0;
        }

        case "packages":
        {
            var manager = ConversionService.Packages;
            var installed = manager.ScanInstalled()
                .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"{"пакет",-16} {"тип",-11} {"размер",-10} {"состояние",-12} название");
            Console.WriteLine(new string('-', 86));

            foreach (var package in ConversionService.Catalog.Packages)
            {
                var id = package.Id ?? "?";
                var has = installed.TryGetValue(id, out var installedPackage);

                var size = package.SizeBytes > 0
                    ? $"{package.SizeBytes / 1048576.0:F1} МБ"
                    : "—";

                var state = has
                    ? installedPackage!.Source == PackageSource.Bundled
                        ? "офлайн-набор"
                        : package.Permanent ? "установлен*" : "установлен"
                    : "не установлен";

                Console.WriteLine(
                    $"{id,-16} {package.Kind,-11} {size,-10} {state,-12} {package.DisplayName()}");

                if (package.Requires.Count > 0)
                {
                    Console.WriteLine($"{"",-16} требует: {string.Join(", ", package.Requires)}");
                }

                if (has && installedPackage is not null)
                {
                    Console.WriteLine($"{"",-16} на диске: {installedPackage.SizeBytes / 1048576.0:F1} МБ");
                }
            }

            Console.WriteLine();
            Console.WriteLine("* — зависимость: ставится один раз и не удаляется вместе с пакетами форматов.");
            Console.WriteLine($"Каталог пакетов: {manager.PackagesDirectory}");
            return 0;
        }

        case "install":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: nekoconv install <идентификатор пакета>");
                return 1;
            }

            var id = args[1];
            var package = ConversionService.Catalog.Packages.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

            if (package is null)
            {
                Console.Error.WriteLine($"В каталоге нет пакета «{id}». Посмотрите список: nekoconv packages");
                return 1;
            }

            var progress = new Progress<InstallProgress>(p =>
            {
                switch (p.Stage)
                {
                    case InstallStage.Downloading when p.TotalBytes > 0:
                        Console.Write($"\r  загрузка {p.BytesReceived / 1048576.0:F1} / {p.TotalBytes / 1048576.0:F1} МБ ({p.Fraction:P0})   ");
                        break;
                    case InstallStage.Downloading:
                        Console.Write($"\r  загрузка {p.BytesReceived / 1048576.0:F1} МБ   ");
                        break;
                    default:
                        Console.WriteLine($"\n  {p.Message ?? p.Stage.ToString()}");
                        break;
                }
            });

            ConversionService.Packages
                .InstallAsync(package, ConversionService.Catalog, progress)
                .GetAwaiter()
                .GetResult();

            ConversionService.ReloadRegistry();
            Console.WriteLine("\n  Установлено. Доступно форматов: " +
                              $"{ConversionService.Registry.All.Count(ConversionService.Registry.IsAvailable)}");
            return 0;
        }

        case "fetch-deps":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: nekoconv fetch-deps <目录> [--package <id>]");
                Console.Error.WriteLine("   скачивает и распаковывает зависимости для сборки офлайн-набора");
                return 1;
            }

            var destination = args[1];
            var only = args.Contains("--package") ? args[Array.IndexOf(args, "--package") + 1] : null;
            var platform = args.Contains("--platform") ? args[Array.IndexOf(args, "--platform") + 1] : null;

            var catalog = ConversionService.CatalogFor(platform);
            var targets = only is null
                ? catalog.Packages.Where(p => p.Kind == PackageKind.Dependency).ToList()
                : catalog.Packages.Where(p => string.Equals(p.Id, only, StringComparison.OrdinalIgnoreCase)).ToList();

            if (targets.Count == 0)
            {
                Console.Error.WriteLine("В каталоге нет подходящих пакетов для этой платформы.");
                return 1;
            }

            Console.WriteLine($"Платформа: {platform ?? PackageManifest.CurrentPlatformId()}");
            Console.WriteLine($"Назначение: {destination}");
            Console.WriteLine();

            var progress = new Progress<InstallProgress>(p =>
            {
                if (p.Stage == InstallStage.Downloading && p.TotalBytes > 0)
                {
                    Console.Write($"\r  {p.PackageId}: {p.BytesReceived / 1048576.0:F1} / {p.TotalBytes / 1048576.0:F1} МБ   ");
                }
                else if (p.Message is { Length: > 0 })
                {
                    Console.WriteLine($"\n  {p.PackageId}: {p.Message}");
                }
            });

            foreach (var package in targets)
            {
                ConversionService.Packages
                    .FetchToAsync(package, catalog, destination, progress)
                    .GetAwaiter()
                    .GetResult();
            }

            Console.WriteLine();
            Console.WriteLine($"Готово. Офлайн-набор: {Path.GetFullPath(destination)}");
            return 0;
        }

        case "uninstall":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: nekoconv uninstall <идентификатор пакета> [--force]");
                return 1;
            }

            ConversionService.Packages.Uninstall(args[1], force: args.Contains("--force"));
            ConversionService.ReloadRegistry();
            Console.WriteLine($"  «{args[1]}» удалён.");
            return 0;
        }

        case "hash":
        {
            if (args.Length < 2 || !File.Exists(args[1]))
            {
                Console.Error.WriteLine("用法: nekoconv hash <файл>   — считает sha256 для каталога пакетов");
                return 1;
            }

            using var stream = File.OpenRead(args[1]);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
            Console.WriteLine(hash);
            return 0;
        }

        case "font":
            // Показывает, какой системный шрифт выбран для встраивания в PDF.
            Console.WriteLine(SystemCjkFontResolver.Instance.Path);
            return 0;

        case "probe":
            return NekoConverter.Cli.Probe.Run(args[1..]);

        case "batch":
        {
            // Пакетная обработка: несколько файлов за один запуск.
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: nekoconv batch <файл>... [--to <формат>] [--out <каталог>] [--template <шаблон>] [--quality 1-100]");
                return 1;
            }

            var files = new List<string>();
            string? target = null;
            string? outDir = null;
            string? template = null;
            var quality = 90;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--to": target = NextArg(args, ref i); break;
                    case "--out": outDir = NextArg(args, ref i); break;
                    case "--template": template = NextArg(args, ref i); break;
                    case "--quality": quality = int.Parse(NextArg(args, ref i)); break;
                    default: files.Add(args[i]); break;
                }
            }

            var request = new BatchRequest(
                files.Select(f => new BatchItemRequest(f, target, quality)).ToList(),
                outDir,
                template);

            var last = 0;

            var progress = new Progress<BatchProgress>(p =>
            {
                if (p.Current is { } current)
                {
                    var mark = current.Success ? "OK  " : "ОШИБ";
                    var detail = current.Success
                        ? $"{Path.GetFileName(current.OutputPath)} ({current.Bytes:N0} байт)"
                        : current.Error;

                    Console.WriteLine($"  [{p.Completed}/{p.Total}] {mark} {Path.GetFileName(current.InputPath)} → {detail}");
                }

                last = p.Completed;
            });

            var summary = ConversionService.RunBatchAsync(request, progress).GetAwaiter().GetResult();

            Console.WriteLine();
            Console.WriteLine($"  Успешно: {summary.Succeeded}, с ошибками: {summary.Failed}, " +
                              $"всего {summary.TotalBytes / 1048576.0:F2} МБ");
            _ = last;

            return summary.Failed == 0 ? 0 : 2;
        }

        case "mcp":
            // Сервер MCP: JSON-RPC поверх stdio. Ничего лишнего в stdout не пишем.
            return NekoConverter.Core.Mcp.McpServer.RunAsync().GetAwaiter().GetResult();

        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ОШИБКА: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

static string NextArg(string[] args, ref int index)
{
    index++;
    if (index >= args.Length)
    {
        throw new ArgumentException("Не хватает значения для предыдущего аргумента.");
    }

    return args[index];
}

static void PrintUsage()
{
    Console.WriteLine("""
        nekoconv — конвертер форматов (ядро NekoConverter)

        Использование:
          nekoconv convert <файл> [--to <формат>] [-o выходной] [--quality 1-100]
                                          [--rate <Гц>] [--channels <n>] [--bits <8|16|24|32>]
                                          [--max <пикс>] [--fps <n>]
          nekoconv formats [категория]    список форматов и их доступность
          nekoconv packages               список пакетов и их состояние
          nekoconv install <пакет>        установить пакет (зависимости ставятся сами)
          nekoconv uninstall <пакет>      удалить пакет
          nekoconv hash <файл>            sha256 файла — для заполнения каталога
          nekoconv fetch-deps <каталог> [--platform win-x64] [--package <id>]
                                          подготовить офлайн-набор зависимостей
          nekoconv font                   какой шрифт встраивается в PDF
          nekoconv probe <каталог>        что реально умеет SkiaSharp на этой машине
          nekoconv batch <файл>...        пакетная обработка нескольких файлов
                                          [--to <формат>] [--out <каталог>]
                                          [--template <шаблон>] [--quality 1-100]
          nekoconv mcp                    MCP-сервер (JSON-RPC 2.0 поверх stdio)

        Категории: Image, Audio, Video, Document, Data, Subtitle, Model3D

        Примеры:
          nekoconv convert photo.heic --to jpeg --quality 85
          nekoconv convert scan.tiff --to png
          nekoconv convert record.wav --to aiff --bits 24
          nekoconv convert photo.png --to jpeg --max 1920 --quality 88
          nekoconv convert subs.ass --to srt
          nekoconv convert data.csv --to json
          nekoconv convert отчёт.docx --to md
        """);
}
