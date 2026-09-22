namespace NekoConverter.Core.Batch;

/// <summary>
/// Пакетная обработка: последовательно прогоняет список файлов через то же ядро,
/// что и одиночное преобразование.
///
/// Последовательно, а не параллельно, и это осознанно: преобразование видео или
/// распаковка архива уже упираются в диск и процессор, а несколько таких задач
/// одновременно только мешают друг другу и забивают память. Для пакета из десяти
/// файлов разница в общем времени выходит в пределах погрешности, зато прогресс
/// идёт ровно и предсказуемо.
///
/// Отмена проверяется между файлами: прерывать преобразование на середине
/// нельзя, иначе получится обрезанный результат.
/// </summary>
public static class BatchRunner
{
    public static async Task<BatchSummary> RunAsync(
        BatchRequest request,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<BatchItemResult>(request.Items.Count);

        for (var index = 0; index < request.Items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = request.Items[index];
            var result = await Task.Run(
                () => ConvertOne(item, request, index),
                cancellationToken).ConfigureAwait(false);

            results.Add(result);
            progress?.Report(new BatchProgress(index + 1, request.Items.Count, result));
        }

        return new BatchSummary(results);
    }

    private static BatchItemResult ConvertOne(BatchItemRequest item, BatchRequest request, int index)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var registry = ConversionService.Registry;
            var source = registry.ByPath(item.InputPath)
                ?? throw new NotSupportedException(
                    $"Unknown file format: {Path.GetExtension(item.InputPath)}");

            var targetId = item.TargetFormatId
                ?? DefaultTargetFor(source.Kind)
                ?? throw new NotSupportedException(
                    $"No default target format for the \"{source.Kind}\" category.");

            var target = registry.ByExtension(targetId)
                ?? throw new NotSupportedException($"Unknown target format: {targetId}");

            var extension = target.Extensions.Count > 0 ? target.Extensions[0] : target.Id;

            var directory = string.IsNullOrEmpty(request.OutputDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(item.InputPath)) ?? "."
                : request.OutputDirectory;

            Directory.CreateDirectory(directory);

            var fileName = NameTemplate.Apply(
                request.NameTemplate, item.InputPath, target.Id, extension, index + 1);

            var outputPath = Path.Combine(directory, fileName);

            // Что делать, если такой файл уже есть: по умолчанию не затираем,
            // а подбираем свободное имя — потерять чужой файл хуже, чем получить
            // лишний с суффиксом.
            outputPath = ResolveCollision(outputPath, item.InputPath, request.ExistingFileAction);

            if (outputPath is null)
            {
                stopwatch.Stop();
                return new BatchItemResult(
                    index, item.InputPath, null, false, "Skipped: the file already exists", 0, stopwatch.Elapsed);
            }

            ConversionService.Run(new ConversionRequest(
                item.InputPath,
                outputPath,
                target.Id,
                item.Quality,
                MaxDimension: item.MaxDimension));

            stopwatch.Stop();

            var info = new FileInfo(outputPath);

            return new BatchItemResult(
                index, item.InputPath, outputPath, true, null,
                info.Exists ? info.Length : 0, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BatchItemResult(index, item.InputPath, null, false, ex.Message, 0, stopwatch.Elapsed);
        }
    }

    /// <summary>Свободное имя рядом с занятым: «файл (2).jpg» и так далее.</summary>
    private static string? ResolveCollision(string outputPath, string inputPath, ExistingFileAction action)
    {
        // Результат поверх исходного файла — это всегда ошибка, независимо от настройки.
        if (string.Equals(
                Path.GetFullPath(outputPath),
                Path.GetFullPath(inputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(outputPath))
        {
            return outputPath;
        }

        switch (action)
        {
            case ExistingFileAction.Overwrite:
                return outputPath;

            case ExistingFileAction.Skip:
                return null;

            default:
                var directory = Path.GetDirectoryName(outputPath) ?? ".";
                var name = Path.GetFileNameWithoutExtension(outputPath);
                var extension = Path.GetExtension(outputPath);

                for (var suffix = 2; suffix < 1000; suffix++)
                {
                    var candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");

                    if (!File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                return null;
        }
    }

    private static string? DefaultTargetFor(Formats.FormatKind kind) => kind switch
    {
        Formats.FormatKind.Image => "jpeg",
        Formats.FormatKind.Audio => "flac",
        Formats.FormatKind.Video => "mp4",
        Formats.FormatKind.Subtitle => "srt",
        Formats.FormatKind.Data => "json",
        Formats.FormatKind.Document => "pdf",
        Formats.FormatKind.Model3D => "obj",
        _ => null,
    };
}
