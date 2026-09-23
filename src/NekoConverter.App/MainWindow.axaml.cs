using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

// Сборка под Android подключает пространство имён Android.Widget, где лежат
// собственные Button, CheckBox и ProgressBar. Без этих псевдонимов компилятор
// не знает, какой из двух типов имеется в виду, и падает на каждой ссылке.
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using ProgressBar = Avalonia.Controls.ProgressBar;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NekoConverter.Core;
using NekoConverter.App.Localization;
using NekoConverter.Core.Formats;
using NekoConverter.Core.Packaging;
using NekoConverter.Core.Batch;
using NekoConverter.Core.Preview;

namespace NekoConverter.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _timeThemeTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    // ── Навигация ──
    private readonly Border _sidebarPanel;
    private readonly Border _bottomNav;
    private readonly Button _tabConvert;
    private readonly Button _tabModules;
    private readonly Button _tabSettings;
    private readonly Button _tabAbout;
    private readonly Button _navConvert;
    private readonly Button _navModules;
    private readonly Button _navSettings;
    private readonly Button _navAbout;
    private readonly Button _themeQuickToggle;
    private readonly Ellipse _networkDot;
    private readonly TextBlock _networkText;
    private readonly ScrollViewer _convertPage;
    private readonly ScrollViewer _modulesPage;
    private readonly ScrollViewer _settingsPage;
    private readonly ScrollViewer _aboutPage;

    // ── Страница «转换» ──
    private readonly Panel _emptyState;
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
    private readonly TextBlock _statusDetailText;

    // ── Прочие страницы ──
    private readonly TextBlock _modulesSummaryText;
    private readonly StackPanel _packageList;
    private readonly SelectableTextBlock _mcpConfigText;
    private readonly Button _copyMcpConfigButton;
    private readonly TextBlock _mcpToolsText;
    private readonly Button _themeLight;
    private readonly Button _themeDark;
    private readonly Button _themeSystem;
    private readonly Button _themeByTime;
    private readonly ComboBox _languageBox;
    private readonly TextBlock _dataDirectoryText;
    private readonly TextBlock _aboutVersionText;
    private readonly TextBlock _builtinEnginesTitle;
    private readonly StackPanel _builtinEngineList;
    private readonly TextBlock _moduleEnginesTitle;
    private readonly StackPanel _moduleEngineList;
    private readonly TextBlock _noModuleEnginesText;
    private readonly TextBlock _licenseText;

    public MainWindow()
    {
        InitializeComponent();

        _sidebarPanel = Require<Border>("SidebarPanel");
        _bottomNav = Require<Border>("BottomNav");
        _tabConvert = Require<Button>("TabConvert");
        _tabModules = Require<Button>("TabModules");
        _tabSettings = Require<Button>("TabSettings");
        _tabAbout = Require<Button>("TabAbout");
        _navConvert = Require<Button>("NavConvert");
        _navModules = Require<Button>("NavModules");
        _navSettings = Require<Button>("NavSettings");
        _navAbout = Require<Button>("NavAbout");
        _themeQuickToggle = Require<Button>("ThemeQuickToggle");
        _networkDot = Require<Ellipse>("NetworkDot");
        _networkText = Require<TextBlock>("NetworkText");
        _convertPage = Require<ScrollViewer>("ConvertPage");
        _modulesPage = Require<ScrollViewer>("ModulesPage");
        _settingsPage = Require<ScrollViewer>("SettingsPage");
        _aboutPage = Require<ScrollViewer>("AboutPage");

        _emptyState = Require<Panel>("EmptyState");
        _dropZoneShape = Require<Rectangle>("DropZoneShape");
        _dropGlyph = Require<TextBlock>("DropGlyph");
        _dropTitle = Require<TextBlock>("DropTitle");
        _dropHint = Require<TextBlock>("DropHint");
        _pickFileButton = Require<Button>("PickFileButton");
        _fileState = Require<StackPanel>("FileState");

        _filesSummaryText = Require<TextBlock>("FilesSummaryText");
        _filesListText = Require<TextBlock>("FilesListText");
        _addFilesButton = Require<Button>("AddFilesButton");
        _clearFilesButton = Require<Button>("ClearFilesButton");
        _sourceInputBox = Require<TextBox>("SourceInputBox");
        _addFromInputButton = Require<Button>("AddFromInputButton");
        _pasteImageButton = Require<Button>("PasteImageButton");

        _useCustomOutput = Require<CheckBox>("UseCustomOutput");
        _customOutputPanel = Require<StackPanel>("CustomOutputPanel");
        _pickOutputFolderButton = Require<Button>("PickOutputFolderButton");
        _outputFolderText = Require<TextBlock>("OutputFolderText");
        _nameTemplateBox = Require<TextBox>("NameTemplateBox");
        _templateTokensText = Require<TextBlock>("TemplateTokensText");

        _groupList = Require<StackPanel>("GroupList");
        _convertAllButton = Require<Button>("ConvertAllButton");
        _batchProgressCard = Require<Border>("BatchProgressCard");
        _batchProgressText = Require<TextBlock>("BatchProgressText");
        _batchProgressBar = Require<ProgressBar>("BatchProgressBar");

        _previewCard = Require<Border>("PreviewCard");
        _previewImageFrame = Require<Border>("PreviewImageFrame");
        _previewImage = Require<Image>("PreviewImage");
        _previewSummaryText = Require<TextBlock>("PreviewSummaryText");
        _previewLinesText = Require<SelectableTextBlock>("PreviewLinesText");
        _previewThumbnail = Require<CheckBox>("PreviewThumbnail");
        _previewText = Require<CheckBox>("PreviewText");
        _previewAudio = Require<CheckBox>("PreviewAudio");
        _previewMedia = Require<CheckBox>("PreviewMedia");
        _previewMesh = Require<CheckBox>("PreviewMesh");
        _statusCard = Require<Border>("StatusCard");
        _statusText = Require<TextBlock>("StatusText");
        _statusDetailText = Require<TextBlock>("StatusDetailText");

        _modulesSummaryText = Require<TextBlock>("ModulesSummaryText");
        _packageList = Require<StackPanel>("PackageList");
        _mcpConfigText = Require<SelectableTextBlock>("McpConfigText");
        _copyMcpConfigButton = Require<Button>("CopyMcpConfigButton");
        _mcpToolsText = Require<TextBlock>("McpToolsText");
        _themeLight = Require<Button>("ThemeLight");
        _themeDark = Require<Button>("ThemeDark");
        _themeSystem = Require<Button>("ThemeSystem");
        _themeByTime = Require<Button>("ThemeByTime");
        _languageBox = Require<ComboBox>("LanguageBox");
        _dataDirectoryText = Require<TextBlock>("DataDirectoryText");
        _aboutVersionText = Require<TextBlock>("AboutVersionText");
        _builtinEnginesTitle = Require<TextBlock>("BuiltinEnginesTitle");
        _builtinEngineList = Require<StackPanel>("BuiltinEngineList");
        _moduleEnginesTitle = Require<TextBlock>("ModuleEnginesTitle");
        _moduleEngineList = Require<StackPanel>("ModuleEngineList");
        _noModuleEnginesText = Require<TextBlock>("NoModuleEnginesText");
        _licenseText = Require<TextBlock>("LicenseText");

        // Язык уже применён на уровне приложения (см. App.OnFrameworkInitializationCompleted).
        // Здесь только выставляем направление текста для арабского и иврита.
        FlowDirection = Localizer.FlowDirection;

        WireEvents();
        PopulateLanguages();

        // Отзывчивая вёрстка: пересчитывается при каждом изменении размера окна.
        SizeChanged += (_, _) => UpdateLayoutMode();
        UpdateLayoutMode();

        // Путь к пользовательским данным. Показываем его, потому что человеку
        // может понадобиться найти или почистить каталог вручную.
        _dataDirectoryText.Text = ConversionService.DataDirectory;

        ApplyTheme(AppSettings.ForceTheme ?? _settings.Theme);

        // Кнопка темы и список языков зависят от выбранного языка,
        // поэтому заполняем их после его применения.
        UpdateThemeButtons();

        ShowPage(_convertPage);
        SetNavActive(_navConvert);
        UpdateEmptyState();

        PopulateModulesPage();
        PopulateAboutPage();

        PopulateMcpCard();
        PopulatePreviewerSettings();

        BuildNativeMenu();

        // Проверка сети идёт в фоне и не задерживает запуск.
        _ = CheckNetworkAsync();

#if !ANDROID && !IOS
        // Режим предпросмотра: показываем нужную страницу, грузим файл и при
        // необходимости раскрываем настройки — чтобы снять состояние без ручных кликов.
        //
        // Только для настольных сборок: на телефоне аргументов командной строки нет,
        // и сам Program в мобильной сборке не компилируется.
        if (Program.PreviewPage is { } previewPage)
        {
            ShowPage(previewPage.ToLowerInvariant() switch
            {
                "modules" => _modulesPage,
                "settings" => _settingsPage,
                "about" => _aboutPage,
                _ => _convertPage,
            });
        }

        if (Program.PreviewFiles is { Length: > 0 } previewFiles)
        {
            AddFiles(previewFiles.Where(File.Exists));
        }
#endif
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"XAML element x:Name=\"{name}\" is missing. This is a build error, not a user error.");

    /// <summary>
    /// Собирает меню в системной строке macOS.
    ///
    /// Без него система показывает один пункт с именем приложения и парой
    /// служебных команд: ни открыть файл, ни переключить страницу с клавиатуры
    /// нельзя. Пункты только повторяют то, что уже есть на экране, — новых
    /// возможностей меню не добавляет, это второй способ добраться до старых.
    /// </summary>
    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();

        // Первый пункт macOS называет именем приложения, поэтому здесь только
        // команды, которые по системной привычке живут именно в нём.
        var app = new NativeMenuItem("NekoConverter") { Menu = new NativeMenu() };
        app.Menu!.Add(Item("menu.about_app", () => ShowPage(_aboutPage)));
        app.Menu.Add(new NativeMenuItemSeparator());
        app.Menu.Add(Item("menu.settings", () => ShowPage(_settingsPage), Key.OemComma, KeyModifiers.Meta));
        app.Menu.Add(new NativeMenuItemSeparator());
        app.Menu.Add(Item("menu.quit", QuitApplication, Key.Q, KeyModifiers.Meta));
        menu.Add(app);

        var file = new NativeMenuItem(Localizer.Get("menu.file")) { Menu = new NativeMenu() };
        file.Menu!.Add(Item("menu.add_files", () => OnPickFile(null, new RoutedEventArgs()), Key.O, KeyModifiers.Meta));
        file.Menu.Add(Item("input.paste", () => OnPasteImage(null, new RoutedEventArgs())));
        file.Menu.Add(new NativeMenuItemSeparator());
        file.Menu.Add(Item("menu.output_folder", () => OnPickOutputFolder(null, new RoutedEventArgs())));
        file.Menu.Add(new NativeMenuItemSeparator());
        file.Menu.Add(Item("menu.close_window", Close, Key.W, KeyModifiers.Meta));
        menu.Add(file);

        var edit = new NativeMenuItem(Localizer.Get("menu.edit")) { Menu = new NativeMenu() };
        edit.Menu!.Add(Item("menu.undo", () => WithFocusedTextBox(b => b.Undo()), Key.Z, KeyModifiers.Meta));
        edit.Menu.Add(Item("menu.redo", () => WithFocusedTextBox(b => b.Redo()), Key.Z, KeyModifiers.Meta | KeyModifiers.Shift));
        edit.Menu.Add(new NativeMenuItemSeparator());
        edit.Menu.Add(Item("menu.cut", () => WithFocusedTextBox(b => b.Cut()), Key.X, KeyModifiers.Meta));
        edit.Menu.Add(Item("menu.copy", () => WithFocusedTextBox(b => b.Copy()), Key.C, KeyModifiers.Meta));
        edit.Menu.Add(Item("menu.paste", () => WithFocusedTextBox(b => b.Paste()), Key.V, KeyModifiers.Meta));
        edit.Menu.Add(Item("menu.select_all", () => WithFocusedTextBox(b => b.SelectAll()), Key.A, KeyModifiers.Meta));
        menu.Add(edit);

        var view = new NativeMenuItem(Localizer.Get("menu.view")) { Menu = new NativeMenu() };
        view.Menu!.Add(Item("nav.convert", () => ShowPage(_convertPage), Key.D1, KeyModifiers.Meta));
        view.Menu.Add(Item("nav.modules", () => ShowPage(_modulesPage), Key.D2, KeyModifiers.Meta));
        view.Menu.Add(Item("nav.settings", () => ShowPage(_settingsPage), Key.D3, KeyModifiers.Meta));
        view.Menu.Add(Item("nav.about", () => ShowPage(_aboutPage), Key.D4, KeyModifiers.Meta));
        view.Menu.Add(new NativeMenuItemSeparator());
        view.Menu.Add(Item("menu.toggle_theme", ToggleTheme, Key.T, KeyModifiers.Meta | KeyModifiers.Shift));
        menu.Add(view);

        var run = new NativeMenuItem(Localizer.Get("menu.run")) { Menu = new NativeMenu() };
        run.Menu!.Add(Item("convert.all", () => OnConvertAll(null, new RoutedEventArgs()), Key.Enter, KeyModifiers.Meta));
        menu.Add(run);

        var help = new NativeMenuItem(Localizer.Get("menu.help")) { Menu = new NativeMenu() };
        help.Menu!.Add(Item("menu.repository", () => OpenLink(RepositoryUrl)));
        help.Menu.Add(Item("menu.issues", () => OpenLink($"{RepositoryUrl}/issues")));
        menu.Add(help);

        NativeMenu.SetMenu(this, menu);
    }

    private const string RepositoryUrl = "https://github.com/elenaandreevasvinolup-alt/NekoConverter";

    private static NativeMenuItem Item(string key, Action action, Key? gesture = null, KeyModifiers modifiers = KeyModifiers.None)
    {
        var item = new NativeMenuItem(Localizer.Get(key)) { Command = new MenuCommand(action) };

        if (gesture is { } key_)
        {
            item.Gesture = new KeyGesture(key_, modifiers);
        }

        return item;
    }

    private void QuitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Правка работает с тем полем, куда сейчас смотрит курсор.
    ///
    /// В системном меню некому подставить адресата, поэтому его ищем сами:
    /// без этого «Копировать» оставалось бы серым навсегда.
    /// </summary>
    private void WithFocusedTextBox(Action<TextBox> action)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box)
        {
            action(box);
        }
    }

    private void ToggleTheme() =>
        SetTheme(AppSettings.IsEffectivelyDark(_settings.Theme) ? ThemeMode.Light : ThemeMode.Dark);

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Открыть ссылку может не получиться (нет браузера по умолчанию,
            // запрещено политикой) — это не повод ронять приложение.
        }
    }

    /// <summary>Пункт меню — это команда: без неё нажимать нечего.</summary>
    private sealed class MenuCommand(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }

    private void WireEvents()
    {
        _navConvert.Click += (_, _) => ShowPage(_convertPage);
        _navModules.Click += (_, _) => ShowPage(_modulesPage);
        _navSettings.Click += (_, _) => ShowPage(_settingsPage);
        _navAbout.Click += (_, _) => ShowPage(_aboutPage);

        _tabConvert.Click += (_, _) => ShowPage(_convertPage);
        _tabModules.Click += (_, _) => ShowPage(_modulesPage);
        _tabSettings.Click += (_, _) => ShowPage(_settingsPage);
        _tabAbout.Click += (_, _) => ShowPage(_aboutPage);

        _pickFileButton.Click += OnPickFile;
        _addFilesButton.Click += OnPickFile;
        _clearFilesButton.Click += OnClearFiles;
        _addFromInputButton.Click += OnAddFromInput;
        _pasteImageButton.Click += OnPasteImage;
        _convertAllButton.Click += OnConvertAll;
        _pickOutputFolderButton.Click += OnPickOutputFolder;

        _useCustomOutput.IsCheckedChanged += (_, _) =>
        {
            _customOutputPanel.IsVisible = _useCustomOutput.IsChecked == true;

            if (_customOutputPanel.IsVisible && _outputFolderPath is null)
            {
                _outputFolderText.Text = Localizer.Get("output.hint");
            }
        };

        _themeLight.Click += (_, _) => SetTheme(ThemeMode.Light);
        _themeDark.Click += (_, _) => SetTheme(ThemeMode.Dark);
        _themeSystem.Click += (_, _) => SetTheme(ThemeMode.System);
        _themeByTime.Click += (_, _) => SetTheme(ThemeMode.ByTime);
        _themeQuickToggle.Click += (_, _) =>
            SetTheme(AppSettings.IsEffectivelyDark(_settings.Theme) ? ThemeMode.Light : ThemeMode.Dark);

        // Приём файлов перетаскиванием — только там, где оно вообще есть.
        // На Android и iOS такого способа ввода нет, и обработчики там бесполезны.
        if (CanDropFiles)
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropActive(false));
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        _timeThemeTimer.Tick += (_, _) =>
        {
            if (_settings.Theme == ThemeMode.ByTime)
            {
                ApplyTheme(ThemeMode.ByTime);
                UpdateThemeButtons();
            }
        };
        _timeThemeTimer.Start();
    }

    /// <summary>
    /// Список языков строится по тому, что найдено на диске, а не по списку в коде.
    /// Показываем родные названия: человек ищет свой язык по тому, как он выглядит,
    /// а не по коду вида «zh-Hans».
    /// </summary>
    private void PopulateLanguages()
    {
        _languageBox.ItemsSource = Localizer.Languages.Select(l => l.NativeName).ToList();

        var index = Localizer.Languages
            .Select((l, i) => (l, i))
            .FirstOrDefault(x => x.l.Code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase))
            .i;

        _languageBox.SelectedIndex = index >= 0 && index < Localizer.Languages.Count ? index : 0;
        _languageBox.SelectionChanged += (_, _) => OnLanguageChanged();
    }

    private void OnLanguageChanged()
    {
        var index = _languageBox.SelectedIndex;
        if (index < 0 || index >= Localizer.Languages.Count)
        {
            return;
        }

        var code = Localizer.Languages[index].Code;
        if (code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Language = code;
        _settings.Save();

        Localizer.Apply(code);

        // Направление текста для арабского и иврита.
        FlowDirection = Localizer.FlowDirection;

        // Строки, которые выставляются из кода, обновляем вручную:
        // привязки DynamicResource обновились сами, а эти — нет.
        UpdateThemeButtons();
        UpdateLayoutMode();

        if (_files.Count == 0)
        {
            UpdateEmptyState();
        }
        else
        {
                SetStatus(Localizer.Get("status.file_selected"));
        }

        PopulateModulesPage();
        PopulateAboutPage();

        PopulateMcpCard();
        PopulatePreviewerSettings();

        // Проверка сети идёт в фоне и не задерживает запуск.
        _ = CheckNetworkAsync();
    }

    private void ShowPage(ScrollViewer page)
    {
        _convertPage.IsVisible = ReferenceEquals(page, _convertPage);
        _modulesPage.IsVisible = ReferenceEquals(page, _modulesPage);
        _settingsPage.IsVisible = ReferenceEquals(page, _settingsPage);
        _aboutPage.IsVisible = ReferenceEquals(page, _aboutPage);

        SetNavActive(ReferenceEquals(page, _convertPage) ? _navConvert
            : ReferenceEquals(page, _modulesPage) ? _navModules
            : ReferenceEquals(page, _settingsPage) ? _navSettings
            : _navAbout);
    }

    private void SetNavActive(Button active)
    {
        foreach (var button in new[] { _navConvert, _navModules, _navSettings, _navAbout })
        {
            button.Classes.Set("active", ReferenceEquals(button, active));
        }

        var tab = ReferenceEquals(active, _navConvert) ? _tabConvert
            : ReferenceEquals(active, _navModules) ? _tabModules
            : ReferenceEquals(active, _navSettings) ? _tabSettings
            : _tabAbout;

        foreach (var button in new[] { _tabConvert, _tabModules, _tabSettings, _tabAbout })
        {
            button.Classes.Set("active", ReferenceEquals(button, tab));
        }
    }

    /// <summary>Расставляет галочки предпросмотрщиков и перерисовывает превью при изменении.</summary>
    private void PopulatePreviewerSettings()
    {
        _previewThumbnail.IsChecked = _settings.Previewers.HasFlag(Previewers.Thumbnail);
        _previewText.IsChecked = _settings.Previewers.HasFlag(Previewers.Text);
        _previewAudio.IsChecked = _settings.Previewers.HasFlag(Previewers.Audio);
        _previewMedia.IsChecked = _settings.Previewers.HasFlag(Previewers.Media);
        _previewMesh.IsChecked = _settings.Previewers.HasFlag(Previewers.Mesh);

        foreach (var box in new[] { _previewThumbnail, _previewText, _previewAudio, _previewMedia, _previewMesh })
        {
            box.IsCheckedChanged += (_, _) =>
            {
                _settings.Previewers =
                    (_previewThumbnail.IsChecked == true ? Previewers.Thumbnail : Previewers.None) |
                    (_previewText.IsChecked == true ? Previewers.Text : Previewers.None) |
                    (_previewAudio.IsChecked == true ? Previewers.Audio : Previewers.None) |
                    (_previewMedia.IsChecked == true ? Previewers.Media : Previewers.None) |
                    (_previewMesh.IsChecked == true ? Previewers.Mesh : Previewers.None);

                _settings.Save();

                // Пересчитываем превью сразу: иначе непонятно, что изменилось.
                if (_files.Count > 0)
                {
                    UpdatePreview();
                }
            };
        }
    }

    /// <summary>
    /// Собирает подпись предпросмотра на языке интерфейса.
    ///
    /// Ядро отдаёт только данные, формулировки — здесь: иначе в китайском
    /// интерфейсе появлялись бы русские слова.
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

    /// <summary>
    /// Показывает предпросмотр выбранного файла.
    /// Сбой предпросмотра не должен мешать преобразованию, поэтому ошибки гасятся.
    /// </summary>
    private void UpdatePreview()
    {
        // Предпросмотр показываем для первого файла пакета: он даёт представление
        // о том, что вообще загружено, и не требует отдельного выбора.
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
        _previewLinesText.Text = content.Count > 0 ? string.Join('\n', content) : "";

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
    /// Карточка MCP: готовая конфигурация для клиента.
    ///
    /// Путь к исполняемому файлу подставляется настоящий, а не шаблонный:
    /// человеку остаётся скопировать строку целиком, без правки руками.
    /// </summary>
    private void PopulateMcpCard()
    {
        var executable = Environment.ProcessPath ?? "NekoConverter";

        // Экранируем обратные слэши: в Windows путь содержит их, и без экранирования
        // JSON получится невалидным.
        var escaped = executable.Replace("\\", "\\\\", StringComparison.Ordinal);

        _mcpConfigText.Text =
            "{\n" +
            "  \"mcpServers\": {\n" +
            "    \"nekoconverter\": {\n" +
            $"      \"command\": \"{escaped}\",\n" +
            "      \"args\": [\"--mcp\"]\n" +
            "    }\n" +
            "  }\n" +
            "}";

        _mcpToolsText.Text = Localizer.Format(
            "mcp.tools", "convert · inspect · list_formats · list_packages");
    }

    private async void OnCopyMcpConfig(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        // В Avalonia 12 у буфера обмена нет простого SetTextAsync:
        // содержимое передаётся объектом передачи данных, в который
        // кладётся элемент с текстом.
        var item = new DataTransferItem();
        item.SetText(_mcpConfigText.Text ?? string.Empty);

        var transfer = new DataTransfer();
        transfer.Add(item);

        try
        {
            await clipboard.SetDataAsync(transfer);
            SetStatus(Localizer.Get("mcp.copy"));
        }
        catch (Exception ex)
        {
            SetStatus($"Clipboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверяет доступность площадок с пакетами и показывает результат индикатором.
    /// Ошибка сети не влияет на встроенные форматы, поэтому здесь нет ни блокировок,
    /// ни модальных окон — только цветной кружок и подпись.
    /// </summary>
    private async Task CheckNetworkAsync()
    {
        SetNetworkState(NetworkProbe.State.Checking, null);

        NetworkProbe.Result result;

        try
        {
            result = await NetworkProbe.CheckAsync(ConversionService.Catalog);
        }
        catch (Exception ex)
        {
            result = new NetworkProbe.Result(NetworkProbe.State.Offline, null, ex.Message);
        }

        // Продолжение может прийти из фонового потока — переключаемся на UI-поток.
        await Dispatcher.UIThread.InvokeAsync(() => SetNetworkState(result.State, result.Detail));
    }

    private void SetNetworkState(NetworkProbe.State state, string? detail)
    {
        // Цвета состояний заданы напрямую, а не взяты из темы: зелёный «можно»
        // и оранжевый «нельзя» означают одно и то же в светлой и тёмной теме,
        // и подстраивать их под оформление было бы вредно.
        var (key, color) = state switch
        {
            NetworkProbe.State.Online => ("network.online", Color.Parse("#2FA84F")),
            NetworkProbe.State.Offline => ("network.offline", Color.Parse("#C8871F")),
            _ => ("network.checking", Color.Parse("#9CA3AF")),
        };

        _networkText.Text = Localizer.Get(key);
        _networkDot.Fill = new SolidColorBrush(color);

        // Подробность полезна при диагностике: видно, какой узел не ответил.
        ToolTip.SetTip(_networkText, detail);
    }

    /// <summary>
    /// Мобильные платформы: перетаскивания файлов там нет как способа ввода,
    /// поэтому подсказку про «перетащите» показывать нельзя — она вводит в заблуждение.
    /// </summary>
    private static bool IsMobilePlatform =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsBrowser();

    /// <summary>
    /// Порог, ниже которого боковое меню заменяется нижней навигацией.
    ///
    /// Решение принимается ТОЛЬКО по ширине, без оглядки на платформу:
    ///   • iPhone портрет (390) — компактно, навигация внизу;
    ///   • iPad портрет (820) и ландшафт (1180) — боковое меню, место есть;
    ///   • десктоп — боковое меню.
    /// Привязка к «мобильности» была бы ошибкой: широкий iPad — это не узкий экран,
    /// и прятать от него боковое меню незачем.
    /// </summary>
    private const double CompactWidthThreshold = 760;

    private bool _compact;

    /// <summary>
    /// Переключает вёрстку между широкой и компактной.
    ///
    /// Это именно переключение видимости одних и тех же элементов, а не две разные
    /// страницы: иначе представления неизбежно разойдутся.
    /// </summary>
    private void UpdateLayoutMode()
    {
        // Пока окно не измерено, ширина равна нулю — считаем вёрстку компактной,
        // чтобы при запуске не мелькало боковое меню.
        var width = Bounds.Width > 0 ? Bounds.Width : 0;

        var compact = width < CompactWidthThreshold;

        if (compact == _compact && width > 0)
        {
            return;
        }

        _compact = compact;

        _sidebarPanel.IsVisible = !compact;
        _bottomNav.IsVisible = compact;

        // На телефоне отступы меньше: место дороже.
        Resources["PageMargin"] = compact
            ? new Thickness(16, 16, 16, 16)
            : new Thickness(28, 24, 28, 28);

        UpdateEmptyState();
    }

    // ЗАМЕЧАНИЕ ДЛЯ МОБИЛЬНЫХ: на устройствах с вырезом или «домашней полосой»
    // содержимому нужны отступы безопасной зоны. В Avalonia это делается через
    // InsetsManager у TopLevel и настраивается отдельно под iOS и Android.
    // Здесь этого нет, потому что проверить не на чем: сборка под iOS требует Xcode,
    // а его на этой машине нет.

    // ─────────────────────────── Тема ───────────────────────────

    private void SetTheme(ThemeMode mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        ApplyTheme(mode);
        UpdateThemeButtons();
    }

    private static void ApplyTheme(ThemeMode mode) => AppSettings.ApplyTheme(mode);

    private void UpdateThemeButtons()
    {
        _themeLight.Classes.Set("active", _settings.Theme == ThemeMode.Light);
        _themeDark.Classes.Set("active", _settings.Theme == ThemeMode.Dark);
        _themeSystem.Classes.Set("active", _settings.Theme == ThemeMode.System);
        _themeByTime.Classes.Set("active", _settings.Theme == ThemeMode.ByTime);

        // Кнопка в боковом меню показывает, КУДА переключимся, а не текущее состояние.
        var isDark = AppSettings.IsEffectivelyDark(_settings.Theme);
        _themeQuickToggle.Content = isDark
            ? "☀   " + Localizer.Get("theme.light")
            : "🌙   " + Localizer.Get("theme.dark");
    }

    // ─────────────────────────── Перетаскивание ───────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            SetDropActive(false);
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        SetDropActive(true);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropActive(false);

        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files is null || files.Count == 0)
        {
            return;
        }

        AddFiles([files[0].Path.LocalPath]);

        if (files.Count > 1)
        {
            SetStatus(Localizer.Format("status.multi_drop", files.Count));
        }
    }

    /// <summary>
    /// Подсветка зоны приёма во время перетаскивания.
    /// Цвета переключает стиль по классу active, а не код: иначе пришлось бы
    /// искать кисти вручную и дублировать значения темы.
    /// </summary>
    private void SetDropActive(bool active)
    {
        _dropZoneShape.Classes.Set("active", active);

        if (!CanDropFiles)
        {
            return;
        }

        _dropTitle.Text = Localizer.Get(active ? "drop.release" : "drop.select_or_drop");
    }

    /// <summary>Есть ли на этой платформе перетаскивание файлов.</summary>
    private static bool CanDropFiles => !IsMobilePlatform;

    // ─────────────────────────── Пакетный режим ───────────────────────────

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

        /// <summary>Путь результата. Появляется после успешного преобразования.</summary>
        public string? OutputPath { get; set; }

        /// <summary>Кнопка сравнения: показывается, когда есть что сравнивать.</summary>
        public Button? CompareButton { get; set; }
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

        // Порядок групп задан явно: изображения и звук встречаются чаще,
        // и начинать список с «Data» или «Audio» было бы странно.
        int Rank(FormatKind kind) => kind switch
        {
            FormatKind.Image => 0,
            FormatKind.Audio => 1,
            FormatKind.Video => 2,
            FormatKind.Document => 3,
            FormatKind.Subtitle => 4,
            FormatKind.Data => 5,
            FormatKind.Model3D => 6,
            _ => 7,
        };

        foreach (var kind in _files.Select(f => f.Kind).Distinct().OrderBy(Rank))
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
            : string.Join("\n", _files.Select(f => Path.GetFileName(f.Path)));

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

        // Значение по умолчанию: сначала предпочтительный для категории формат,
        // затем любой доступный, отличающийся от исходного.
        var preferred = PreferredTarget(group.Kind);
        var sourceId = registry.ByPath(group.Files[0].Path)?.Id;

        var defaultChoice =
            choices.FirstOrDefault(c => registry.CanWriteNow(c.Format) &&
                                        string.Equals(c.Format.Id, preferred, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(c => registry.CanWriteNow(c.Format) &&
                                           !string.Equals(c.Format.Id, sourceId, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(c => registry.CanWriteNow(c.Format))
            ?? choices.FirstOrDefault();

        if (defaultChoice is not null)
        {
            group.TargetBox.Text = defaultChoice.Display;
            group.TargetFormatId = defaultChoice.Format.Id;
        }

        // AutoCompleteBox не имеет SelectedIndex: выбранным считается текст,
        // совпавший с одним из вариантов. Поэтому ищем по строке.
        group.TargetBox.SelectionChanged += (_, _) => ApplyTargetChoice(group, choices);

        group.TargetBox.LostFocus += (_, _) => ApplyTargetChoice(group, choices);

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

        UpdateQualityLabel(group);

        return new Border { Classes = { "card" }, Child = content };
    }

    /// <summary>Применяет выбранный в списке формат к группе.</summary>
    private static void ApplyTargetChoice(KindGroup group, List<FormatChoice> choices)
    {
        var text = group.TargetBox.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var match = choices.FirstOrDefault(c =>
            string.Equals(c.Display, text, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return;
        }

        group.TargetFormatId = match.Format.Id;

        // Качество имеет смысл только для форматов с потерями.
        group.QualityRow.IsVisible = IsLossyFormat(match.Format.Id);
    }

    /// <summary>Форматы с потерями: для них показываем выбор качества.</summary>
    private static bool IsLossyFormat(string id) => id is
        "jpeg" or "webp" or "avif" or "heic" or "jxl"
        or "mp3" or "aac" or "ogg" or "opus" or "wma" or "ac3" or "amr"
        or "mp4" or "mkv" or "webm" or "mov" or "avi" or "wmv" or "flv"
        or "mpegps" or "mpegts" or "3gp" or "ogv";

    /// <summary>
    /// Разумный целевой формат по умолчанию для категории.
    ///
    /// Без этого списка выбирался первый по алфавиту доступный формат, и для
    /// изображений им оказывался ICO — «Apple Icon». Пользователь видит
    /// осмысленное предложение, а не случайное.
    /// </summary>
    private static string PreferredTarget(FormatKind kind) => kind switch
    {
        FormatKind.Image => "jpeg",
        FormatKind.Audio => "flac",
        FormatKind.Video => "mp4",
        FormatKind.Subtitle => "srt",
        FormatKind.Data => "json",
        FormatKind.Document => "pdf",
        FormatKind.Model3D => "obj",
        _ => "",
    };

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

    /// <summary>
    /// Строка одного файла: имя, собственный формат, состояние и результат.
    ///
    /// Собственный формат нужен ровно затем, зачем группа и раскрывается:
    /// если один файл из десяти надо преобразовать иначе, незачем менять
    /// настройку всей группы.
    /// </summary>
    private Control BuildItemRow(KindGroup group, LoadedFile file)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };

        var name = new TextBlock
        {
            Text = Path.GetFileName(file.Path),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        // Собственный формат файла: пусто — значит как в группе.
        var registry = ConversionService.Registry;

        var ownChoices = registry.For(group.Kind)
            .Where(f => f.CanWrite && registry.CanWriteNow(f))
            .OrderBy(f => f.Label ?? f.Id, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FormatChoice(f, FormatChoiceText(f, registry)))
            .ToList();

        var ownBox = new AutoCompleteBox
        {
            ItemsSource = ownChoices.Select(c => c.Display).ToList(),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            Width = 200,
            PlaceholderText = Localizer.Get("item.same_as_group"),
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ownBox.SelectionChanged += (_, _) =>
        {
            var match = ownChoices.FirstOrDefault(c =>
                string.Equals(c.Display, ownBox.Text, StringComparison.OrdinalIgnoreCase));

            file.TargetFormatId = match?.Format.Id;
        };

        Grid.SetColumn(ownBox, 1);
        row.Children.Add(ownBox);

        var status = new TextBlock
        {
            Text = Localizer.Get("item.pending"),
            Classes = { "engineBadge" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(status, 2);
        row.Children.Add(status);

        // Кнопка сравнения и текст результата: кнопка появляется только после
        // успешного преобразования, раньше сравнивать нечего.
        var resultPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var details = new TextBlock
        {
            Text = "",
            Classes = { "hint" },
            MaxWidth = 240,
            VerticalAlignment = VerticalAlignment.Center,
        };
        resultPanel.Children.Add(details);

        var compareButton = new Button
        {
            Content = Localizer.Get("compare.open"),
            Classes = { "ghost" },
            FontSize = 11,
            Padding = new Thickness(10, 4),
            IsVisible = false,
        };
        compareButton.Click += (_, _) =>
        {
            if (file.OutputPath is { Length: > 0 } output && File.Exists(output))
            {
                new CompareWindow(file.Path, output).Show();
            }
        };
        resultPanel.Children.Add(compareButton);

        file.CompareButton = compareButton;

        Grid.SetColumn(resultPanel, 3);
        row.Children.Add(resultPanel);

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
            // В Avalonia 12 буфер отдаёт объект передачи, из которого нужное
            // содержимое достаётся методами-расширениями.
            var transfer = await clipboard.TryGetDataAsync();

            if (transfer is not null)
            {
                var paths = new List<string>();

                foreach (var item in transfer.Items)
                {
                    // Сначала файлы: так приходит и картинка, скопированная как файл,
                    // и файл, скопированный в Finder.
                    var fileRaw = await item.TryGetRawAsync(DataFormat.File);

                    switch (fileRaw)
                    {
                        case Avalonia.Platform.Storage.IStorageItem single:
                            paths.Add(single.Path.LocalPath);
                            break;

                        case IEnumerable<Avalonia.Platform.Storage.IStorageItem> many:
                            paths.AddRange(many.Select(i => i.Path.LocalPath));
                            break;
                    }

                    if (paths.Count > 0)
                    {
                        continue;
                    }

                    // Затем растровое изображение: сохраняем во временный PNG
                    // и дальше работаем с ним как с обычным файлом.
                    var bitmapRaw = await item.TryGetRawAsync(DataFormat.Bitmap);

                    if (bitmapRaw is Avalonia.Media.Imaging.Bitmap bitmap)
                    {
                        paths.Add(await SaveClipboardBitmapAsync(bitmap));
                    }
                }

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

    /// <summary>Сохраняет картинку из буфера во временный PNG.</summary>
    private static async Task<string> SaveClipboardBitmapAsync(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nekoconvert-inputs");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"clipboard-{Guid.NewGuid():N}.png");

        await Task.Run(() => bitmap.Save(path, PngBitmapEncoderOptions.Default));

        return path;
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

                if (result.Success && result.OutputPath is { Length: > 0 } output)
                {
                    file.OutputPath = output;

                    if (file.CompareButton is { } button)
                    {
                        button.IsVisible = true;
                    }
                }

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

    /// <summary>
    /// Собирает подпись предпросмотра на языке интерфейса.
    /// Ядро отдаёт только данные, формулировки — здесь.
    /// </summary>


    /// <summary>
    /// Страница модулей. Карточки строятся в коде, а не шаблоном: у каждой свой набор
    /// кнопок и своя реакция на нажатие, и так не нужны ни команды, ни привязки.
    /// </summary>
    private void PopulateModulesPage()
    {
        _packageList.Children.Clear();

        var registry = ConversionService.Registry;
        var manager = ConversionService.Packages;
        var catalog = ConversionService.Catalog;
        var installed = manager.ScanInstalled().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var available = registry.All.Count(registry.IsAvailable);

        _modulesSummaryText.Text = Localizer.Format(
            "modules.summary", registry.All.Count, available, catalog.Packages.Count);

        if (catalog.Packages.Count == 0)
        {
            _packageList.Children.Add(BuildInfoCard(
                Localizer.Get("modules.empty_title"),
                Localizer.Get("modules.empty_body")));
            return;
        }

        foreach (var package in catalog.Packages)
        {
            var id = package.Id ?? "?";
            installed.TryGetValue(id, out var onDisk);
            _packageList.Children.Add(BuildPackageCard(package, onDisk));
        }
    }

    private Border BuildInfoCard(string title, string body)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        return new Border
        {
            Classes = { "card" },
            Child = stack,
        };
    }

    private Border BuildPackageCard(PackageManifest package, InstalledPackage? onDisk)
    {
        var id = package.Id ?? "?";
        var isDependency = package.Kind == PackageKind.Dependency;

        var content = new StackPanel { Spacing = 8 };

        // ── Заголовок: название, версия, метка типа ──
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock
        {
            Text = package.DisplayName(),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });

        var subtitle = $"{package.Version}";
        if (package.SizeBytes > 0)
        {
            subtitle += $" · {package.SizeBytes / 1048576.0:F1} MB";
        }

        if (isDependency)
        {
            subtitle += " · " + Localizer.Get(
                onDisk?.Source == PackageSource.Bundled
                    ? "pkg.dep_label_offline"
                    : "pkg.dep_label");
        }

        titleStack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Opacity = 0.7,
        });

        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        // ── Кнопка действия ──
        var action = new Button
        {
            Classes = { "primary" },
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (onDisk is not null)
        {
            var bundled = onDisk.Source == PackageSource.Bundled;

            action.Content = Localizer.Get(
                bundled ? "pkg.bundled" : isDependency ? "pkg.installed" : "pkg.uninstall");
            action.IsEnabled = !isDependency && !bundled;

            if (isDependency || bundled)
            {
                action.Classes.Set("primary", false);
                action.Classes.Add("ghost");
            }

            if (!bundled)
            {
                action.Click += async (_, _) => await UninstallPackageAsync(package);
            }
        }
        else
        {
            action.Content = Localizer.Get(
                package.Requires.Count > 0 ? "pkg.install_with_deps" : "pkg.install");
            action.Click += async (_, _) => await InstallPackageAsync(package);
        }

        Grid.SetColumn(action, 1);
        header.Children.Add(action);
        content.Children.Add(header);

        // ── Описание ──
        if (package.Description.TryGetValue("zh-Hans", out var description) && description.Length > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
        }

        // ── Что даёт ──
        if (package.ProvidesFormats.Count > 0)
        {
            var list = string.Join(", ", package.ProvidesFormats.Take(18)) +
                       (package.ProvidesFormats.Count > 18 ? "…" : "");

            content.Children.Add(new TextBlock
            {
                Text = Localizer.Format("pkg.unlocks", package.ProvidesFormats.Count, list),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            });
        }

        // ── Лицензия и источник: обязательны для стороннего ПО ──
        var legal = new List<string>();
        if (package.License is { Length: > 0 })
        {
            legal.Add(Localizer.Format("pkg.license", package.License));
        }

        if (package.SourceUrl is { Length: > 0 })
        {
            legal.Add(Localizer.Format("pkg.source", package.SourceUrl));
        }

        if (onDisk is not null)
        {
            var size = $"{onDisk.SizeBytes / 1048576.0:F1} MB";
            legal.Add(Localizer.Format(
                onDisk.Source == PackageSource.Bundled ? "pkg.offline_size" : "pkg.disk", size));
        }

        if (legal.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = string.Join("　·　", legal),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.55,
            });
        }

        return new Border
        {
            Classes = { "card" },
            Child = content,
        };
    }

    private async Task InstallPackageAsync(PackageManifest package)
    {
        SetBusy(true);
        SetStatus(Localizer.Format("status.converting", package.DisplayName(), ""));

        var progress = new Progress<InstallProgress>(p =>
        {
            var name = package.DisplayName();

            var text = p.Stage switch
            {
                InstallStage.Resolving => Localizer.Get("install.resolving"),

                InstallStage.Downloading when p.TotalBytes > 0 => Localizer.Format(
                    "install.downloading", name,
                    (p.BytesReceived / 1048576.0).ToString("F1"),
                    (p.TotalBytes / 1048576.0).ToString("F1"),
                    p.Fraction.ToString("P0")),

                InstallStage.Downloading => Localizer.Format(
                    "install.downloading_unknown", name,
                    (p.BytesReceived / 1048576.0).ToString("F1")),

                InstallStage.Verifying => Localizer.Get("install.verifying"),
                InstallStage.Extracting => Localizer.Get("install.extracting"),
                InstallStage.Finishing => Localizer.Get("install.finishing"),
                _ => "",
            };

            if (text.Length > 0)
            {
                SetStatus(text);
            }
        });

        try
        {
            await ConversionService.Packages.InstallAsync(
                package, ConversionService.Catalog, progress);

            // Таблицу возможностей нужно пересобрать: иначе новые форматы
            // не появятся до перезапуска приложения.
            ConversionService.ReloadRegistry();

            var registry = ConversionService.Registry;
            SetStatus(Localizer.Format(
                "install.done", package.DisplayName(), registry.All.Count(registry.IsAvailable)));
            _statusDetailText.Text = Localizer.Get("install.done_hint");
        }
        catch (Exception ex)
        {
            SetStatus(Localizer.Format("install.failed", ex.Message));
            _statusDetailText.Text = Localizer.Get("install.failed_hint");
        }
        finally
        {
            SetBusy(false);
            PopulateModulesPage();
            RefreshTargetsAfterPackageChange();
        }
    }

    private async Task UninstallPackageAsync(PackageManifest package)
    {
        SetBusy(true);

        try
        {
            await Task.Run(() => ConversionService.Packages.Uninstall(package.Id!));
            ConversionService.ReloadRegistry();

            SetStatus(Localizer.Format("install.uninstalled", package.DisplayName()));
        }
        catch (Exception ex)
        {
            SetStatus(Localizer.Format("install.uninstall_failed", ex.Message));
        }
        finally
        {
            SetBusy(false);
            PopulateModulesPage();
            RefreshTargetsAfterPackageChange();
        }
    }

    /// <summary>
    /// После изменения набора пакетов перестраиваем список целевых форматов,
    /// сохраняя текущий выбор, если он ещё доступен.
    /// </summary>
    private void RefreshTargetsAfterPackageChange()
    {
        // После установки или удаления пакета меняется доступность форматов,
        // поэтому карточки групп перестраиваются заново.
        if (_files.Count > 0)
        {
            RebuildGroups();
        }
    }

    // ─────────────────────────── Страница «关于» ───────────────────────────

    /// <summary>
    /// Страница «О программе».
    ///
    /// Список движков строится из таблицы форматов, а не из отдельного перечня в коде.
    /// Иначе он неизбежно разойдётся с реальностью: добавили движок или формат —
    /// а на странице об этом ничего.
    /// </summary>
    private void PopulateAboutPage()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

        _aboutVersionText.Text = Localizer.Format(
            "about.version", version, Environment.OSVersion.Version.ToString(), Environment.Version.ToString());

        var registry = ConversionService.Registry;

        // Собираем, какие движки вообще упоминаются в таблице и сколько форматов
        // приходится на каждый.
        var engines = registry.All
            .SelectMany(f => f.ReadEngines
                .Concat(f.WriteEngines)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(engine => (Engine: engine, FormatId: f.Id)))
            .GroupBy(x => x.Engine, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EngineSummary(
                g.Key,
                g.Select(x => x.FormatId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                g.Select(x => x.FormatId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                CapabilityRegistry.IsBuiltinEngine(g.Key)))
            .ToList();

        var builtin = engines.Where(e => e.IsBuiltin).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        var external = engines.Where(e => !e.IsBuiltin).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

        _builtinEnginesTitle.Text = Localizer.Get("about.engines_builtin");
        _moduleEnginesTitle.Text = Localizer.Get("about.engines_module");

        _builtinEngineList.Children.Clear();
        foreach (var engine in builtin)
        {
            _builtinEngineList.Children.Add(BuildEngineRow(engine));
        }

        _moduleEngineList.Children.Clear();
        foreach (var engine in external)
        {
            _moduleEngineList.Children.Add(BuildEngineRow(engine));
        }

        _noModuleEnginesText.Text = external.Count == 0
            ? Localizer.Get("about.engines_none")
            : "";
    }

    /// <param name="Name">Имя движка.</param>
    /// <param name="FormatCount">Сколько форматов он обслуживает.</param>
    /// <param name="Formats">Список форматов для показа.</param>
    /// <param name="IsBuiltin">Встроен в приложение или приходит с пакетом.</param>
    private sealed record EngineSummary(string Name, int FormatCount, List<string> Formats, bool IsBuiltin);

    /// <summary>
    /// Строка движка: имя, количество форматов и перечень. Длинные перечни
    /// обрезаются — страница «О программе» не место для полного каталога.
    /// </summary>
    private Control BuildEngineRow(EngineSummary engine)
    {
        const int shown = 9;

        var formats = string.Join(" · ", engine.Formats.Take(shown)) +
                      (engine.Formats.Count > shown ? " …" : "");

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 5) };

        var name = new TextBlock
        {
            Text = engine.Name,
            Classes = { "engineName" },
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var list = new TextBlock
        {
            Text = formats,
            Classes = { "engineFormats" },
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(list, 1);
        row.Children.Add(list);

        var badge = new TextBlock
        {
            Text = Localizer.Format("about.engines_count", engine.FormatCount),
            Classes = { "engineBadge" },
        };
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        return row;
    }

    /// <summary>Элемент списка форматов для выпадающего списка с поиском.</summary>
    private sealed record FormatChoice(FormatDescriptor Format, string Display)
    {
        public override string ToString() => Display;
    }
}
