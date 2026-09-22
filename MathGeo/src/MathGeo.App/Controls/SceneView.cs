using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MathGeo.Core;

namespace MathGeo.App.Controls;

/// <summary>
/// 画布：把内核产出的显示列表画出来。
///
/// 这里是"显示列表是唯一渲染抽象"最直接的兑现 —— 屏幕和 SVG 导出吃的是同一份数据，
/// 所以画布上看到的和导出到 PPT 里的必然一致，不可能出现"导出的图和看到的不一样"。
///
/// 它只负责画，不负责交互。拖拽、播放、滑块都由窗口层处理，这样画布可以单独测试。
/// </summary>
public sealed class SceneView : Control
{
    /// <summary>内容四周留白。和 SVG 导出取一样的值，两种输出的构图才一致。</summary>
    private const double Padding = 24;

    /// <summary>标签离锚点的像素距离。同样与 SVG 导出保持一致。</summary>
    private const double LabelOffset = 11;

    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pen> _pens = [];

    private IReadOnlyList<Shape> _shapes = [];
    private Theme _theme = MathGeo.Core.Theme.Textbook;

    /// <summary>当前场景的包围盒（世界坐标）。窗口层用它算"自适应缩放"。</summary>
    public Bounds2 ContentBounds { get; private set; } = new(0, 0, 1, 1);

    /// <summary>最近一次绘制用的缩放与偏移。窗口层把屏幕坐标换算回世界坐标时要用。</summary>
    public double LastScale { get; private set; } = 1;

