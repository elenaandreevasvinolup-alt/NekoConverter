using System.Globalization;

namespace MathGeo.Core;

/// <summary>
/// 角的标注绘制。二维和三维共用一份实现 —— 差别只在投影函数，所以把它当参数传进来。
///
/// 直角一律改用直角符号而不是写 90°：这是课本规范，学生认的就是那个小方块。
/// 非直角才画圆弧并标数值。
/// </summary>
internal static class AngleMark
{
    /// <summary>直角判定的容差（度）。落在这个范围内就画直角符号。</summary>
    private const double RightAngleTolerance = 0.5;

    /// <summary>圆弧采样段数。三维里圆弧投影后是圆锥曲线，没有现成图元，只能采样。</summary>
    private const int ArcSamples = 32;

    public static void Build(
        Vec3 vertex, Vec3 pointA, Vec3 pointB,
        double radius, bool showValue,
        Func<Vec3, Vec2> project, Theme theme, List<Shape> shapes)
    {
        if (radius < MathUtil.Epsilon) return;

        var directionA = (pointA - vertex).Normalized();
        var directionB = (pointB - vertex).Normalized();

        // 有一条边退化成一个点：画不出角，静默跳过（诊断在求解阶段已经记过了）。
        if (directionA.LengthSquared < 0.5 || directionB.LengthSquared < 0.5) return;

        var degrees = MathUtil.RadToDeg(
            Math.Acos(MathUtil.Clamp(Vec3.Dot(directionA, directionB), -1, 1)));

        if (Math.Abs(degrees - 90) <= RightAngleTolerance)
        {
            BuildRightAngleSymbol(vertex, directionA, directionB, radius, project, theme, shapes);
            return;
        }

        var arc = ArcPoints(vertex, directionA, directionB, radius);
        for (var i = 0; i + 1 < arc.Count; i++)
            shapes.Add(new LineShape(project(arc[i]), project(arc[i + 1]), theme.AngleStroke, theme.LineWidth));

        if (!showValue) return;

        var bisector = (directionA + directionB).Normalized();
        if (bisector.LengthSquared < 0.5) return;

        shapes.Add(new LabelShape(
            project(vertex + bisector * (radius * 1.6)),
            $"{degrees.ToString("0.#", CultureInfo.InvariantCulture)}°",
            LabelAnchor.Center,
            theme.AngleStroke,
            theme.LabelSize * 0.85,
            Italic: false));
    }

    /// <summary>直角符号：从顶点沿两边各取一小段，再补上对角，画成一个"小方块"。</summary>
    private static void BuildRightAngleSymbol(
        Vec3 vertex, Vec3 directionA, Vec3 directionB, double radius,
        Func<Vec3, Vec2> project, Theme theme, List<Shape> shapes)
    {
        var size = radius * 0.5;

        var alongA = project(vertex + directionA * size);
        var corner = project(vertex + (directionA + directionB) * size);
        var alongB = project(vertex + directionB * size);

        shapes.Add(new LineShape(alongA, corner, theme.AngleStroke, theme.HelperWidth));
        shapes.Add(new LineShape(corner, alongB, theme.AngleStroke, theme.HelperWidth));
    }

    /// <summary>在两条边张成的平面里采样圆弧。</summary>
    public static IReadOnlyList<Vec3> ArcPoints(
        Vec3 vertex, Vec3 directionA, Vec3 directionB, double radius, int samples = ArcSamples)
    {
        var cosine = MathUtil.Clamp(Vec3.Dot(directionA, directionB), -1, 1);
        var perpendicular = directionB - directionA * cosine;

        // 两条边平行（角为 0° 或 180°）：圆弧退化，给两个端点就够。
        if (perpendicular.Length < MathUtil.Epsilon)
            return [vertex + directionA * radius, vertex + directionB * radius];

        perpendicular = perpendicular.Normalized();
        var angle = Math.Acos(cosine);

        var points = new List<Vec3>(samples + 1);
        for (var i = 0; i <= samples; i++)
        {
            var t = angle * i / samples;
            points.Add(vertex + (directionA * Math.Cos(t) + perpendicular * Math.Sin(t)) * radius);
        }

        return points;
    }
}
