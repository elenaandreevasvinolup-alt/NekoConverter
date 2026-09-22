using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using MathGeo.Core;

namespace MathGeo.App.Controls;

/// <summary>
/// 视角立方体。画布右上角那个小方块，用来一眼看懂"现在从哪个方向看"并一键换视角。
///
/// 三件事：
///   · 外面是镂空的立方体，六个面各有一个轴名（x / −x / y / −y / z / −z）；
///   · 里面是一个小的实心立方体，点它在透视与正投影之间切换；
///   · 正对着某个面时，四条棱外侧出现**扁长方形**，点它们切到隔壁面。
///
/// 最后一条是必须的：正对一个面时，隔壁四个面是**边缘朝向**的（投影成一条线），
/// 根本没有面积可点。没有这条，从正视图就再也回不到别的视角了 —— 死路一条。
///
/// 选中定义刻意做成"悬停**或**按下"，而且按下后还能移动：
/// 触摸屏上手指按下去会盖住目标，按错了还能移到正确的位置再松手。
/// </summary>
public sealed class ViewCube : Control, ICustomHitTest
{
    /// <summary>立方体的半边长（局部坐标）。</summary>
    private const double CubeHalf = 0.5;

    /// <summary>内部实心立方体的缩放。</summary>
    private const double InnerScale = 0.32;

    /// <summary>棱外侧那条扁长方形的厚度（局部坐标）。</summary>
    private const double EdgeStripThickness = 0.12;

    /// <summary>
    /// 一个面要"朝向相机"到什么程度才算可见（约 8.6°）。
    ///
    /// 不能只判 dot > 0：正对一个面时，隔壁面的 dot 是 6.1e-16 而不是 0，
    /// 于是边缘面会被当成正面，它的标签会被画到立方体外面去。
    /// </summary>
    private const double MinFacing = 0.15;

    private const double Padding = 14;

