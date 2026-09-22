namespace MathGeo.Core;

/// <summary>
/// 路径几何：把一个路径对象当成"可以取参数点的曲线"。
/// 动点（OnPath）就是靠它拿到坐标的，所以这里是动点问题的基础设施。
/// </summary>
public static class PathGeometry
{
    /// <summary>
    /// 取路径上参数 t 处的点。
    ///
    /// 参数约定（刻意做得简单可预测，因为 agent 也会用）：
    ///   线段/射线/直线：t=0 在 A，t=1 在 B，线性外推；
    ///   圆：t 是归一化角度，t=0 在圆心正右方，逆时针为正。
    /// </summary>
    public static Vec3 PointAt(Scene scene, GeoObject path, double t) => path switch
    {
        GeoLine line => PointOnLine(scene, line, t),
        GeoCircle circle => PointOnCircle(scene, circle, t),
        _ => throw new NotSupportedException($"'{path.Id}' 不能作为路径使用"),
    };

    /// <summary>路径长度。无限长的线返回 PositiveInfinity。</summary>
    public static double Length(Scene scene, GeoObject path) => path switch
    {
        GeoLine { Kind: LineKind.Segment } line
            => Vec3.Distance(scene.Point(line.AId)!.Position, scene.Point(line.BId)!.Position),
        GeoLine => double.PositiveInfinity,
        GeoCircle circle => 2 * Math.PI * circle.ResolveRadius(scene),
        _ => double.NaN,
    };

    /// <summary>路径是否闭合（决定动点在端点处是否折返）。</summary>
    public static bool IsClosed(GeoObject path) => path switch
    {
        GeoCircle => true,
        GeoPolygon => true,
        _ => false,
    };

    private static Vec3 PointOnLine(Scene scene, GeoLine line, double t)
    {
        var a = scene.Point(line.AId)!.Position;
        var b = scene.Point(line.BId)!.Position;
        return Vec3.Lerp(a, b, t);
    }

    private static Vec3 PointOnCircle(Scene scene, GeoCircle circle, double t)
    {
        var center = scene.Point(circle.CenterId)!.Position;
        var radius = circle.ResolveRadius(scene);
        var angle = 2 * Math.PI * t;
        return new Vec3(
            center.X + radius * Math.Cos(angle),
            center.Y + radius * Math.Sin(angle),
            center.Z);
    }
}

public static class CircleGeometry
{
    /// <summary>半径可能是常量，也可能由"过某点"动态决定。</summary>
    public static double ResolveRadius(this GeoCircle circle, Scene scene)
    {
        if (circle.ThroughId is null) return circle.Radius;
        var center = scene.Point(circle.CenterId);
        var through = scene.Point(circle.ThroughId);
        if (center is null || through is null) return circle.Radius;
        return Vec3.Distance(center.Position, through.Position);
    }
}

/// <summary>
/// 求交。教学场景里 99% 是直线与圆，全部在 XY 平面上算。
/// 立体几何的"截面求交"会在三维核心里另起一套，不在这里硬凑。
/// </summary>
public static class Intersect
{
    public static IReadOnlyList<Vec3> Of(Scene scene, GeoObject a, GeoObject b) => (a, b) switch
    {
        (GeoLine la, GeoLine lb) => LineLine(scene, la, lb),
        (GeoLine line, GeoCircle circle) => LineCircle(scene, line, circle),
        (GeoCircle circle, GeoLine line) => LineCircle(scene, line, circle),
        (GeoCircle ca, GeoCircle cb) => CircleCircle(scene, ca, cb),
        _ => [],
    };

    private static List<Vec3> LineLine(Scene scene, GeoLine la, GeoLine lb)
    {
        var a1 = scene.Point(la.AId)!.Position;
        var a2 = scene.Point(la.BId)!.Position;
        var b1 = scene.Point(lb.AId)!.Position;
        var b2 = scene.Point(lb.BId)!.Position;

        var d1 = a2 - a1;
        var d2 = b2 - b1;
        var denom = d1.X * d2.Y - d1.Y * d2.X;

        // 平行（含重合）。重合在教学中是"无数交点"，不给出确定解，直接判无交点。
        if (Math.Abs(denom) < MathUtil.Tolerance) return [];

        var diff = b1 - a1;
        var t = (diff.X * d2.Y - diff.Y * d2.X) / denom;
        var u = (diff.X * d1.Y - diff.Y * d1.X) / denom;

        if (!InRange(la.Kind, t) || !InRange(lb.Kind, u)) return [];

        var p = a1 + d1 * t;
        return [new Vec3(p.X, p.Y, 0)];
    }

    private static List<Vec3> LineCircle(Scene scene, GeoLine line, GeoCircle circle)
    {
        var center = scene.Point(circle.CenterId)!.Position;
        var radius = circle.ResolveRadius(scene);
        var a = scene.Point(line.AId)!.Position;
        var d = scene.Point(line.BId)!.Position - a;

        var f = a - center;
        var qa = d.X * d.X + d.Y * d.Y;
        if (qa < MathUtil.Epsilon) return [];

        var qb = 2 * (f.X * d.X + f.Y * d.Y);
        var qc = f.X * f.X + f.Y * f.Y - radius * radius;
        var disc = qb * qb - 4 * qa * qc;
        if (disc < -MathUtil.Tolerance) return [];

        var root = Math.Sqrt(Math.Max(disc, 0));
        var ts = root < MathUtil.Tolerance
            ? new[] { -qb / (2 * qa) }
            : new[] { (-qb - root) / (2 * qa), (-qb + root) / (2 * qa) };

        var result = new List<Vec3>(2);
        foreach (var t in ts)
        {
            if (!InRange(line.Kind, t)) continue;
            var p = a + d * t;
            result.Add(new Vec3(p.X, p.Y, 0));
        }
        return result;
    }

    private static List<Vec3> CircleCircle(Scene scene, GeoCircle ca, GeoCircle cb)
    {
        var p1 = scene.Point(ca.CenterId)!.Position.XY;
        var r1 = ca.ResolveRadius(scene);
        var p2 = scene.Point(cb.CenterId)!.Position.XY;
        var r2 = cb.ResolveRadius(scene);

        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var d = Math.Sqrt(dx * dx + dy * dy);

        if (d < MathUtil.Epsilon) return [];                                // 同心
        if (d > r1 + r2 + MathUtil.Tolerance) return [];                    // 相离
        if (d < Math.Abs(r1 - r2) - MathUtil.Tolerance) return [];          // 内含

        var a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
        var hSq = r1 * r1 - a * a;
        var h = hSq <= 0 ? 0 : Math.Sqrt(hSq);

        var xm = p1.X + a * dx / d;
        var ym = p1.Y + a * dy / d;

        if (h < MathUtil.Tolerance) return [new Vec3(xm, ym, 0)];

        var rx = -dy * (h / d);
        var ry = dx * (h / d);
        return [new Vec3(xm + rx, ym + ry, 0), new Vec3(xm - rx, ym - ry, 0)];
    }

    /// <summary>参数是否落在这条线对象实际存在的范围内（线段/射线/直线）。</summary>
    private static bool InRange(LineKind kind, double t) => kind switch
    {
        LineKind.Segment => t >= -MathUtil.Tolerance && t <= 1 + MathUtil.Tolerance,
        LineKind.Ray => t >= -MathUtil.Tolerance,
        _ => true,
    };
}
