using System.Globalization;
using System.Text;

namespace MathGeo.Core;

public sealed record SvgOptions
{
    /// <summary>输出位图等价宽度（像素）。SVG 本身是矢量的，这个值只决定字号与间距的观感。</summary>
    public double TargetWidth { get; init; } = 960;

    public double Padding { get; init; } = 28;

    /// <summary>标签离顶点的像素距离。用像素而不是世界单位，保证任意缩放下观感一致。</summary>
    public double LabelOffset { get; init; } = 11;

    public bool IncludeBackground { get; init; } = true;
}

/// <summary>
/// 显示列表 → SVG。
///
/// 只做这一件事：世界坐标一次性映射到像素坐标（Y 轴翻转也在这里做完），
/// 然后逐条输出。因为显示列表就是屏幕无关的，所以屏幕渲染和导出的差异
/// 被压缩到"谁来画"这一件事上，不可能出现导出的图和看到的图不一致。
/// </summary>
public static class SvgWriter
{
    public static string Write(IReadOnlyList<Shape> shapes, Theme theme, SvgOptions? options = null)
    {
        var opt = options ?? new SvgOptions();

        var content = Bounds2.Of(shapes);
        if (!double.IsFinite(content.MinX) || !double.IsFinite(content.MaxX))
            content = new Bounds2(0, 0, 1, 1);

        var usable = Math.Max(opt.TargetWidth - 2 * opt.Padding, 1);
        var scale = content.Width > 1e-6 ? usable / content.Width : 40.0;

        var width = opt.TargetWidth;
        var height = content.Height * scale + 2 * opt.Padding;
        if (!double.IsFinite(height) || height < 1) height = opt.TargetWidth;

        // 世界坐标（Y 向上）→ 像素坐标（Y 向下）。翻转只发生这一次。
        Vec2 ToPixel(Vec2 world) => new(
            opt.Padding + (world.X - content.MinX) * scale,
            opt.Padding + (content.MaxY - world.Y) * scale);

        var sb = new StringBuilder(8192);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(width))
          .Append("\" height=\"").Append(N(height))
          .Append("\" viewBox=\"0 0 ").Append(N(width)).Append(' ').Append(N(height))
          .Append("\">\n");

        if (opt.IncludeBackground)
            sb.Append("  <rect width=\"100%\" height=\"100%\" fill=\"").Append(theme.Background).Append("\"/>\n");

        foreach (var shape in shapes)
            WriteShape(sb, shape, theme, opt, ToPixel, scale);

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void WriteShape(
        StringBuilder sb, Shape shape, Theme theme, SvgOptions opt, Func<Vec2, Vec2> toPixel, double scale)
    {
        switch (shape)
        {
            case LineShape line:
            {
                var a = toPixel(line.A);
                var b = toPixel(line.B);
                sb.Append("  <line x1=\"").Append(N(a.X)).Append("\" y1=\"").Append(N(a.Y))
                  .Append("\" x2=\"").Append(N(b.X)).Append("\" y2=\"").Append(N(b.Y))
                  .Append("\" stroke=\"").Append(line.Stroke)
                  .Append("\" stroke-width=\"").Append(N(line.Width))
                  .Append("\" stroke-linecap=\"round\"");
                if (line.Dash is { Length: > 0 })
                    sb.Append(" stroke-dasharray=\"").Append(string.Join(' ', line.Dash.Select(N))).Append('"');
                sb.Append("/>\n");
                break;
            }

            case PolygonShape polygon:
            {
                sb.Append("  <polygon points=\"");
                for (var i = 0; i < polygon.Points.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    var p = toPixel(polygon.Points[i]);
                    sb.Append(N(p.X)).Append(',').Append(N(p.Y));
                }
                sb.Append("\" fill=\"").Append(polygon.Fill ?? "none")
                  .Append("\" stroke=\"").Append(polygon.Stroke ?? "none")
                  .Append("\" stroke-width=\"").Append(N(polygon.StrokeWidth))
                  .Append("\" stroke-linejoin=\"round\"/>\n");
                break;
            }

            case CircleShape circle:
            {
                var c = toPixel(circle.Center);
                sb.Append("  <circle cx=\"").Append(N(c.X)).Append("\" cy=\"").Append(N(c.Y))
                  .Append("\" r=\"").Append(N(Math.Abs(circle.Radius) * scale))
                  .Append("\" fill=\"").Append(circle.Fill ?? "none")
                  .Append("\" stroke=\"").Append(circle.Stroke ?? "none")
                  .Append("\" stroke-width=\"").Append(N(circle.StrokeWidth))
                  .Append("\"/>\n");
                break;
            }

            case DotShape dot:
            {
                var c = toPixel(dot.At);
                sb.Append("  <circle cx=\"").Append(N(c.X)).Append("\" cy=\"").Append(N(c.Y))
                  .Append("\" r=\"").Append(N(dot.Radius))
                  .Append("\" fill=\"").Append(dot.Fill).Append("\"/>\n");
                break;
            }

            case ArcShape arc:
                WriteArc(sb, arc, toPixel, scale);
                break;

            case LabelShape label:
                WriteLabel(sb, label, theme, opt, toPixel);
                break;
        }
    }

