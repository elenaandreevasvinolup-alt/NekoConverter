#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Заменяет одиночный режим страницы «Преобразовать» на пакетный.

Что меняется:
  • поля страницы — вместо выбора одного файла и одного формата теперь список
    файлов, общие настройки вывода и контейнер для групп;
  • методы страницы — загрузка нескольких файлов, группировка по категориям,
    построение карточек групп и пакетный запуск.

Остальные страницы (модули, настройки, о программе) не затрагиваются.
"""
import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
path = os.path.join(ROOT, 'src', 'NekoConverter.App', 'MainWindow.axaml.cs')

with io.open(path, encoding='utf-8') as f:
    s = f.read()

# ─────────── 1. Поля ───────────

old_fields_start = '    private readonly Panel _emptyState;'
old_fields_end = '    private readonly TextBlock _statusDetailText;'

i = s.index(old_fields_start)
j = s.index(old_fields_end) + len(old_fields_end)

new_fields = '''    private readonly Panel _emptyState;
    private readonly Rectangle _dropZoneShape;
    private readonly TextBlock _dropGlyph;
    private readonly TextBlock _dropTitle;
    private readonly TextBlock _dropHint;
    private readonly Button _pickFileButton;
    private readonly StackPanel _fileState;

    private readonly TextBlock _filesSummaryText;
    private readonly TextBlock _filesListText;
    private readonly Button _addFilesButton;
    private readonly Button _clearFilesButton;
    private readonly TextBox _sourceInputBox;
    private readonly Button _addFromInputButton;
    private readonly Button _pasteImageButton;

    private readonly CheckBox _useCustomOutput;
    private readonly StackPanel _customOutputPanel;
    private readonly Button _pickOutputFolderButton;
    private readonly TextBlock _outputFolderText;
    private readonly TextBox _nameTemplateBox;
    private readonly TextBlock _templateTokensText;

    private readonly StackPanel _groupList;
    private readonly Button _convertAllButton;
    private readonly Border _batchProgressCard;
    private readonly TextBlock _batchProgressText;
    private readonly ProgressBar _batchProgressBar;

    private readonly Border _previewCard;
    private readonly Border _previewImageFrame;
    private readonly Image _previewImage;
    private readonly TextBlock _previewSummaryText;
    private readonly SelectableTextBlock _previewLinesText;
    private readonly CheckBox _previewThumbnail;
    private readonly CheckBox _previewText;
    private readonly CheckBox _previewAudio;
    private readonly CheckBox _previewMedia;
    private readonly CheckBox _previewMesh;

    private readonly Border _statusCard;
    private readonly TextBlock _statusText;
    private readonly TextBlock _statusDetailText;'''

s = s[:i] + new_fields + s[j:]

# ─────────── 2. Методы страницы ───────────

start_marker = '    // ─────────────────────────── Состояние файла ───────────────────────────'
end_marker = '    // ─────────────────────────── Страница «模块» ───────────────────────────'

a = s.index(start_marker)
b = s.index(end_marker)

new_code = '''    // ─────────────────────────── Пакетный режим ───────────────────────────

    /// <summary>Сколько файлов принимаем за раз. Ограничение осознанное: это
    /// удерживает интерфейс от превращения в файловый менеджер.</summary>
    private const int MaxFiles = 10;

    /// <summary>Загруженный файл.</summary>
    private sealed class LoadedFile(string path, FormatKind kind, string label)
    {
        public string Path { get; } = path;

        public FormatKind Kind { get; } = kind;

        public string Label { get; } = label;

        /// <summary>Целевой формат именно для этого файла. null — как в группе.</summary>
        public string? TargetFormatId { get; set; }

        /// <summary>Качество именно для этого файла. null — как в группе.</summary>
        public int? Quality { get; set; }
    }

    /// <summary>
    /// Группа файлов одной категории со своими настройками и карточкой.
    ///
    /// Смысл группировки: у трёх аудиофайлов настройки одинаковые, и задавать их
    /// трижды незачем. Но если нужно — группа раскрывается, и каждый файл
    /// настраивается отдельно.
    /// </summary>
    private sealed class KindGroup(FormatKind kind)
    {
        public FormatKind Kind { get; } = kind;

        public List<LoadedFile> Files { get; } = [];

        public string? TargetFormatId { get; set; }

        public int Quality { get; set; } = 90;

        public bool Expanded { get; set; }

        // Элементы карточки создаются при построении.
        public TextBlock SummaryText { get; set; } = null!;

        public AutoCompleteBox TargetBox { get; set; } = null!;

        public Slider QualitySlider { get; set; } = null!;

        public TextBlock QualityText { get; set; } = null!;

        public Border QualityRow { get; set; } = null!;

        public StackPanel ItemList { get; set; } = null!;

        public Button ExpandButton { get; set; } = null!;

        /// <summary>Строки состояния по каждому файлу, чтобы обновлять их при обработке.</summary>
        public List<(LoadedFile File, TextBlock Status, TextBlock Details)> Rows { get; } = [];
    }

    private readonly List<LoadedFile> _files = [];
    private readonly List<KindGroup> _groups = [];
    private string? _outputFolderPath;

    /// <summary>Показывает пустое состояние или список файлов.</summary>
    private void UpdateEmptyState()
    {
        var hasFiles = _files.Count > 0;

        // Пока файлов нет, на странице нет ни форматов, ни настроек —
        // только одна строка-приглашение.
        _emptyState.IsVisible = !hasFiles;
        _fileState.IsVisible = hasFiles;
        _statusCard.IsVisible = hasFiles;

        if (!hasFiles)
        {
            _previewCard.IsVisible = false;

            var registry = ConversionService.Registry;
            var available = registry.All.Count(registry.IsAvailable);

            _dropHint.Text = Localizer.Format("drop.formats_hint", available);
            _dropTitle.Text = Localizer.Get(CanDropFiles ? "drop.select_or_drop" : "drop.select_only");
            SetDropActive(false);
        }
    }

    private async void OnPickOutputFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Get("output.pick"),
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        _outputFolderPath = folders[0].Path.LocalPath;
        _outputFolderText.Text = _outputFolderPath;
    }

    private async void OnPickFile(object? sender, RoutedEventArgs e)
    {
        var registry = ConversionService.Registry;

        var patterns = registry.All
            .SelectMany(f => f.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => "*." + x)
            .ToList();

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get("pick.title"),
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(Localizer.Format("pick.supported", patterns.Count)) { Patterns = patterns },
                new(Localizer.Get("pick.all")) { Patterns = new List<string> { "*" } },
            },
        });

        AddFiles(files.Select(f => f.Path.LocalPath));
    }

    /// <summary>
    /// Добавляет файлы в пакет и перестраивает группы.
    /// Лишние отбрасываются с сообщением: молча терять файлы нельзя.
    /// </summary>
    private void AddFiles(IEnumerable<string> paths)
    {
        var registry = ConversionService.Registry;
        var skipped = 0;
        var unknown = 0;

        foreach (var path in paths)
        {
            if (_files.Count >= MaxFiles)
            {
                skipped++;
                continue;
            }

            if (!File.Exists(path) || _files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var source = registry.ByPath(path);

            if (source is null)
            {
                unknown++;
                continue;
            }

            _files.Add(new LoadedFile(path, source.Kind, source.Label ?? source.Id));
        }

        RebuildGroups();
        UpdatePreview();

        if (skipped > 0)
        {
            SetStatus(Localizer.Format("files.max", MaxFiles));
        }
        else if (unknown > 0)
        {
            SetStatus(Localizer.Get("file.unknown").Replace("{0}", unknown.ToString()));
        }
        else
        {
            SetStatus(Localizer.Get("status.file_selected"));
        }
    }

    /// <summary>Собирает файлы в группы по категориям и перерисовывает карточки.</summary>
    private void RebuildGroups()
    {
        _groups.Clear();

        foreach (var kind in _files.Select(f => f.Kind).Distinct().OrderBy(k => k.ToString()))
        {
            var group = new KindGroup(kind);
            group.Files.AddRange(_files.Where(f => f.Kind == kind));
            _groups.Add(group);
        }

        _groupList.Children.Clear();

        foreach (var group in _groups)
        {
            _groupList.Children.Add(BuildGroupCard(group));
        }

        UpdateFilesSummary();
        UpdateEmptyState();
    }

    private void UpdateFilesSummary()
    {
        _filesSummaryText.Text = Localizer.Format("files.summary", _files.Count, _groups.Count);

        _filesListText.Text = _files.Count == 0
            ? ""
            : string.Join("\\n", _files.Select(f => Path.GetFileName(f.Path)));

        _templateTokensText.Text = Localizer.Format(
            "output.tokens", string.Join("  ", NameTemplate.Tokens));
    }

    /// <summary>
    /// Карточка одной категории: общие настройки группы плюс, при раскрытии,
    /// строки отдельных файлов.
    /// </summary>
    private Border BuildGroupCard(KindGroup group)
    {
        var content = new StackPanel { Spacing = 12 };

        // ── заголовок: категория, количество, кнопка раскрытия ──
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        group.SummaryText = new TextBlock
        {
            Text = Localizer.Format("group.title", KindName(group.Kind), group.Files.Count),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(group.SummaryText, 0);
        header.Children.Add(group.SummaryText);

        group.ExpandButton = new Button
        {
            Classes = { "ghost" },
            Content = Localizer.Get("group.expand"),
        };
        group.ExpandButton.Click += (_, _) =>
        {
            group.Expanded = !group.Expanded;
            group.ExpandButton.Content = Localizer.Get(group.Expanded ? "group.collapse" : "group.expand");
            group.ItemList.IsVisible = group.Expanded;
        };
        Grid.SetColumn(group.ExpandButton, 1);
        header.Children.Add(group.ExpandButton);

        content.Children.Add(header);

        // ── целевой формат: с поиском по списку ──
        var targetRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var targetLabel = new TextBlock
        {
            Text = Localizer.Get("target.title"),
            Classes = { "label" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(targetLabel, 0);
        targetRow.Children.Add(targetLabel);

        // AutoCompleteBox, а не ComboBox: форматов больше сотни, и выбрать нужный
        // прокруткой нереально — нужен поиск по вводу.
        var registry = ConversionService.Registry;

        var choices = registry.For(group.Kind)
            .Where(f => f.CanWrite)
            .OrderBy(f => registry.CanWriteNow(f) ? 0 : 1)
            .ThenBy(f => f.Label ?? f.Id, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FormatChoice(f, FormatChoiceText(f, registry)))
            .ToList();

        group.TargetBox = new AutoCompleteBox
        {
            ItemsSource = choices.Select(c => c.Display).ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Ставим значение по умолчанию: первый доступный формат, отличный от исходного.
        var defaultChoice = choices.FirstOrDefault(c => registry.CanWriteNow(c.Format)) ?? choices.FirstOrDefault();

        if (defaultChoice is not null)
        {
            group.TargetBox.Text = defaultChoice.Display;
            group.TargetFormatId = defaultChoice.Format.Id;
        }

        group.TargetBox.SelectionChanged += (_, _) =>
        {
            var index = group.TargetBox.SelectedIndex;

            if (index >= 0 && index < choices.Count)
            {
                group.TargetFormatId = choices[index].Format.Id;
                UpdateGroupQualityVisibility(group, choices);
            }
        };

        Grid.SetColumn(group.TargetBox, 1);
        targetRow.Children.Add(group.TargetBox);

        content.Children.Add(targetRow);

        // ── качество: показываем только для форматов с потерями ──
        group.QualityRow = new Border { IsVisible = false };

        var qualityContent = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var qualityLabel = new TextBlock
        {
            Text = Localizer.Get("quality.title"),
            Classes = { "label" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(qualityLabel, 0);
        qualityContent.Children.Add(qualityLabel);

        group.QualitySlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Value = group.Quality,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(group.QualitySlider, 1);
        qualityContent.Children.Add(group.QualitySlider);

        group.QualityText = new TextBlock
        {
            Classes = { "label" },
            Margin = new Thickness(14, 0, 0, 0),
            MinWidth = 72,
        };
        Grid.SetColumn(group.QualityText, 2);
        qualityContent.Children.Add(group.QualityText);

        group.QualityRow.Child = qualityContent;
        content.Children.Add(group.QualityRow);

        group.QualitySlider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                group.Quality = (int)group.QualitySlider.Value;
                UpdateQualityLabel(group);
            }
        };

        // ── строки отдельных файлов ──
        group.ItemList = new StackPanel { Spacing = 8, IsVisible = false };

        foreach (var file in group.Files)
        {
            group.ItemList.Children.Add(BuildItemRow(group, file));
        }

        content.Children.Add(group.ItemList);

        UpdateGroupQualityVisibility(group, choices);
        UpdateQualityLabel(group);

        return new Border { Classes = { "card" }, Child = content };
    }

    private void UpdateGroupQualityVisibility(KindGroup group, List<FormatChoice> choices)
    {
        var lossy = group.TargetFormatId is { } id && IsLossyFormat(id);
        group.QualityRow.IsVisible = lossy;
    }

    private static string KindName(FormatKind kind) => kind switch
    {
        FormatKind.Image => "Image",
        FormatKind.Audio => "Audio",
        FormatKind.Video => "Video",
        FormatKind.Document => "Document",
        FormatKind.Data => "Data",
        FormatKind.Subtitle => "Subtitle",
        FormatKind.Model3D => "3D",
        _ => kind.ToString(),
    };

    private static string FormatChoiceText(FormatDescriptor format, CapabilityRegistry registry) =>
        registry.CanWriteNow(format)
            ? $"{format.Label ?? format.Id}{(format.Note is null ? "" : $" — {format.Note}")}"
            : $"{format.Label ?? format.Id} — {Localizer.Get("file.needs_module")}";

    /// <summary>Строка одного файла: имя, состояние, результат.</summary>
    private Control BuildItemRow(KindGroup group, LoadedFile file)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        var name = new TextBlock
        {
            Text = Path.GetFileName(file.Path),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var status = new TextBlock
        {
            Text = Localizer.Get("item.pending"),
            Classes = { "engineBadge" },
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(status, 1);
        row.Children.Add(status);

        var details = new TextBlock
        {
            Text = "",
            Classes = { "hint" },
            MaxWidth = 300,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(details, 2);
        row.Children.Add(details);

        group.Rows.Add((file, status, details));

        return row;
    }

    private void UpdateQualityLabel(KindGroup group)
    {
        var value = group.Quality;

        var key = value switch
        {
            >= 95 => "quality.highest",
            >= 85 => "quality.high",
            >= 70 => "quality.medium",
            >= 50 => "quality.small",
            _ => "quality.smallest",
        };

        group.QualityText.Text = $"{Localizer.Get(key)} ({value})";
    }

    /// <summary>Добавляет файл по пути или скачивает по ссылке.</summary>
    private async void OnAddFromInput(object? sender, RoutedEventArgs e)
    {
        var text = _sourceInputBox.Text?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Ссылка: скачиваем во временный файл и работаем с ним как с обычным.
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(Localizer.Get("input.downloading"));

            try
            {
                var downloaded = await DownloadToTempAsync(text);
                AddFiles([downloaded]);
                _sourceInputBox.Text = "";
            }
            catch (Exception ex)
            {
                SetStatus(Localizer.Format("status.failed", ex.Message));
            }

            return;
        }

        // Обычный путь: раскрываем ~ и относительные пути.
        var path = text.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text.TrimStart('~', '/'))
            : text;

        if (!File.Exists(path))
        {
            SetStatus(Localizer.Format("status.failed", path));
            return;
        }

        AddFiles([path]);
        _sourceInputBox.Text = "";
    }

    private static async Task<string> DownloadToTempAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var bytes = await client.GetByteArrayAsync(url);

        // Имя берём из адреса, чтобы расширение сохранилось: по нему определяется формат.
        var name = Path.GetFileName(new Uri(url).AbsolutePath);

        if (string.IsNullOrEmpty(name))
        {
            name = "download";
        }

        var directory = Path.Combine(Path.GetTempPath(), "nekoconvert-inputs");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{name}");
        await File.WriteAllBytesAsync(path, bytes);

        return path;
    }

    /// <summary>Вставляет изображение из буфера обмена.</summary>
    private async void OnPasteImage(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            // Формат DataFormat.File покрывает и картинку, скопированную как файл,
            // и файл, скопированный в Finder.
            var files = await clipboard.GetDataAsync(DataFormat.File);

            if (files is IEnumerable<Avalonia.Platform.Storage.IStorageItem> items)
            {
                var paths = items.Select(i => i.Path.LocalPath).ToList();

                if (paths.Count > 0)
                {
                    AddFiles(paths);
                    return;
                }
            }

            SetStatus(Localizer.Get("input.paste_failed"));
        }
        catch (Exception ex)
        {
            SetStatus(Localizer.Format("status.failed", ex.Message));
        }
    }

    /// <summary>Запускает пакетную обработку всех групп.</summary>
    private async void OnConvertAll(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
        {
            SetStatus(Localizer.Get("error.no_file"));
            return;
        }

        if (_useCustomOutput.IsChecked == true && string.IsNullOrEmpty(_outputFolderPath))
        {
            SetStatus(Localizer.Get("output.pick"));
            return;
        }

        // Собираем задания: у каждого файла либо своё значение, либо значение группы.
        var requests = new List<BatchItemRequest>();

        foreach (var group in _groups)
        {
            foreach (var file in group.Files)
            {
                requests.Add(new BatchItemRequest(
                    file.Path,
                    file.TargetFormatId ?? group.TargetFormatId,
                    file.Quality ?? group.Quality));
            }
        }

        var batch = new BatchRequest(
            requests,
            _useCustomOutput.IsChecked == true ? _outputFolderPath : null,
            _nameTemplateBox.Text);

        SetBusy(true);
        _batchProgressCard.IsVisible = true;
        _batchProgressBar.Value = 0;

        // Все строки снова «в ожидании»: иначе после второго запуска
        // останутся отметки от первого.
        foreach (var group in _groups)
        {
            foreach (var (_, status, details) in group.Rows)
            {
                status.Text = Localizer.Get("item.pending");
                details.Text = "";
            }
        }

        var progress = new Progress<BatchProgress>(p =>
        {
            _batchProgressBar.Value = p.Fraction * 100;

            if (p.Current is { } current)
            {
                _batchProgressText.Text = Localizer.Format(
                    "progress.batch", p.Completed, p.Total, Path.GetFileName(current.InputPath));

                MarkRow(current);
            }
        });

        try
        {
            var summary = await ConversionService.RunBatchAsync(batch, progress);

            _batchProgressText.Text = Localizer.Format(
                "progress.summary", summary.Succeeded, summary.Failed, requests.Count);

            SetStatus(Localizer.Format(
                "status.details", summary.TotalBytes.ToString("N0"), "—", "batch"));
        }
        catch (Exception ex)
        {
            SetStatus(Localizer.Format("status.failed", ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Обновляет строку файла по результату его обработки.</summary>
    private void MarkRow(BatchItemResult result)
    {
        foreach (var group in _groups)
        {
            foreach (var (file, status, details) in group.Rows)
            {
                if (!string.Equals(file.Path, result.InputPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                status.Text = Localizer.Get(result.Success ? "item.done" : "item.failed");
                details.Text = result.Success
                    ? $"{FormatBytes(result.Bytes)} · {result.Elapsed.TotalMilliseconds:F0} ms"
                    : result.Error ?? "";

                return;
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _pickFileButton.IsEnabled = !busy;
        _addFilesButton.IsEnabled = !busy;
        _clearFilesButton.IsEnabled = !busy;
        _convertAllButton.IsEnabled = !busy;
        _pasteImageButton.IsEnabled = !busy;
        _addFromInputButton.IsEnabled = !busy;
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
        _statusCard.IsVisible = true;
    }

    private void OnClearFiles(object? sender, RoutedEventArgs e)
    {
        _files.Clear();
        RebuildGroups();
        UpdatePreview();
        _batchProgressCard.IsVisible = false;
    }

    // ─────────────────────────── Предпросмотр ───────────────────────────

    /// <summary>
    /// Показывает предпросмотр первого файла пакета.
    /// Сбой предпросмотра не должен мешать преобразованию, поэтому ошибки гасятся.
    /// </summary>
    private void UpdatePreview()
    {
        if (_files.Count == 0 || _settings.Previewers == Previewers.None)
        {
            _previewCard.IsVisible = false;
            return;
        }

        FilePreview preview;

        try
        {
            preview = PreviewService.Describe(
                _files[0].Path, ConversionService.Registry, _settings.Previewers);
        }
        catch
        {
            _previewCard.IsVisible = false;
            return;
        }

        if (!preview.HasAnything && preview.Error is null)
        {
            _previewCard.IsVisible = false;
            return;
        }

        _previewCard.IsVisible = true;
        _previewSummaryText.Text = BuildPreviewSummary(preview);

        var content = preview.Content;
        _previewLinesText.Text = content.Count > 0 ? string.Join('\\n', content) : "";

        if (preview.ThumbnailPng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png);
                _previewImage.Source = new Bitmap(stream);
                _previewImageFrame.IsVisible = true;
            }
            catch
            {
                _previewImageFrame.IsVisible = false;
            }
        }
        else
        {
            _previewImageFrame.IsVisible = false;
            _previewImage.Source = null;
        }
    }

    /// <summary>
    /// Собирает подпись предпросмотра на языке интерфейса.
    /// Ядро отдаёт только данные, формулировки — здесь.
    /// </summary>
    private static string BuildPreviewSummary(FilePreview preview)
    {
        if (preview.Error is { Length: > 0 } error)
        {
            return Localizer.Format("preview.unavailable", error);
        }

        var parts = new List<string> { preview.Label };

        if (preview.Width is { } width && preview.Height is { } height)
        {
            parts.Add($"{width}×{height}");
        }

        if (preview.ShownLines is { } shown && preview.TotalLines is { } total)
        {
            parts.Add(Localizer.Format("preview.lines", shown, total));
        }

        if (preview.Details is { Length: > 0 } details)
        {
            parts.Add(details);
        }

        if (preview.SizeBytes > 0)
        {
            parts.Add(FormatBytes(preview.SizeBytes));
        }

        return string.Join(" · ", parts);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1048576.0:F2} MB",
    };

'''

s = s[:a] + new_code + s[b:]

# ─────────── 3. Убираем ненужную запись FormatChoice в конце ───────────
old_record = '''    /// <summary>Элемент списка форматов. ToString используется ComboBox для отображения.</summary>
    private sealed record FormatChoice(FormatDescriptor Format, string Display)
    {
        public override string ToString() => Display;
    }
'''
if old_record in s:
    s = s.replace(old_record, '''    /// <summary>Элемент списка форматов для выпадающего списка с поиском.</summary>
    private sealed record FormatChoice(FormatDescriptor Format, string Display)
    {
        public override string ToString() => Display;
    }
''')

with io.open(path, 'w', encoding='utf-8') as f:
    f.write(s)

print('MainWindow.axaml.cs: пакетный режим внедрён')
