namespace MathGeo.Core;

/// <summary>
/// 显示列表里的一个图元。
///
/// 这是整个渲染架构的枢纽：渲染器只产出这份数据，不认识任何绘图 API。
/// 于是同一份显示列表可以
///   ① 交给 Avalonia 的 DrawingContext 上屏，
///   ② 序列化成 SVG 导出（贴进 PPT，矢量不糊），
///   ③ 在无头模式里直接落盘给 agent 批量用。
/// 屏幕和导出天然一致，不可能出现"导出和看到的不是一个东西"。
/// </summary>
public abstract record Shape;

public sealed record LineShape(Vec2 A, Vec2 B, string Stroke, double Width, double[]? Dash = null) : Shape;

public sealed record PolygonShape(
    IReadOnlyList<Vec2> Points, string? Fill, string? Stroke, double StrokeWidth) : Shape;

public sealed record CircleShape(
    Vec2 Center, double Radius, string? Fill, string? Stroke, double StrokeWidth) : Shape;

public sealed record DotShape(Vec2 At, double Radius, string Fill) : Shape;

/// <summary>圆弧，用于角的标注。</summary>
public sealed record ArcShape(
    Vec2 Center, double Radius, double StartRad, double SweepRad, string Stroke, double Width) : Shape;

public enum LabelAnchor
{
    Center, Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight,
}

/// <summary>
/// 顶点标签。
///
/// 刻意只给 8 个锚点方向、不给任意像素偏移：偏移量由输出端按像素施加。
/// 这样标签在任意缩放下都保持同样的视觉间距，而且旋转立体图时
/// 不会因为世界坐标尺度变化而忽大忽小 —— 这是"标签不叠字"能成立的前提。
/// </summary>
public sealed record LabelShape(
    Vec2 At, string Text, LabelAnchor Anchor, string Fill, double Size, bool Italic = true) : Shape;

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public bool IsEmpty => double.IsInfinity(MinX) || Width <= 0 && Height <= 0;

    public static Bounds2 Of(IEnumerable<Shape> shapes)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        void Include(Vec2 p, double radius = 0)
        {
            minX = Math.Min(minX, p.X - radius);
            minY = Math.Min(minY, p.Y - radius);
            maxX = Math.Max(maxX, p.X + radius);
            maxY = Math.Max(maxY, p.Y + radius);
        }

        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case LineShape l: Include(l.A); Include(l.B); break;
                case DotShape d: Include(d.At, d.Radius); break;
                case LabelShape t: Include(t.At); break;
                case PolygonShape p: foreach (var v in p.Points) Include(v); break;
                case CircleShape c: Include(c.Center, c.Radius); break;
                case ArcShape a: Include(a.Center, a.Radius); break;
            }
        }

        return new Bounds2(minX, minY, maxX, maxY);
    }
}

/// <summary>
/// 配色与线宽。默认值直接取上一会话那套设计令牌（Accent #5B6BE8 等），
/// 这样图上的颜色和界面上的颜色是同一套，不会出现"图里是另一个软件"的割裂感。
/// </summary>
public sealed record Theme
{
    public string Background { get; init; } = "#FFFFFF";

    /// <summary>页面底色（预览 HTML 用）。和图形底色分开：图是白纸，页面是桌面。</summary>
    public string PageBackground { get; init; } = "#F4F5F9";

    /// <summary>分隔线、卡片描边。</summary>
    public string SurfaceBorder { get; init; } = "#E6E8EF";

    public string PointFill { get; init; } = "#1F2430";
    public string LineStroke { get; init; } = "#1F2430";
    public string CurveStroke { get; init; } = "#1F2430";

    /// <summary>辅助线：灰 + 虚线，和课本习惯一致。</summary>
    public string HelperStroke { get; init; } = "#9CA3AF";

    /// <summary>所求对象（"求 AD"里的 AD）：用 Danger 红，一眼看到。</summary>
    public string AnswerStroke { get; init; } = "#D0453F";

    public string HighlightStroke { get; init; } = "#5B6BE8";

    /// <summary>
    /// 强调色的实色浅底（答案框、选中态的背景）。
    /// 刻意不用 PolygonFill —— 那个带 alpha，当背景会几乎看不见。
    /// </summary>
    public string AccentSoft { get; init; } = "#EEF0FE";

    public string PolygonFill { get; init; } = "#5B6BE81F";
    public string PolygonStroke { get; init; } = "#5B6BE8";

    /// <summary>立体几何里"面"的描边。刻意比棱淡，让棱是主角。</summary>
    public string FaceStroke { get; init; } = "#8A93C4";

    /// <summary>被强调的面。讲截面、二面角、面面关系时用。</summary>
    public string FaceFillHighlight { get; init; } = "#D0453F33";

    /// <summary>被遮挡的棱用这套虚线。比辅助线短一点，符合课本习惯。</summary>
    public double[] HiddenEdgeDash { get; init; } = [5, 4];

    /// <summary>
    /// 截面。用 Success 绿：和 Accent 蓝、Danger 红都能区分开，
    /// 而且截面常常和"所求对象"同时出现，颜色不能撞。
    /// </summary>
    public string SectionFill { get; init; } = "#2FA84F38";
    public string SectionStroke { get; init; } = "#2FA84F";

