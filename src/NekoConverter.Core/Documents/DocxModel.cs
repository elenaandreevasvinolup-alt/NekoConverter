namespace NekoConverter.Core.Documents;

/// <summary>Стиль абзаца, определяемый по w:pStyle и w:outlineLvl.</summary>
public enum ParagraphStyle
{
    Body,
    Title,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    ListItem,
}

/// <summary>
/// Фрагмент текста с одинаковым форматированием.
/// SizePt — размер в пунктах (в DOCX размер хранится в полупунктах).
/// </summary>
public sealed record DocxRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    double SizePt = 11.0);

/// <summary>Блок документа: абзац, таблица или изображение.</summary>
public abstract record DocxBlock;

/// <summary>Абзац. Runs пустой — значит пустая строка (вертикальный отступ).</summary>
public sealed record ParagraphBlock(
    IReadOnlyList<DocxRun> Runs,
    ParagraphStyle Style = ParagraphStyle.Body,
    bool IsListItem = false) : DocxBlock
{
    public string Text => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// Таблица: строки -> ячейки -> фрагменты текста.
/// Объединение ячеек (gridSpan / vMerge) в первой версии не поддерживается.
/// </summary>
public sealed record TableBlock(
    IReadOnlyList<IReadOnlyList<IReadOnlyList<DocxRun>>> Rows) : DocxBlock;

/// <summary>
/// Встроенное изображение. Размер берётся из wp:extent и уже переведён в пункты
/// (1 пункт = 12700 EMU).
/// </summary>
public sealed record ImageBlock(
    byte[] Data,
    double WidthPt,
    double HeightPt) : DocxBlock;

/// <summary>Разобранный DOCX: плоский список блоков в исходном порядке.</summary>
public sealed record DocxDocument(IReadOnlyList<DocxBlock> Blocks);
