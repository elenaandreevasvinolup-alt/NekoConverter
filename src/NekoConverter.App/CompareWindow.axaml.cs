using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NekoConverter.App.Localization;
using NekoConverter.Core;
using NekoConverter.Core.Preview;

namespace NekoConverter.App;

/// <summary>
/// Окно сравнения исходного файла и результата.
///
/// Отдельное окно, а не панель внутри главного: сравнивать имеет смысл только
/// когда обе картинки видны целиком, а в узкой колонке страницы это невозможно.
/// </summary>
public partial class CompareWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _sizeChangeText;
    private readonly TextBlock _originalTitleText;
    private readonly TextBlock _resultTitleText;
    private readonly TextBlock _originalMetaText;
    private readonly TextBlock _resultMetaText;
    private readonly SelectableTextBlock _originalLinesText;
    private readonly SelectableTextBlock _resultLinesText;
    private readonly Border _originalImageFrame;
    private readonly Border _resultImageFrame;
    private readonly Image _originalImage;
    private readonly Image _resultImage;
    private readonly Button _revealButton;
    private readonly Button _closeButton;

    private readonly string _resultPath;

    public CompareWindow()
        : this(string.Empty, string.Empty)
    {
    }

    public CompareWindow(string originalPath, string resultPath)
    {
        _resultPath = resultPath;

        InitializeComponent();

        _titleText = Require<TextBlock>("TitleText");
        _sizeChangeText = Require<TextBlock>("SizeChangeText");
        _originalTitleText = Require<TextBlock>("OriginalTitleText");
        _resultTitleText = Require<TextBlock>("ResultTitleText");
        _originalMetaText = Require<TextBlock>("OriginalMetaText");
        _resultMetaText = Require<TextBlock>("ResultMetaText");
        _originalLinesText = Require<SelectableTextBlock>("OriginalLinesText");
        _resultLinesText = Require<SelectableTextBlock>("ResultLinesText");
        _originalImageFrame = Require<Border>("OriginalImageFrame");
        _resultImageFrame = Require<Border>("ResultImageFrame");
        _originalImage = Require<Image>("OriginalImage");
        _resultImage = Require<Image>("ResultImage");
        _revealButton = Require<Button>("RevealButton");
        _closeButton = Require<Button>("CloseButton");

        Title = Localizer.Get("compare.title");
        _titleText.Text = Localizer.Get("compare.title");
        _originalTitleText.Text = Localizer.Get("compare.original");
        _resultTitleText.Text = Localizer.Get("compare.result");
        _revealButton.Content = Localizer.Get("compare.reveal");
        _closeButton.Content = Localizer.Get("compare.close");

        _closeButton.Click += (_, _) => Close();
        _revealButton.Click += OnReveal;

        if (originalPath.Length > 0 && resultPath.Length > 0)
        {
            Load(originalPath, resultPath);
        }
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"XAML element x:Name=\"{name}\" is missing.");

    /// <summary>Строит сравнение. Ошибки гасятся: окно не должно падать из-за одного файла.</summary>
    public void Load(string originalPath, string resultPath)
    {
        CompareResult comparison;

        try
        {
            comparison = CompareService.Build(
                originalPath,
                resultPath,
                ConversionService.Registry,
                Previewers.All);
        }
        catch (Exception ex)
        {
            _sizeChangeText.Text = Localizer.Format("compare.failed", ex.Message);
            return;
        }

        _sizeChangeText.Text = BuildSizeChangeText(comparison);

        ShowSide(comparison.Original, _originalMetaText, _originalLinesText, _originalImage, _originalImageFrame);
        ShowSide(comparison.Result, _resultMetaText, _resultLinesText, _resultImage, _resultImageFrame);
    }

    /// <summary>
    /// Главная строка: насколько изменился размер.
    /// Знак важен: рост размера при переходе в формат без потерь — это нормально,
    /// а рост при переходе в формат с потерями означает, что качество выбрано зря.
    /// </summary>
    private static string BuildSizeChangeText(CompareResult comparison)
    {
        var original = FormatBytes(comparison.Original.SizeBytes);
        var result = FormatBytes(comparison.Result.SizeBytes);

        if (comparison.SizeRatio is not { } ratio)
        {
            return $"{original} → {result}";
        }

        var percent = Math.Abs(ratio) * 100;

        var direction = ratio switch
        {
            < -0.005 => Localizer.Format("compare.smaller", percent.ToString("F1")),
            > 0.005 => Localizer.Format("compare.larger", percent.ToString("F1")),
            _ => Localizer.Get("compare.same"),
        };

        return $"{original} → {result} · {direction}";
    }

    private static void ShowSide(
        CompareSide side,
        TextBlock meta,
        SelectableTextBlock lines,
        Image image,
        Border imageFrame)
    {
        var parts = new List<string> { side.Label };

        if (side.Width is { } width && side.Height is { } height)
        {
            parts.Add($"{width}×{height}");
        }

        if (side.Details is { Length: > 0 } details)
        {
            parts.Add(details);
        }

        parts.Add(FormatBytes(side.SizeBytes));

        meta.Text = string.Join(" · ", parts);

        lines.Text = side.Lines.Count > 0 ? string.Join('\n', side.Lines) : "";

        if (side.ThumbnailPng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png);
                image.Source = new Bitmap(stream);
                imageFrame.IsVisible = true;
            }
            catch
            {
                imageFrame.IsVisible = false;
            }
        }
        else
        {
            imageFrame.IsVisible = false;
            image.Source = null;
        }
    }

    /// <summary>Показывает результат в Finder или проводнике.</summary>
    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_resultPath.Length == 0 || !File.Exists(_resultPath))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", _resultPath]);
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", ["/select,", _resultPath]);
            }
            else
            {
                Process.Start("xdg-open", [Path.GetDirectoryName(_resultPath) ?? "."]);
            }
        }
        catch
        {
            // Не удалось открыть проводник — не повод показывать ошибку.
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1048576.0:F2} MB",
    };
}
