using System.Globalization;
using System.Text;

namespace NekoConverter.Core.Batch;

/// <summary>Что делать с файлом, если такой уже есть.</summary>
public enum ExistingFileAction
{
    /// <summary>Перезаписать.</summary>
    Overwrite,

    /// <summary>Добавить числовой суффикс, чтобы не потерять прежний файл.</summary>
    Rename,

    /// <summary>Пропустить.</summary>
    Skip,
}

/// <param name="InputPath">Исходный файл.</param>
/// <param name="TargetFormatId">Целевой формат. null — значение по умолчанию для категории.</param>
/// <param name="Quality">Качество для форматов с потерями.</param>
/// <param name="MaxDimension">Ограничение размера изображения.</param>
public sealed record BatchItemRequest(
    string InputPath,
    string? TargetFormatId,
    int Quality = 90,
    int? MaxDimension = null);

/// <param name="Items">Файлы в порядке обработки.</param>
/// <param name="OutputDirectory">Общий каталог результата. null — рядом с исходными файлами.</param>
/// <param name="NameTemplate">Шаблон имени результата. null — имя исходного файла.</param>
/// <param name="ExistingFileAction">Что делать, если результат уже существует.</param>
public sealed record BatchRequest(
    IReadOnlyList<BatchItemRequest> Items,
    string? OutputDirectory = null,
    string? NameTemplate = null,
    ExistingFileAction ExistingFileAction = ExistingFileAction.Rename);

/// <param name="Index">Порядковый номер в пакете, с нуля.</param>
/// <param name="InputPath">Исходный файл.</param>
/// <param name="OutputPath">Результат, если он получился.</param>
/// <param name="Success">Удалось ли преобразование.</param>
/// <param name="Error">Текст ошибки, если не удалось.</param>
/// <param name="Bytes">Размер результата.</param>
/// <param name="Elapsed">Сколько заняло.</param>
public sealed record BatchItemResult(
    int Index,
    string InputPath,
    string? OutputPath,
    bool Success,
    string? Error,
    long Bytes,
    TimeSpan Elapsed);

/// <param name="Completed">Сколько файлов обработано.</param>
/// <param name="Total">Сколько всего.</param>
/// <param name="Current">Результат только что обработанного файла.</param>
public sealed record BatchProgress(int Completed, int Total, BatchItemResult? Current)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : 0;
}

/// <summary>Итог всего пакета.</summary>
public sealed record BatchSummary(IReadOnlyList<BatchItemResult> Results)
{
    public int Succeeded => Results.Count(r => r.Success);

    public int Failed => Results.Count(r => !r.Success);

    public long TotalBytes => Results.Where(r => r.Success).Sum(r => r.Bytes);
}

/// <summary>
/// Подстановка в шаблон имени результата.
///
/// Шаблон нужен именно при пакетной обработке: без него десять файлов лягут
/// в один каталог под своими именами и перемешаются. Доступные подстановки:
///   {name}   — имя исходного файла без расширения
///   {ext}    — расширение целевого формата
///   {format} — идентификатор целевого формата
///   {date}   — дата в виде 2026-09-22
///   {time}   — время в виде 23-58-12
///   {index}  — порядковый номер в пакете, начиная с единицы
/// </summary>
public static class NameTemplate
{
    public const string Default = "{name}.{ext}";

    /// <summary>Подстановки, которые понимает шаблон. Нужны интерфейсу для подсказки.</summary>
    public static readonly string[] Tokens =
        ["{name}", "{ext}", "{format}", "{date}", "{time}", "{index}"];

    public static string Apply(
        string? template,
        string inputPath,
        string targetFormatId,
        string targetExtension,
        int index)
    {
        var effective = string.IsNullOrWhiteSpace(template) ? Default : template;

        var result = effective
            .Replace("{name}", Path.GetFileNameWithoutExtension(inputPath), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", targetExtension.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", targetFormatId, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", DateTime.Now.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        // Имя не должно содержать разделителей пути: шаблон может прийти от пользователя.
        result = Sanitize(result);

        if (result.Length == 0)
        {
            result = Path.GetFileNameWithoutExtension(inputPath) + "." + targetExtension.TrimStart('.');
        }

        // Расширение дописываем, если шаблон его не содержит:
        // без расширения результат не откроется ничем.
        var extension = targetExtension.TrimStart('.');

        if (!result.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
        {
            result += "." + extension;
        }

        return result;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.ToString().Trim().TrimEnd('.');
    }
}
