using NekoConverter.Core.Formats;

namespace NekoConverter.Core.Engines.Image;

/// <summary>
/// Маршрутизатор конвертации изображений.
///
/// Ключевая идея: движок выбирается по РЕАЛЬНЫМ возможностям пары «источник → цель»,
/// а не по полю engine в таблице форматов. Поле engine описывает основной обработчик
/// для интерфейса, но решать должен тот, кто действительно умеет эту пару.
///
/// Порядок предпочтения:
///   1. Skia — если умеет и читать источник, и писать цель. Это путь в процессе, ~70 мс.
///   2. sips — если умеет и то и другое. Это +45 мс на процесс, зато 59 форматов.
///   3. Через PNG — когда движки разные. PNG читают оба, поэтому он универсальный посредник.
///
/// Пример: PNG → TIFF идёт в sips (Skia не пишет TIFF), HEIC → PNG идёт в sips напрямую,
/// HEIC → WebP идёт в два шага (sips делает PNG, Skia делает WebP).
/// </summary>
public static class ImagePipeline
{
    public static void Convert(
        string inputPath,
        string outputPath,
        string targetFormatId,
        int quality,
        CapabilityRegistry registry,
        int? maxDimension = null)
    {
        var target = registry.ByExtension(targetFormatId)
            ?? throw new NotSupportedException($"Unknown target format: {targetFormatId}");

        if (!target.CanWrite)
        {
            throw new NotSupportedException(
                $"Format \"{target.Label ?? targetFormatId}\" is read-only.");
        }

        if (!registry.IsAvailable(target))
        {
            throw new NotSupportedException(
                $"Format \"{target.Label ?? targetFormatId}\" requires a module. " +
                "Install it in the Modules section.");
        }

        // Берём КАНОНИЧЕСКИЙ идентификатор из таблицы, а не сырое расширение.
        //
        // Разница принципиальная: у одной строки таблицы бывает несколько расширений
        // («jpg» и «jpeg» — это одна запись «jpeg»), а движки знают только
        // идентификатор. С сырым расширением файл «photo.jpg» не читался вовсе:
        // движок искал «jpg» и не находил.
        var source = registry.ByPath(inputPath);
        var sourceId = source?.Id ?? string.Empty;

        var sipsAvailable = SipsImageEngine.IsAvailable;

        // 1. Skia на оба конца — самый быстрый путь.
        if (SkiaImageEngine.CanRead(sourceId) && SkiaImageEngine.CanWrite(target.Id))
        {
            SkiaImageEngine.ConvertFile(inputPath, outputPath, target.Id, quality, maxDimension);
            return;
        }

        // 2. sips на оба конца — прямая конвертация без посредника.
        if (sipsAvailable && SipsImageEngine.CanRead(sourceId) && SipsImageEngine.CanWrite(target.Id))
        {
            SipsImageEngine.Convert(inputPath, outputPath, target.Id, quality, maxDimension);
            return;
        }

        // 3. Движки разные — через PNG.
        ConvertThroughPng(inputPath, outputPath, target.Id, quality, sourceId, sipsAvailable, maxDimension);
    }

    private static void ConvertThroughPng(
        string inputPath,
        string outputPath,
        string targetFormatId,
        int quality,
        string sourceId,
        bool sipsAvailable,
        int? maxDimension)
    {
        // Если источник уже PNG, посредник не нужен — отдаём его целевому движку напрямую.
        if (string.Equals(sourceId, "png", StringComparison.OrdinalIgnoreCase))
        {
            if (sipsAvailable && SipsImageEngine.CanWrite(targetFormatId))
            {
                SipsImageEngine.Convert(inputPath, outputPath, targetFormatId, quality, maxDimension);
                return;
            }

            SkiaImageEngine.ConvertFile(inputPath, outputPath, targetFormatId, quality, maxDimension);
            return;
        }

        var intermediate = Path.Combine(Path.GetTempPath(), $"nekoconv-{Guid.NewGuid():N}.png");

        try
        {
            // Шаг 1: источник → PNG. Читает тот движок, который источник умеет.
            if (sipsAvailable && SipsImageEngine.CanRead(sourceId))
            {
                SipsImageEngine.ToPng(inputPath, intermediate);
            }
            else if (SkiaImageEngine.CanRead(sourceId))
            {
                SkiaImageEngine.ConvertFile(inputPath, intermediate, "png");
            }
            else
            {
                throw new NotSupportedException(
                    $"Source format \"{(sourceId.Length > 0 ? sourceId : "unknown")}\" is not supported.");
            }

            // Шаг 2: PNG → цель. Масштабирование делаем здесь, чтобы не платить за него дважды.
            if (sipsAvailable && SipsImageEngine.CanWrite(targetFormatId))
            {
                SipsImageEngine.Convert(intermediate, outputPath, targetFormatId, quality, maxDimension);
            }
            else if (SkiaImageEngine.CanWrite(targetFormatId))
            {
                SkiaImageEngine.ConvertFile(intermediate, outputPath, targetFormatId, quality, maxDimension);
            }
            else
            {
                throw new NotSupportedException(
                    $"Writing format \"{targetFormatId}\" is not supported.");
            }
        }
        finally
        {
            if (File.Exists(intermediate))
            {
                try
                {
                    File.Delete(intermediate);
                }
                catch
                {
                    // Временный файл не критичен: ОС почистит его сама.
                }
            }
        }
    }
}
