using System.Text;

namespace MathGeo.Core;

/// <summary>
/// 显示列表 → JSON。
///
/// 存在的理由有两个，而且它们指向同一件事 —— 显示列表是唯一的渲染抽象：
///   1. 交互式预览把它交给浏览器里的渲染器，于是"屏幕上看到的"和"导出 SVG"
///      是同一条数据出来的，不可能不一致；
///   2. MCP 的 render 工具应该返回这个而不是 SVG 字符串 ——
///      agent 拿到结构化数据才能继续处理（算面积、判断形状、比较两帧差异）。
///
/// 键名刻意用单字母：一份动点动画是几十帧，每帧几十个图元，
/// 键名的长度会直接乘上去。字段含义写在下面每个 Write 方法上。
/// </summary>
public static class DisplayListJson
{
    public static string Write(IReadOnlyList<Shape> shapes)
    {
        var builder = new StringBuilder(shapes.Count * 64);
        builder.Append('[');

        for (var i = 0; i < shapes.Count; i++)
        {
            if (i > 0) builder.Append(',');
            WriteShape(builder, shapes[i]);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static void WriteShape(StringBuilder builder, Shape shape)
    {
        switch (shape)
        {
            // t=l 线段：a/b 端点，s 描边色，w 线宽，d 虚线段长（省略即实线）
            case LineShape line:
                builder.Append("{\"t\":\"l\",\"a\":");
                WritePoint(builder, line.A);
                builder.Append(",\"b\":");
                WritePoint(builder, line.B);
                builder.Append(",\"s\":\"").Append(line.Stroke).Append("\",\"w\":").Append(N(line.Width));
                if (line.Dash is { Length: > 0 })
                    builder.Append(",\"d\":[").Append(string.Join(',', line.Dash.Select(N))).Append(']');
                builder.Append('}');
                break;

            // t=p 多边形：p 顶点环，f 填充色（null 即不填充），s 描边色，w 线宽
            case PolygonShape polygon:
                builder.Append("{\"t\":\"p\",\"p\":[");
                for (var i = 0; i < polygon.Points.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    WritePoint(builder, polygon.Points[i]);
                }
                builder.Append("],\"f\":").Append(polygon.Fill is null ? "null" : $"\"{polygon.Fill}\"");
                builder.Append(",\"s\":").Append(polygon.Stroke is null ? "null" : $"\"{polygon.Stroke}\"");
                builder.Append(",\"w\":").Append(N(polygon.StrokeWidth)).Append('}');
                break;

            // t=c 圆：c 圆心，r 半径，f 填充色，s 描边色，w 线宽
            case CircleShape circle:
                builder.Append("{\"t\":\"c\",\"c\":");
                WritePoint(builder, circle.Center);
                builder.Append(",\"r\":").Append(N(circle.Radius));
                builder.Append(",\"f\":").Append(circle.Fill is null ? "null" : $"\"{circle.Fill}\"");
                builder.Append(",\"s\":").Append(circle.Stroke is null ? "null" : $"\"{circle.Stroke}\"");
                builder.Append(",\"w\":").Append(N(circle.StrokeWidth)).Append('}');
                break;

            // t=o 顶点圆点：a 位置，r 半径，f 填充色
            case DotShape dot:
                builder.Append("{\"t\":\"o\",\"a\":");
                WritePoint(builder, dot.At);
                builder.Append(",\"r\":").Append(N(dot.Radius));
                builder.Append(",\"f\":\"").Append(dot.Fill).Append("\"}");
                break;

            // t=x 文本：a 锚点，x 内容，n 锚向，f 颜色，z 字号，i 是否斜体
            case LabelShape label:
                builder.Append("{\"t\":\"x\",\"a\":");
                WritePoint(builder, label.At);
                builder.Append(",\"x\":\"").Append(Escape(label.Text)).Append('"');
                builder.Append(",\"n\":\"").Append(AnchorName(label.Anchor)).Append('"');
                builder.Append(",\"f\":\"").Append(label.Fill).Append('"');
                builder.Append(",\"z\":").Append(N(label.Size));
                if (label.Italic) builder.Append(",\"i\":true");
                builder.Append('}');
                break;

            // t=a 圆弧：c 圆心，r 半径，b 起始角，e 扫过角（弧度），s 描边色，w 线宽
            case ArcShape arc:
                builder.Append("{\"t\":\"a\",\"c\":");
                WritePoint(builder, arc.Center);
                builder.Append(",\"r\":").Append(N(arc.Radius));
                builder.Append(",\"b\":").Append(N(arc.StartRad));
                builder.Append(",\"e\":").Append(N(arc.SweepRad));
                builder.Append(",\"s\":\"").Append(arc.Stroke).Append('"');
                builder.Append(",\"w\":").Append(N(arc.Width)).Append('}');
                break;
        }
    }

    private static void WritePoint(StringBuilder builder, Vec2 point)
        => builder.Append('[').Append(N(point.X)).Append(',').Append(N(point.Y)).Append(']');

    private static string AnchorName(LabelAnchor anchor) => anchor switch
    {
        LabelAnchor.Center => "c",
        LabelAnchor.Top => "t",
        LabelAnchor.Bottom => "b",
        LabelAnchor.Left => "l",
        LabelAnchor.Right => "r",
        LabelAnchor.TopLeft => "tl",
        LabelAnchor.TopRight => "tr",
        LabelAnchor.BottomLeft => "bl",
        _ => "br",
    };

    private static string N(double value) => MathUtil.Num(value);

    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