    private static readonly Vec3[] Vertices =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
    ];

    /// <summary>六个面：绕向从体外看逆时针，于是叉积朝外。</summary>
    private static readonly (Vec3 Normal, string Label, int[] Indices)[] Faces =
    [
        (new Vec3(1, 0, 0), "x", [5, 1, 2, 6]),
        (new Vec3(-1, 0, 0), "−x", [0, 4, 7, 3]),
        (new Vec3(0, 1, 0), "y", [7, 6, 2, 3]),
        (new Vec3(0, -1, 0), "−y", [0, 1, 5, 4]),
        (new Vec3(0, 0, 1), "z", [4, 5, 6, 7]),
        (new Vec3(0, 0, -1), "−z", [1, 0, 3, 2]),
    ];

    /// <summary>12 条棱，以及每条棱两侧的面。预计算一次。</summary>
    private static readonly (int A, int B, int FaceA, int FaceB)[] Edges = BuildEdges();

    private static (int A, int B, int FaceA, int FaceB)[] BuildEdges()
    {
        var edges = new List<(int, int, int, int)>();

        for (var i = 0; i < Faces.Length; i++)
            for (var j = i + 1; j < Faces.Length; j++)
            {
                var shared = Faces[i].Indices.Intersect(Faces[j].Indices).ToArray();
                if (shared.Length == 2) edges.Add((shared[0], shared[1], i, j));
            }

        return [.. edges];
    }

    private Camera _camera = Camera.Textbook;

    // 当前高亮的目标：面（法向）或棱条（点击后切到 stripNormal 那个面）
    private Vec3? _highlightFace;
    private Vec3? _highlightStrip;
    private bool _highlightCenter;
    private bool _pressed;

    public ViewCube()
    {
        Width = 124;
        Height = 124;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event Action<Camera>? ViewRequested;

    /// <summary>点中间的实心立方体时请求在透视 / 正投影之间切换。</summary>
    public event Action? ProjectionRequested;

    public Camera Camera
    {
        get => _camera;
        set
        {
            if (_camera == value) return;
            _camera = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 命中判定。
    ///
    /// **必须自己检查边界。** 实现了 ICustomHitTest 之后，Avalonia 用这个方法
    /// *替代*默认的边界检查 —— 直接 return true 会让这个部件吃掉整个窗口的指针事件，
    /// 画布就再也收不到拖动，光标也会一直停在手指图标上。
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    // ————————————————————————— 绘制 —————————————————————————

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 8 || size.Height < 8) return;

        var line = Resolve("TextTertiary", "#9CA3AF");
        var accent = Resolve("Accent", "#5B6BE8");
        var border = Resolve("CardBorder", "#E6E8EF");

        // 半透明底 + 描边：图形很花的时候没有底就完全读不出立方体在哪。
        context.DrawRectangle(
            new SolidColorBrush(ColorOf("CardBackground", "#FFFFFF"), 0.9),
            new Pen(border, 1),
            new RoundedRect(new Rect(size), 10));

        var view = MiniView();

        var outer = new Vec2[Vertices.Length];
        for (var i = 0; i < Vertices.Length; i++)
            outer[i] = view.Project(Vertices[i] * CubeHalf);

        var scale = FitScale(outer, size);
        Point ToScreen(Vec2 point) => new(
            size.Width / 2 + point.X * scale,
            size.Height / 2 - point.Y * scale);

        DrawInnerCube(context, view, ToScreen, scale, accent, line);
        DrawOuterWireframe(context, outer, ToScreen, line);
        DrawEdgeStrips(context, view, outer, ToScreen, accent);
        DrawFaceLabels(context, view, outer, ToScreen, line, accent);
    }

    private CameraView MiniView() => new Camera
    {
        AzimuthDeg = _camera.AzimuthDeg,
        ElevationDeg = _camera.ElevationDeg,
        Orthographic = true,
        Distance = 10,
    }.CreateView();

    private static double FitScale(Vec2[] projected, Size size)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach (var point in projected)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var width = Math.Max(maxX - minX, 1e-6);
        var height = Math.Max(maxY - minY, 1e-6);
        var usable = Math.Max(Math.Min(size.Width, size.Height) - Padding * 2, 1);

        return usable / Math.Max(width, height);
    }

    /// <summary>镂空的外框：只画棱，于是里面的实心立方体透过来看得见。</summary>
    private static void DrawOuterWireframe(
        DrawingContext context, Vec2[] projected, Func<Vec2, Point> toScreen, IBrush line)
    {
        var pen = new Pen(line, 1.2);

        foreach (var (_, _, indices) in Faces)
            for (var i = 0; i < indices.Length; i++)
                context.DrawLine(
                    pen,
                    toScreen(projected[indices[i]]),
                    toScreen(projected[indices[(i + 1) % indices.Length]]));
    }

    /// <summary>
    /// 棱外侧的扁长方形。
    ///
    /// 只在"这个面可见、隔壁面不可见"时出现 —— 也就是正对着某个面的时候。
    /// 那四个隔壁面此时是边缘朝向的，投影成一条线，没有面积可点；
    /// 这四个长方形就是它们唯一的入口。
    /// </summary>
    private void DrawEdgeStrips(
        DrawingContext context, CameraView view, Vec2[] projected,
        Func<Vec2, Point> toScreen, IBrush accent)
    {
        foreach (var (a, b, faceA, faceB) in Edges)
        {
            var visibleA = IsFacing(view, faceA);
            var visibleB = IsFacing(view, faceB);
            if (visibleA == visibleB) continue;   // 两个都可见或都不可见，不需要棱条

            var visibleFace = visibleA ? faceA : faceB;
            var targetFace = visibleA ? faceB : faceA;

            var quad = StripQuad(projected, Faces[visibleFace].Indices, a, b);
            var highlighted = _highlightStrip is { } s
                              && Vec3.Dot(s, Faces[targetFace].Normal) > 0.99;

            var geometry = BuildGeometry(quad, toScreen);

            // 未高亮时几乎只是一层淡淡的底 —— 它在那儿是为了能点，
            // 不是为了抢注意力；高亮时才明显起来。
            context.DrawGeometry(
                new SolidColorBrush(AccentColor(accent), highlighted ? 0.5 : 0.07),
                new Pen(accent, highlighted ? 1.6 : 0.8, new DashStyle([3, 3], 0)),
                geometry);

            // 棱条上写出目标面的名字 —— 不然用户不知道点它去哪儿。
            if (!highlighted) continue;

            var mid = (quad[0] + quad[2]) * 0.5;
            var text = new FormattedText(
                Faces[targetFace].Label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                11,
                accent);

            var at = toScreen(mid);
            context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }
    }

    private void DrawFaceLabels(
        DrawingContext context, CameraView view, Vec2[] projected,
        Func<Vec2, Point> toScreen, IBrush line, IBrush accent)
    {
        for (var index = 0; index < Faces.Length; index++)
        {
            if (!IsFacing(view, index)) continue;

            var (normal, label, indices) = Faces[index];
            var highlighted = _highlightFace is { } h && Vec3.Dot(h, normal) > 0.99;

            if (highlighted)
                context.DrawGeometry(
                    new SolidColorBrush(AccentColor(accent), 0.22),
                    new Pen(accent, 1.6),
                    BuildGeometry([.. indices.Select(i => projected[i])], toScreen));

            // 标签从面心往外推一点：面心正好被中间的实心立方体挡住。
            var centroid = Centroid(projected, indices);
            var center = new Vec2(centroid.X * 1.24, centroid.Y * 1.24);

            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
                highlighted ? 15 : 11.5,
                highlighted ? accent : line);

            var at = toScreen(center);
            context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }
    }

    private void DrawInnerCube(
        DrawingContext context, CameraView view, Func<Vec2, Point> toScreen,
        double scale, IBrush accent, IBrush line)
    {
        var projected = new Vec2[Vertices.Length];
        for (var i = 0; i < Vertices.Length; i++)
            projected[i] = view.Project(Vertices[i] * (CubeHalf * InnerScale));

        // 先背面后正面：不排序的话实心小立方体看起来会像个平面六边形。
        foreach (var back in new[] { false, true })
        {
            for (var index = 0; index < Faces.Length; index++)
            {
                if (IsFacing(view, index) == back) continue;

                var brush = back
                    ? new SolidColorBrush(AccentColor(accent), 0.12)
                    : new SolidColorBrush(AccentColor(accent), 0.32);

                context.DrawGeometry(
                    brush, null,
                    BuildGeometry([.. Faces[index].Indices.Select(i => projected[i])], toScreen));
            }
        }

        var pen = new Pen(_highlightCenter ? accent : line, _highlightCenter ? 1.8 : 1);

        foreach (var (_, _, indices) in Faces)
            for (var i = 0; i < indices.Length; i++)
                context.DrawLine(
                    pen,
                    toScreen(projected[indices[i]]),
                    toScreen(projected[indices[(i + 1) % indices.Length]]));
    }

    /// <summary>棱条的四角：把棱沿着"离开可见面中心"的方向外扩一条厚度。</summary>
    private static Vec2[] StripQuad(Vec2[] projected, int[] visibleFace, int a, int b)
    {
        var pa = projected[a];
        var pb = projected[b];
        var faceCenter = Centroid(projected, visibleFace);
        var mid = (pa + pb) * 0.5;

        var outward = (mid - faceCenter).Normalized();
        if (outward.LengthSquared < 0.5) outward = new Vec2(0, 1);

        var offset = outward * EdgeStripThickness;
        return [pa, pb, pb + offset, pa + offset];
    }

    private static bool IsFacing(CameraView view, int faceIndex)
        => Vec3.Dot(Faces[faceIndex].Normal, view.Eye.Normalized()) >= MinFacing;

    private static Vec2 Centroid(Vec2[] projected, int[] indices)
    {
        double x = 0, y = 0;
        foreach (var index in indices)
        {
            x += projected[index].X;
            y += projected[index].Y;
        }

        return new Vec2(x / indices.Length, y / indices.Length);
    }

    private static StreamGeometry BuildGeometry(IReadOnlyList<Vec2> points, Func<Vec2, Point> toScreen)
    {
        var geometry = new StreamGeometry();

        using (var writer = geometry.Open())
        {
            writer.BeginFigure(toScreen(points[0]), isFilled: true);
            for (var i = 1; i < points.Count; i++)
                writer.LineTo(toScreen(points[i]));
            writer.EndFigure(isClosed: true);
        }

        return geometry;
    }

    // ————————————————————————— 命中测试 —————————————————————————

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHighlight(e.GetPosition(this));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        _pressed = true;
        UpdateHighlight(e.GetPosition(this));

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_pressed) return;

        _pressed = false;
        UpdateHighlight(e.GetPosition(this));
        Activate();

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_pressed) return;   // 按下时移出边界不取消，手指还在屏幕上

        _highlightFace = null;
        _highlightStrip = null;
        _highlightCenter = false;
        InvalidateVisual();
    }

    private void UpdateHighlight(Point point)
    {
        var size = Bounds.Size;
        if (size.Width < 8 || size.Height < 8) return;

        var view = MiniView();

        var outer = new Vec2[Vertices.Length];
        for (var i = 0; i < Vertices.Length; i++)
            outer[i] = view.Project(Vertices[i] * CubeHalf);

        var scale = FitScale(outer, size);
        Vec2 ToLocal(Vec2 p) => new(p.X * scale, -p.Y * scale);

        var cursor = new Vec2(point.X - size.Width / 2, -(point.Y - size.Height / 2));

        var local = new Vec2[Vertices.Length];
        for (var i = 0; i < Vertices.Length; i++) local[i] = ToLocal(outer[i]);

        // 优先级：中间实心立方体 → 棱条 → 面。
        // 棱条排在面之前，因为正对时它紧贴可见面的外沿，不先测会被面抢走。
        var inner = new Vec2[Vertices.Length];
        for (var i = 0; i < Vertices.Length; i++)
            inner[i] = ToLocal(view.Project(Vertices[i] * (CubeHalf * InnerScale)));

        var centerHit = false;
        for (var index = 0; index < Faces.Length && !centerHit; index++)
        {
            if (!IsFacing(view, index)) continue;
            centerHit = Contains(inner, Faces[index].Indices, cursor);
        }

        Vec3? stripHit = null;
        Vec3? faceHit = null;

        if (!centerHit)
        {
            foreach (var (a, b, faceA, faceB) in Edges)
            {
                var visibleA = IsFacing(view, faceA);
                var visibleB = IsFacing(view, faceB);
                if (visibleA == visibleB) continue;

                var visibleFace = visibleA ? faceA : faceB;
                var targetFace = visibleA ? faceB : faceA;

                if (!Contains(local, StripQuad(local, Faces[visibleFace].Indices, a, b), cursor))
                    continue;

                stripHit = Faces[targetFace].Normal;
                break;
            }

            if (stripHit is null)
            {
                for (var index = 0; index < Faces.Length; index++)
                {
                    if (!IsFacing(view, index)) continue;
                    if (!Contains(local, Faces[index].Indices, cursor)) continue;

                    faceHit = Faces[index].Normal;
                    break;
                }
            }
        }

        if (_highlightCenter == centerHit
            && Nullable.Equals(_highlightFace, faceHit)
            && Nullable.Equals(_highlightStrip, stripHit))
        {
            return;
        }

        _highlightCenter = centerHit;
        _highlightFace = faceHit;
        _highlightStrip = stripHit;
        InvalidateVisual();
    }

    /// <summary>点在多边形内。</summary>
    private static bool Contains(Vec2[] points, int[] indices, Vec2 test)
        => Contains(points, [.. indices.Select(i => points[i])], test);

    private static bool Contains(Vec2[] points, Vec2[] polygon, Vec2 test)
    {
        var inside = false;

        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];

            if (a.Y > test.Y != b.Y > test.Y
                && test.X < (b.X - a.X) * (test.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private void Activate()
    {
        if (_highlightCenter)
        {
            ProjectionRequested?.Invoke();
            return;
        }

        // 棱条和面走同一条路：都是"切到某个法向对应的视角"。
        var normal = _highlightStrip ?? _highlightFace;
        if (normal is not { } direction) return;

        ViewRequested?.Invoke(new Camera
        {
            AzimuthDeg = MathUtil.RadToDeg(Math.Atan2(direction.Z, direction.X)),
            ElevationDeg = MathUtil.RadToDeg(Math.Asin(MathUtil.Clamp(direction.Y, -1, 1))),
            Distance = _camera.Distance,
            Orthographic = _camera.Orthographic,
        });
    }

    // ————————————————————————— 资源 —————————————————————————

    private IBrush Resolve(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallback));

    private Color ColorOf(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is ISolidColorBrush solid
            ? solid.Color
            : Color.Parse(fallback);

    private static Color AccentColor(IBrush brush)
        => brush is ISolidColorBrush solid ? solid.Color : Colors.MediumSlateBlue;
}
