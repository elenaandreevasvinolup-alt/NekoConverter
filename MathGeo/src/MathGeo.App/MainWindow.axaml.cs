using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MathGeo.Core;

namespace MathGeo.App;

/// <summary>
/// 主窗口。三层结构：侧栏选题目 → 画布出图 → 底栏控制。
///
/// 交互只有一条规则：**在图上直接拖**。
/// 三维场景拖 = 转视角；动点场景拖 = 擦洗。不依赖滚轮、右键、快捷键 ——
/// 希沃一体机是触摸屏，没有鼠标，那些都用不上。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>动画帧数。36 帧足够顺，再多只是白算。</summary>
    private const int FrameCount = 36;

    private readonly DispatcherTimer _timer;

    private Scene? _scene;
    private ProblemFile? _problem;
    private string _kind = "static";
    private int _frameCount = 1;
    private int _frame;
    private bool _playing;
    private Bounds2? _viewBox;
    private Theme _theme = MathGeo.Core.Theme.Textbook;
    private List<string> _files = [];

    // 拖动状态
    private bool _dragging;
    private Point _dragStart;
    private int _dragStartFrame;
    private Camera _dragStartCamera = Camera.Textbook;

    private Localizer _text = Localizer.Current;

    // 显示设置：只影响本机观感，不写进题目文件。
    private double _labelScale = 1;
    private double _pointScale = 1;

    // 临时挂在场景上的投影视图对象（点视角立方体中间那个方块时加/摘）。
    private GeoThreeViews? _projection;

    /// <summary>
    /// 实际用于渲染的主题。
    /// 标记大小、点大小是显示设置，不进题目文件，所以在这里叠加到主题上 ——
    /// 内核的主题是这一版配色长什么样，显示设置是这台机器上要多大。
    /// </summary>
    private Theme EffectiveTheme => _theme with
    {
        LabelSize = _theme.LabelSize * _labelScale,
        PointRadius = _theme.PointRadius * _pointScale,
    };

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 24) };
        _timer.Tick += (_, _) => AdvanceFrame();

        WireUp();
        ApplyTheme();
        ApplyLanguage();

        // 首次启动就有东西看：如果可执行文件旁边带着 samples，直接装进去。
        // 老师第一次打开不该面对一块空白画布 —— 那会让人以为软件坏了。
        if (!TryLoadBundledSamples()) ShowPlaceholder();
    }

    private bool TryLoadBundledSamples()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "samples");
        if (!Directory.Exists(folder)) return false;

        LoadFolder(folder);
        return _files.Count > 0;
    }

    // ————————————————————————— 接线 —————————————————————————

    private void WireUp()
    {
        OpenFolderButton.Click += async (_, _) => await OpenFolderAsync();
        ThemeButton.Click += (_, _) => ToggleTheme();
        ExportButton.Click += async (_, _) => await ExportSvgAsync();
        PlayButton.Click += (_, _) => TogglePlay();

        FrameSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_dragging) SetFrame((int)FrameSlider.Value);
        };

        ProblemList.SelectionChanged += (_, _) => OpenSelectedProblem();

        ViewTextbook.Click += (_, _) => SetCamera(Camera.Textbook);
        ViewFront.Click += (_, _) => SetCamera(Camera.Front);
        ViewTop.Click += (_, _) => SetCamera(Camera.Top);
        ViewSide.Click += (_, _) => SetCamera(Camera.Side);
        ViewIsometric.Click += (_, _) => SetCamera(Camera.Isometric);
        ViewThreeViews.Click += (_, _) => ToggleThreeViews();

        // 指针事件挂在 Border 上：SceneView 是自绘控件，套一层有背景的 Border
        // 才能保证空白区域也接得住事件。
        CanvasHost.PointerPressed += OnPointerPressed;
        CanvasHost.PointerMoved += OnPointerMoved;
        CanvasHost.PointerReleased += OnPointerReleased;
        CanvasHost.PointerCaptureLost += (_, _) => _dragging = false;

        // 视角立方体：点面换视角，点中间切三视图。
        ViewCube.ViewRequested += SetCamera;
        ViewCube.ProjectionRequested += ToggleProjectionView;

        // 显示设置。每改一项都重算包围盒 —— 网格开关会改变内容的范围。
        GridCheck.IsCheckedChanged += (_, _) => ApplyDisplaySettings();
        InvertYCheck.IsCheckedChanged += (_, _) => ApplyDisplaySettings();

        LabelSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _labelScale = LabelSizeSlider.Value;
            RefreshDisplay();
        };

        PointSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _pointScale = PointSizeSlider.Value;
            RefreshDisplay();
        };
    }

    /// <summary>网格开关与 Y 轴反转改变的是场景的视图设置，要重算包围盒。</summary>
    private void ApplyDisplaySettings()
    {
        if (_scene is null) return;

        _scene.View = _scene.View with
        {
            ShowGrid = GridCheck.IsChecked ?? true,
            InvertY = InvertYCheck.IsChecked ?? false,
        };

        _viewBox = ComputeViewBox();
        RenderFrame();
    }

    /// <summary>标记大小、点大小只影响主题，不需要重算包围盒。</summary>
    private void RefreshDisplay()
    {
        Canvas.FigureTheme = EffectiveTheme;
        RenderFrame();
    }

    /// <summary>
    /// 切换投影视图 —— 也就是**正投影**（视场角 0）。
    ///
    /// 之前我理解错了：做成了"在旁边画出三视图"。那是另一回事。
    /// 你要的是**当前这个视角**从透视切成正投影：平行线不再收敛，
    /// 图上量出来的长度和实际一致，这才是"投影视图"。
    /// </summary>
    private void ToggleProjectionView()
    {
        if (_scene is null || _kind != "orbit") return;

        _camera = _camera with { Orthographic = !_camera.Orthographic };
        _scene.Camera = _camera;
        _scene.Solve();

        _viewBox = ComputeViewBox();
        RenderFrame();
    }

    /// <summary>
    /// 三视图：把体按正、侧、俯三个方向投影，并排画在立体图旁边。
    /// 和投影视图是两件事，所以放在预设按钮那一排，不占视角立方体的中间。
    /// </summary>
    private void ToggleThreeViews()
    {
        if (_scene is null || _kind != "orbit") return;

        if (_projection is not null)
        {
            _scene.Remove(_projection.Id);
            _projection = null;
        }
        else
        {
            var solid = _scene.Objects.OfType<GeoPolyhedron>().FirstOrDefault();
            if (solid is null) return;

            var positions = solid.VertexIds
                .Select(id => _scene.Point(id)?.Position)
                .Where(p => p is not null)
                .Select(p => p!.Value)
                .ToList();

            if (positions.Count == 0) return;

            var maxX = positions.Max(p => p.X);
            var minY = positions.Min(p => p.Y);
            var height = Math.Max(positions.Max(p => p.Y) - minY, 1);

            _projection = new GeoThreeViews
            {
                Id = "__projection",
                SolidIds = [solid.Id],
                Offset = new Vec2(maxX + height * 1.2, minY - height * 0.2),
            };

            _scene.Add(_projection);
        }

        _viewBox = ComputeViewBox();
        RenderFrame();
    }

    // ————————————————————————— 打开与加载 —————————————————————————

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _text["ui.openFolder"],
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (path is null) return;

        LoadFolder(path);
    }

    private void LoadFolder(string folder)
    {
        _files = [.. Directory.EnumerateFiles(folder, "*.problem.json").Order()];

        ProblemList.ItemsSource = _files.Select(Path.GetFileName).ToList();

        if (_files.Count > 0) ProblemList.SelectedIndex = 0;
        else ShowPlaceholder();
    }

    private void OpenSelectedProblem()
    {
        var index = ProblemList.SelectedIndex;
        if (index < 0 || index >= _files.Count) return;

        LoadProblem(_files[index]);
    }

    private void LoadProblem(string path)
    {
        StopPlayback();

        try
        {
            _problem = ProblemFile.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            ShowPlaceholder(ex.Message);
            return;
        }

        if (_problem is null)
        {
            ShowPlaceholder(_text["ui.parseFailed"]);
            return;
        }

        var result = ProblemMapper.Load(_problem);
        _scene = result.Scene;

        // 内核默认不画网格，外壳默认打开 —— 老师在课堂上需要尺度参照。
        _scene.View = _scene.View with
        {
            ShowGrid = GridCheck.IsChecked ?? true,
            InvertY = InvertYCheck.IsChecked ?? false,
        };

        _camera = _scene.Camera ?? Camera.Textbook;
        _projection = null;
        _kind = DetermineKind(_scene);
        _frameCount = _kind == "static" ? 1 : FrameCount;
        _frame = 0;

        // 包围盒按所有帧的并集算一次并锁住 —— 否则动点一动整张图就跟着缩放抖动，
        // 看起来像软件在抽搐。这一步要遍历所有帧，所以只在换题时做。
        _viewBox = ComputeViewBox();

        ProblemTitle.Text = _problem.Title ?? Path.GetFileName(path);
        ProblemStatement.Text = _problem.Statement ?? string.Empty;
        ShowAnswer(_problem);
        ShowDiagnostics(result.Diagnostics);

        var animated = _kind == "animation";
        FrameSlider.Maximum = Math.Max(_frameCount - 1, 0);
        FrameSlider.IsEnabled = animated;
        PlayButton.IsEnabled = animated;
        PlayButton.IsVisible = animated;
        FrameSlider.IsVisible = animated;
        FrameCounter.IsVisible = animated;
        ViewPresetRow.IsVisible = _kind == "orbit";

        HintText.Text = _kind switch
        {
            "orbit" => _text["ui.drag3d"],
            "animation" => _text["ui.drag2d"],
            _ => _text["ui.static"],
        };

        RenderFrame();
    }

    private static string DetermineKind(Scene scene)
    {
        if (scene.Objects.OfType<GeoPoint>().Any(p => p.Driver is not null)) return "animation";
        if (scene.Objects.OfType<GeoPolyhedron>().Any()) return "orbit";
        return "static";
    }

    /// <summary>遍历所有帧求包围盒并集。算完把场景还原到第 0 帧。</summary>
    private Bounds2? ComputeViewBox()
    {
        if (_scene is null) return null;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        for (var i = 0; i < _frameCount; i++)
        {
            ApplyFrameToScene(i);

            var bounds = Bounds2.Of(_scene.BuildDisplayList(EffectiveTheme));
            if (!double.IsFinite(bounds.MinX)) continue;

            minX = Math.Min(minX, bounds.MinX);
            minY = Math.Min(minY, bounds.MinY);
            maxX = Math.Max(maxX, bounds.MaxX);
            maxY = Math.Max(maxY, bounds.MaxY);
        }

        ApplyFrameToScene(0);

        if (!double.IsFinite(minX)) return null;

        // 留一点余量给顶点标签：标签是按像素偏移画的，不在世界坐标里。
        var margin = Math.Max(maxX - minX, maxY - minY) * 0.08 + 1e-6;
        return new Bounds2(minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    // ————————————————————————— 帧与重绘 —————————————————————————

    private void ApplyFrameToScene(int frame)
    {
        if (_scene is null) return;

        switch (_kind)
        {
            case "animation":
            {
                var t = _frameCount <= 1 ? 0 : (double)frame / _frameCount;

                foreach (var point in _scene.Objects.OfType<GeoPoint>())
                {
                    if (point.Driver is not { } driver) continue;

                    // 往返播放：讲动点问题时几乎总是要的，单程播完会"啪"地跳回起点。
                    var value = driver.PingPong
                        ? t < 0.5
                            ? driver.Min + (driver.Max - driver.Min) * (t * 2)
                            : driver.Max - (driver.Max - driver.Min) * ((t - 0.5) * 2)
                        : driver.Min + (driver.Max - driver.Min) * t;

                    _scene.Drive(point.Id, value);
                }

                break;
            }

            case "orbit":
                _scene.Camera = _camera;
                _scene.Solve();
                break;
        }
    }

    private void RenderFrame()
    {
        if (_scene is null) return;

        ApplyFrameToScene(_frame);

        Canvas.FigureTheme = EffectiveTheme;
        Canvas.SetShapes(_scene.BuildDisplayList(EffectiveTheme), _viewBox);

        // 视角立方体只在三维场景里有意义，二维图没有视角可转。
        ViewCube.IsVisible = _kind == "orbit";
        if (_kind == "orbit") ViewCube.Camera = _camera;

        if (_kind == "animation")
        {
            FrameSlider.Value = _frame;
            FrameCounter.Text = _text.Format("ui.frame", _frame + 1, _frameCount);
        }
    }

    private void SetFrame(int frame)
    {
        _frame = Math.Clamp(frame, 0, Math.Max(_frameCount - 1, 0));
        RenderFrame();
    }

    private void AdvanceFrame()
    {
        if (!_playing) return;
        SetFrame(_frame + 1 >= _frameCount ? 0 : _frame + 1);
    }

    private void TogglePlay()
    {
        if (_frameCount <= 1) return;

        _playing = !_playing;
        PlayButton.Content = _playing ? _text["ui.pause"] : _text["ui.play"];

        if (_playing) _timer.Start();
        else _timer.Stop();
    }

    private void StopPlayback()
    {
        _playing = false;
        _timer.Stop();
        PlayButton.Content = _text["ui.play"];
    }

    // ————————————————————————— 拖动 —————————————————————————

    private Camera _camera = Camera.Textbook;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scene is null) return;

        _dragging = true;
        _dragStart = e.GetPosition(CanvasHost);
        _dragStartFrame = _frame;
        _dragStartCamera = _camera;

        if (_kind == "animation") StopPlayback();

        e.Pointer.Capture(CanvasHost);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _scene is null) return;

        var current = e.GetPosition(CanvasHost);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;

        switch (_kind)
        {
            case "orbit":
            {
                // 拖动 → 转视角。水平转方位角，垂直调仰角（Orbit 内部会夹到 ±89°）。
                _camera = _dragStartCamera.Orbit(dx * 0.6, -dy * 0.4);
                _scene.Camera = _camera;
                _scene.Solve();
                Canvas.SetShapes(_scene.BuildDisplayList(EffectiveTheme), _viewBox);
                break;
            }

            case "animation":
            {
                // 拖动 → 擦洗。横向拖过画布 80% 宽度的距离就扫完整个动画。
                var span = Math.Max(Canvas.Bounds.Width * 0.8, 120);
                SetFrame(_dragStartFrame + (int)Math.Round(dx / span * (_frameCount - 1)));
                break;
            }
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Handled = true;
    }

    private void SetCamera(Camera camera)
    {
        _camera = camera;

        if (_scene is null) return;

        _scene.Camera = camera;
        _scene.Solve();
        _viewBox = ComputeViewBox();
        Canvas.SetShapes(_scene.BuildDisplayList(EffectiveTheme), _viewBox);
    }

    // ————————————————————————— 主题与语言 —————————————————————————

    private void ToggleTheme()
    {
        _theme = ReferenceEquals(_theme, MathGeo.Core.Theme.Dark) ? MathGeo.Core.Theme.Textbook : MathGeo.Core.Theme.Dark;

        ApplyTheme();
        Canvas.FigureTheme = EffectiveTheme;

        if (_scene is not null) Canvas.SetShapes(_scene.BuildDisplayList(EffectiveTheme), _viewBox);
    }

    private void ApplyTheme()
    {
        var dark = ReferenceEquals(_theme, MathGeo.Core.Theme.Dark);

        RequestedThemeVariant = dark
            ? Avalonia.Styling.ThemeVariant.Dark
            : Avalonia.Styling.ThemeVariant.Light;

        ThemeButton.Content = dark ? _text["ui.theme.light"] : _text["ui.theme.dark"];
    }

    private void ApplyLanguage()
    {
        _text = Localizer.Current;

        BrandTitle.Text = _text["app.name"];
        BrandSubtitle.Text = _text["app.tagline"];
        OpenFolderButton.Content = _text["ui.openFolder"];
        ThemeLabel.Text = _text["ui.theme"];
        LanguageLabel.Text = _text["ui.language"];
        ExportButton.Content = _text["ui.export"];
        PlayButton.Content = _playing ? _text["ui.pause"] : _text["ui.play"];
        ViewTextbook.Content = _text["view.textbook"];
        ViewFront.Content = _text["view.front"];
        ViewTop.Content = _text["view.top"];
        ViewSide.Content = _text["view.side"];
        ViewIsometric.Content = _text["view.isometric"];
        ViewThreeViews.Content = _text["view.threeViews"];

        DisplayPanel.Header = _text["ui.display"];
        GridCheck.Content = _text["ui.grid"];
        InvertYCheck.Content = _text["ui.invertY"];
        LabelSizeLabel.Text = _text["ui.labelSize"];
        PointSizeLabel.Text = _text["ui.pointSize"];

        ThemeButton.Content = ReferenceEquals(_theme, MathGeo.Core.Theme.Dark)
            ? _text["ui.theme.light"]
            : _text["ui.theme.dark"];

        if (_scene is null) ShowPlaceholder();

        RefreshLanguageBox();
    }

    /// <summary>
    /// 重填语言下拉框。
    ///
    /// 必须加抑制标志：设置 ItemsSource / SelectedIndex 会触发 SelectionChanged，
    /// 而处理函数里又会调 ApplyLanguage() → 回到这里，形成无限递归、直接栈溢出。
    /// 之前切语言崩溃就是这个原因。
    /// </summary>
    private void RefreshLanguageBox()
    {
        var languages = Localizer.Installed;

        _suppressLanguageChange = true;
        try
        {
            LanguageBox.ItemsSource = languages
                .Select(l => l.Reviewed ? l.Name : $"{l.Name} · {_text["ui.unreviewed"]}")
                .ToList();

            var index = languages.ToList().FindIndex(l => l.Lang == _text.Lang);
            LanguageBox.SelectedIndex = Math.Max(0, index);
        }
        finally
        {
            _suppressLanguageChange = false;
        }
    }

    private bool _suppressLanguageChange;

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;

        var index = LanguageBox.SelectedIndex;
        if (index < 0) return;

        Localizer.Use(Localizer.Installed[index].Lang);

        // 诊断文案是求解时按当时的语言烘进对象里的，切语言后要重解一次。
        if (_files.Count > 0 && ProblemList.SelectedIndex >= 0)
            LoadProblem(_files[ProblemList.SelectedIndex]);
        else
            ApplyLanguage();
    }

    // ————————————————————————— 导出 —————————————————————————

    private async Task ExportSvgAsync()
    {
        if (_scene is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _text["ui.export"],
            SuggestedFileName = "figure.svg",
            DefaultExtension = "svg",
            FileTypeChoices = [new FilePickerFileType("SVG") { Patterns = ["*.svg"] }],
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (path is null) return;

        await File.WriteAllTextAsync(path, _scene.ToSvg(EffectiveTheme));
    }

    // ————————————————————————— 界面状态 —————————————————————————

    private void ShowAnswer(ProblemFile problem)
    {
        var hasValue = !string.IsNullOrWhiteSpace(problem.Answer?.Value);
        var steps = problem.Answer?.Steps ?? [];

        AnswerBox.IsVisible = hasValue || steps.Count > 0;
        AnswerValue.Text = problem.Answer?.Value ?? string.Empty;
        AnswerSteps.ItemsSource = steps;
    }

    private void ShowDiagnostics(IReadOnlyList<SolveDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count == 0) return;

        // 出图失败不该是"一片空白 + 不知道为什么"。把第一条错误显示出来，
        // 老师至少知道是题目的哪一部分没被识别。
        AnswerBox.IsVisible = true;
        AnswerValue.Text = errors[0].Message;
        AnswerSteps.ItemsSource = errors.Skip(1).Select(d => d.Message).ToList();
    }

    private void ShowPlaceholder(string? message = null)
    {
        _scene = null;
        _problem = null;
        _kind = "static";
        _frameCount = 1;
        _viewBox = null;

        Canvas.SetShapes([]);

        ProblemTitle.Text = _text["app.name"];
        ProblemStatement.Text = message ?? _text["ui.empty"];
        AnswerBox.IsVisible = false;
        ViewCube.IsVisible = false;
        ViewPresetRow.IsVisible = false;
        PlayButton.IsVisible = false;
        FrameSlider.IsVisible = false;
        FrameCounter.IsVisible = false;
        HintText.Text = _text["ui.hintOpenFolder"];
    }
}
