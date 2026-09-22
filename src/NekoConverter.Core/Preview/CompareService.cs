namespace NekoConverter.Core.Preview;

/// <param name="Path">Путь к файлу.</param>
/// <param name="Label">Название формата.</param>
/// <param name="SizeBytes">Размер файла.</param>
/// <param name="Width">Ширина изображения, если известно.</param>
/// <param name="Height">Высота изображения, если известно.</param>
/// <param name="Details">Нейтральные подробности: «44100 Hz · 2 ch · 0:01».</param>
/// <param name="ThumbnailPng">Миниатюра в PNG.</param>
/// <param name="Lines">Первые строки для текстовых форматов.</param>
public sealed record CompareSide(
    string Path,
    string Label,
    long SizeBytes,
    int? Width,
    int? Height,
    string? Details,
    byte[]? ThumbnailPng,
    IReadOnlyList<string> Lines);

/// <param name="Original">Исходный файл.</param>
/// <param name="Result">Результат преобразования.</param>
/// <param name="SizeRatio">
/// Изменение размера в долях: -0.62 значит «стало на 62 % меньше».
/// null — размер исходного неизвестен.
/// </param>
public sealed record CompareResult(CompareSide Original, CompareSide Result, double? SizeRatio);

/// <summary>
/// Сравнение исходного файла и результата.
///
/// Главное число здесь — изменение размера. Именно его смотрят, когда проверяют,
/// приемлемы ли потери: качество оценивается глазами, а цена этого качества
/// измеряется процентами. Без этого числа сравнение сводится к «на глазок».
/// </summary>
public static class CompareService
{
    public static CompareResult Build(
        string originalPath,
        string resultPath,
        Formats.CapabilityRegistry registry,
        Previewers enabled)
    {
        var original = BuildSide(originalPath, registry, enabled);
        var result = BuildSide(resultPath, registry, enabled);

        double? ratio = original.SizeBytes > 0
            ? (double)(result.SizeBytes - original.SizeBytes) / original.SizeBytes
            : null;

        return new CompareResult(original, result, ratio);
    }

    private static CompareSide BuildSide(
        string path,
        Formats.CapabilityRegistry registry,
        Previewers enabled)
    {
        var preview = PreviewService.Describe(path, registry, enabled);
        var info = new FileInfo(path);

        // Предпросмотр может быть отключён для этой категории: тогда стороны
        // всё равно показываются, просто без миниатюры и строк.
        return new CompareSide(
            path,
            preview.Label.Length > 0 ? preview.Label : Path.GetExtension(path).TrimStart('.'),
            info.Exists ? info.Length : 0,
            preview.Width,
            preview.Height,
            preview.Details,
            preview.ThumbnailPng,
            preview.Content);
    }
}