    /// <summary>
    /// 图形主题。刻意不叫 Theme —— StyledElement 已经有一个 Theme 属性（控件模板主题），
    /// 同名会让继承体系里到处产生歧义。
    /// </summary>
    public Theme FigureTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            _pens.Clear();
            InvalidateVisual();
        }
    }

    public void SetShapes(IReadOnlyList<Shape> shapes, Bounds2? viewBox = null)
    {
        _shapes = shapes;

        // viewBox 由调用方锁住（比如一整段动画的并集包围盒）。
        // 不锁的话，动点一动整张图就跟着缩放，看起来像在抽搐。
        ContentBounds = viewBox ?? (shapes.Count == 0 ? new Bounds2(0, 0, 1, 1) : Bounds2.Of(shapes));

        InvalidateVisual();
    }

    /// <summary>屏幕坐标 → 世界坐标。拖动时把鼠标位置翻译成模型坐标要用它。</summary>
    public Vec2 ToWorld(Point screen, Size size)
    {
        if (LastScale <= 0) return Vec2.Zero;

        var usableWidth = Math.Max(size.Width - Padding * 2, 1);
        var usableHeight = Math.Max(size.Height - Padding * 2, 1);
        var offsetX = Padding + (usableWidth - ContentBounds.Width * LastScale) / 2;
        var offsetY = Padding + (usableHeight - ContentBounds.Height * LastScale) / 2;

        return new Vec2(
            ContentBounds.MinX + (screen.X - offsetX) / LastScale,
            ContentBounds.MaxY - (screen.Y - offsetY) / LastScale);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return;

        context.FillRectangle(Brush(_theme.Background), new Rect(size));

        if (_shapes.Count == 0) return;

        var bounds = ContentBounds;
        if (!double.IsFinite(bounds.MinX)) return;

        var usableWidth = Math.Max(size.Width - Padding * 2, 1);
        var usableHeight = Math.Max(size.Height - Padding * 2, 1);

        // 等比缩放，长边贴合。宽或高退化成零时（比如一条竖线）用另一边兜底。
        var scale = Math.Min(
            usableWidth / Math.Max(bounds.Width, 1e-6),
            usableHeight / Math.Max(bounds.Height, 1e-6));
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        LastScale = scale;

        var offsetX = Padding + (usableWidth - bounds.Width * scale) / 2;
        var offsetY = Padding + (usableHeight - bounds.Height * scale) / 2;

        Point Project(Vec2 point) => new(
            offsetX + (point.X - bounds.MinX) * scale,
            offsetY + (bounds.MaxY - point.Y) * scale);

        foreach (var shape in _shapes)
            DrawShape(context, shape, Project, scale);
    }

    private void DrawShape(DrawingContext context, Shape shape, Func<Vec2, Point> project, double scale)
    {
        switch (shape)
        {
            case LineShape line:
                context.DrawLine(
                    PenFor(line.Stroke, line.Width, line.Dash),
                    project(line.A),
                    project(line.B));
                break;

            case PolygonShape polygon:
                context.DrawGeometry(
                    polygon.Fill is null ? null : Brush(polygon.Fill),
                    polygon.Stroke is null ? null : PenFor(polygon.Stroke, polygon.StrokeWidth, null),
                    BuildPolygon(polygon.Points, project));
                break;

            case CircleShape circle:
                context.DrawEllipse(
                    circle.Fill is null ? null : Brush(circle.Fill),
                    circle.Stroke is null ? null : PenFor(circle.Stroke, circle.StrokeWidth, null),
                    project(circle.Center),
                    Math.Abs(circle.Radius) * scale,
                    Math.Abs(circle.Radius) * scale);
                break;

            case DotShape dot:
                context.DrawEllipse(Brush(dot.Fill), null, project(dot.At), dot.Radius, dot.Radius);
                break;

            case ArcShape arc:
                context.DrawGeometry(
                    null,
                    PenFor(arc.Stroke, arc.Width, null),
                    BuildArc(arc, project, scale));
                break;

            case LabelShape label:
                DrawLabel(context, label, project);
                break;
        }
    }

    private static StreamGeometry BuildPolygon(IReadOnlyList<Vec2> points, Func<Vec2, Point> project)
    {
        var geometry = new StreamGeometry();

        using (var writer = geometry.Open())
        {
            writer.BeginFigure(project(points[0]), isFilled: true);
            for (var i = 1; i < points.Count; i++)
                writer.LineTo(project(points[i]));
            writer.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildArc(ArcShape arc, Func<Vec2, Point> project, double scale)
    {
        var start = new Vec2(
            arc.Center.X + arc.Radius * Math.Cos(arc.StartRad),
            arc.Center.Y + arc.Radius * Math.Sin(arc.StartRad));

        var endAngle = arc.StartRad + arc.SweepRad;
        var end = new Vec2(
            arc.Center.X + arc.Radius * Math.Cos(endAngle),
            arc.Center.Y + arc.Radius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();

        using (var writer = geometry.Open())
        {
            writer.BeginFigure(project(start), isFilled: false);

            // 世界坐标 Y 向上、逆时针为正；屏幕 Y 向下。
            // 于是世界里的逆时针在屏幕上是"角度递减"，对应 CounterClockwise。
            writer.ArcTo(
                project(end),
                new Size(Math.Abs(arc.Radius) * scale, Math.Abs(arc.Radius) * scale),
                rotationAngle: 0,
                isLargeArc: Math.Abs(arc.SweepRad) > Math.PI,
                sweepDirection: arc.SweepRad >= 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);

            writer.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private void DrawLabel(DrawingContext context, LabelShape label, Func<Vec2, Point> project)
    {
        var typeface = new Typeface(
            new FontFamily(_theme.FontFamily),
            label.Italic ? FontStyle.Italic : FontStyle.Normal,
            FontWeight.Normal);

        // 用 FormattedText 而不是 TextLayout：这个版本的 DrawingContext.DrawText
        // 只收 FormattedText。两者都能拿到排版尺寸，够用。
        var layout = new FormattedText(
            label.Text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            label.Size,
            Brush(label.Fill));

        var (horizontal, vertical) = Alignment(label.Anchor);
        var at = project(label.At);

        // 和 SVG 导出同一套锚点规则：偏移按像素施加，所以任意缩放下标签间距一致。
        var x = at.X + horizontal * LabelOffset
                + (horizontal < 0 ? -layout.Width : horizontal > 0 ? 0 : -layout.Width / 2);
        var y = at.Y + vertical * LabelOffset
                + (vertical < 0 ? -layout.Height : vertical > 0 ? 0 : -layout.Height / 2);

        context.DrawText(layout, new Point(x, y));
    }

    private static (double Horizontal, double Vertical) Alignment(LabelAnchor anchor) => anchor switch
    {
        LabelAnchor.Center => (0, 0),
        LabelAnchor.Top => (0, -1),
        LabelAnchor.Bottom => (0, 1),
        LabelAnchor.Left => (-1, 0),
        LabelAnchor.Right => (1, 0),
        LabelAnchor.TopLeft => (-1, -1),
        LabelAnchor.TopRight => (1, -1),
        LabelAnchor.BottomLeft => (-1, 1),
        _ => (1, 1),
    };

    // ————————————————————————— 资源缓存 —————————————————————————
    //
    // 每帧为每个图元新建 Brush / Pen 会让拖动时明显掉帧。
    // 教学图的颜色种类是个位数，按颜色缓存足够，而且不占内存。

    private IBrush Brush(string color)
    {
        if (_brushes.TryGetValue(color, out var cached)) return cached;

        var brush = new SolidColorBrush(ParseColor(color));
        _brushes[color] = brush;
        return brush;
    }

    /// <summary>
    /// 解析颜色字符串。
    ///
    /// **必须自己解析，不能用 Color.Parse。** Avalonia 的八位十六进制是 #AARRGGBB，
    /// 而内核（以及 SVG、CSS）用的是 #RRGGBBAA —— 两者恰好相反。
    /// 直接交给 Color.Parse 的话，半透明的面会被解析成一个完全不透明的怪颜色：
    /// 立方体在屏幕上是实心绿块，而导出的 SVG 里是半透明的蓝面。
    /// </summary>
    private static Color ParseColor(string value)
    {
        var hex = value.TrimStart('#');

        try
        {
            return hex.Length switch
            {
                // #RRGGBBAA：alpha 在最后两位
                8 => Color.FromArgb(
                    Convert.ToByte(hex.Substring(6, 2), 16),
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)),

                // #RRGGBB
                6 => Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)),

                _ => Colors.Black,
            };
        }
        catch (Exception)
        {
            // 颜色写错了不该让整张图画不出来，退成黑色继续。
            return Colors.Black;
        }
    }

    private Pen PenFor(string color, double width, double[]? dash)
    {
        var key = $"{color}|{width:0.###}|{string.Join(',', dash ?? [])}";
        if (_pens.TryGetValue(key, out var cached)) return cached;

        var pen = new Pen(
            Brush(color),
            width,
            dash is { Length: > 0 } ? new DashStyle(dash, 0) : null,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        _pens[key] = pen;
        return pen;
    }
}