    /// <summary>角的标注。用 Warning 橙：和截面绿、所求红、强调蓝都区分得开。</summary>
    public string AngleStroke { get; init; } = "#C8871F";

    /// <summary>网格线。要足够淡，否则会盖过图形本身。</summary>
    public string GridLine { get; init; } = "#E6E8EF";
    public double GridWidth { get; init; } = 1;

    /// <summary>
    /// 坐标轴：X 红、Y 绿、Z 蓝。
    /// 和三维软件的惯例一致（也和你 Unity 里那套 gizmo 一致），
    /// 学生换到别的软件里不会认错轴。
    /// </summary>
    public string AxisX { get; init; } = "#D0453F";
    public string AxisY { get; init; } = "#2FA84F";
    public string AxisZ { get; init; } = "#5B6BE8";
    public double AxisWidth { get; init; } = 1.6;

    public string LabelFill { get; init; } = "#1F2430";

    public double LineWidth { get; init; } = 1.6;
    public double HelperWidth { get; init; } = 1.0;
    public double PointRadius { get; init; } = 3.2;
    public double LabelSize { get; init; } = 15;

    public double[] HelperDash { get; init; } = [6, 4];

    /// <summary>
    /// 课本里顶点字母是斜体衬线体。字体栈把数学字体放前面、中文放后面，
    /// 这样 "A₁" 和 "甲" 都能正常显示。
    /// </summary>
    public string FontFamily { get; init; } =
        "Latin Modern Math, STIX Two Math, Cambria Math, Times New Roman, Noto Sans SC, serif";

    /// <summary>暗色主题。图要跟界面主题走，否则暗色模式下白底图会刺眼。</summary>
    public static Theme Textbook { get; } = new();

    public static Theme Dark { get; } = new()
    {
        Background = "#1E222A",
        PageBackground = "#14161C",
        SurfaceBorder = "#2A2F3A",
        PointFill = "#E8EAF0",
        LineStroke = "#E8EAF0",
        CurveStroke = "#E8EAF0",
        HelperStroke = "#6E7686",
        AnswerStroke = "#F0736C",
        HighlightStroke = "#7C8BF5",
        AccentSoft = "#262B45",
        PolygonFill = "#7C8BF526",
        PolygonStroke = "#7C8BF5",
        FaceStroke = "#6E7686",
        FaceFillHighlight = "#F0736C33",
        SectionFill = "#4ED17A38",
        SectionStroke = "#4ED17A",
        AngleStroke = "#E0A94A",
        GridLine = "#2A2F3A",
        AxisX = "#F0736C",
        AxisY = "#4ED17A",
        AxisZ = "#7C8BF5",
        LabelFill = "#E8EAF0",
    };
}

/// <summary>
/// 浅色 → 深色的颜色映射。
///
/// 存在的理由：交互式预览要把主题切换做成瞬时的。如果为两种主题各存一套帧，
/// 文件大小直接翻倍（一份动点动画是几十帧 × 几十个图元）。所以帧里只带浅色，
/// 切换时按这张表换色。
///
/// 表是从 Theme.Textbook / Theme.Dark 逐字段抄下来的。加新颜色时两处都要改 ——
/// SolidTests 里有一条测试专门盯这件事，漏了会红。
/// </summary>
public static class ThemePalette
{
    public static IReadOnlyDictionary<string, string> DarkMap { get; } = BuildDarkMap();

    private static Dictionary<string, string> BuildDarkMap()
    {
        var light = Theme.Textbook;
        var dark = Theme.Dark;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Pair(string lightColor, string darkColor) => map[lightColor] = darkColor;

        Pair(light.Background, dark.Background);
        Pair(light.PageBackground, dark.PageBackground);
        Pair(light.SurfaceBorder, dark.SurfaceBorder);
        Pair(light.PointFill, dark.PointFill);
        Pair(light.LineStroke, dark.LineStroke);
        Pair(light.CurveStroke, dark.CurveStroke);
        Pair(light.HelperStroke, dark.HelperStroke);
        Pair(light.AnswerStroke, dark.AnswerStroke);
        Pair(light.HighlightStroke, dark.HighlightStroke);
        Pair(light.AccentSoft, dark.AccentSoft);
        Pair(light.PolygonFill, dark.PolygonFill);
        Pair(light.PolygonStroke, dark.PolygonStroke);
        Pair(light.FaceStroke, dark.FaceStroke);
        Pair(light.FaceFillHighlight, dark.FaceFillHighlight);
        Pair(light.SectionFill, dark.SectionFill);
        Pair(light.SectionStroke, dark.SectionStroke);
        Pair(light.AngleStroke, dark.AngleStroke);
        Pair(light.GridLine, dark.GridLine);
        Pair(light.AxisX, dark.AxisX);
        Pair(light.AxisY, dark.AxisY);
        Pair(light.AxisZ, dark.AxisZ);
        Pair(light.LabelFill, dark.LabelFill);

        return map;
    }
}