    private static void WriteArc(StringBuilder sb, ArcShape arc, Func<Vec2, Vec2> toPixel, double scale)
    {
        var start = PointOnCircle(arc.Center, arc.Radius, arc.StartRad);
        var end = PointOnCircle(arc.Center, arc.Radius, arc.StartRad + arc.SweepRad);

        var a = toPixel(start);
        var b = toPixel(end);
        var radius = Math.Abs(arc.Radius) * scale;

        // 世界坐标是 Y 向上、逆时针为正；像素坐标 Y 向下，方向感被翻转一次，
        // 所以 sweep-flag 取反（世界逆时针 → 像素顺时针 = 1）。
        var largeArc = Math.Abs(arc.SweepRad) > Math.PI ? 1 : 0;
        var sweepFlag = arc.SweepRad >= 0 ? 1 : 0;

        sb.Append("  <path d=\"M ").Append(N(a.X)).Append(' ').Append(N(a.Y))
          .Append(" A ").Append(N(radius)).Append(' ').Append(N(radius))
          .Append(" 0 ").Append(largeArc).Append(' ').Append(sweepFlag).Append(' ')
          .Append(N(b.X)).Append(' ').Append(N(b.Y))
          .Append("\" fill=\"none\" stroke=\"").Append(arc.Stroke)
          .Append("\" stroke-width=\"").Append(N(arc.Width))
          .Append("\" stroke-linecap=\"round\"/>\n");
    }

    private static void WriteLabel(
        StringBuilder sb, LabelShape label, Theme theme, SvgOptions opt, Func<Vec2, Vec2> toPixel)
    {
        var (anchor, dx, dy) = MapAnchor(label.Anchor, opt.LabelOffset);
        var at = toPixel(label.At);

        sb.Append("  <text x=\"").Append(N(at.X + dx)).Append("\" y=\"").Append(N(at.Y + dy))
          .Append("\" font-family=\"").Append(Escape(theme.FontFamily))
          .Append("\" font-size=\"").Append(N(label.Size))
          .Append("\" fill=\"").Append(label.Fill)
          .Append("\" text-anchor=\"").Append(anchor)
          .Append("\" dominant-baseline=\"middle\"");
        if (label.Italic) sb.Append(" font-style=\"italic\"");
        sb.Append('>').Append(Escape(label.Text)).Append("</text>\n");
    }

    /// <summary>8 个锚点 → (text-anchor, 像素偏移)。所有偏移都在像素空间施加。</summary>
    private static (string Anchor, double Dx, double Dy) MapAnchor(LabelAnchor anchor, double offset)
        => anchor switch
        {
            LabelAnchor.Center => ("middle", 0, 0),
            LabelAnchor.Top => ("middle", 0, -offset),
            LabelAnchor.Bottom => ("middle", 0, offset),
            LabelAnchor.Left => ("end", -offset, 0),
            LabelAnchor.Right => ("start", offset, 0),
            LabelAnchor.TopLeft => ("end", -offset, -offset),
            LabelAnchor.TopRight => ("start", offset, -offset),
            LabelAnchor.BottomLeft => ("end", -offset, offset),
            _ => ("start", offset, offset),
        };

    private static Vec2 PointOnCircle(Vec2 center, double radius, double radians)
        => new(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));

    private static string N(double value) => MathUtil.Num(value);

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
