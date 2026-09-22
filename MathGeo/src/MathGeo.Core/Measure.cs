namespace MathGeo.Core;

/// <summary>
/// 测量。内核不只负责"画出来"，还要能回答"多长、多大、多少度"——
/// 这是"老师问、软件答"和 MCP 工具（agent 核对答案）共同需要的能力。
///
/// 全部返回可空：测不了就返回 null，而不是抛异常或者给一个 0 冒充答案。
/// </summary>
public static class Measure
{
    /// <summary>线段长度。其它对象返回 null —— 点没有"长度"，不要用它的模长冒充。</summary>
    public static double? Length(Scene scene, string objectId) => scene.Find(objectId) switch
    {
        GeoLine line when scene.Point(line.AId) is { } a && scene.Point(line.BId) is { } b
            => Vec3.Distance(a.Position, b.Position),
        _ => null,
    };

    /// <summary>面积。多边形、截面、三角形都能算。</summary>
    public static double? Area(Scene scene, string objectId) => scene.Find(objectId) switch
    {
        GeoSection section => CrossSection.Of(scene, section) is { } polygon
            ? PolygonArea(polygon)
            : null,

        GeoPolygon polygon => PolygonArea(VertexPositions(scene, polygon.VertexIds)),

        GeoCircle circle => Math.PI * circle.ResolveRadius(scene) * circle.ResolveRadius(scene),

        _ => null,
    };

    /// <summary>
    /// 平面多边形的面积，用 Newell 法 —— 它对三维里的任意平面多边形都成立，
    /// 不需要先把它投影到某个坐标平面上，也不用管顶点从哪一端开始。
    /// </summary>
    public static double PolygonArea(IReadOnlyList<Vec3> points)
    {
        if (points.Count < 3) return 0;

        var accumulator = Vec3.Zero;
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            accumulator += Vec3.Cross(current, next);
        }

        return accumulator.Length / 2;
    }

    /// <summary>周长。多边形和截面用。</summary>
    public static double? Perimeter(Scene scene, string objectId)
    {
        var points = objectId switch
        {
            _ when scene.Find(objectId) is GeoSection section => CrossSection.Of(scene, section),
            _ when scene.Find(objectId) is GeoPolygon polygon => VertexPositions(scene, polygon.VertexIds),
            _ => null,
        };

        if (points is null || points.Count < 3) return null;

        var total = 0.0;
        for (var i = 0; i < points.Count; i++)
            total += Vec3.Distance(points[i], points[(i + 1) % points.Count]);

        return total;
    }

    private static IReadOnlyList<Vec3> VertexPositions(Scene scene, IReadOnlyList<string> ids)
    {
        var positions = new List<Vec3>(ids.Count);

        foreach (var id in ids)
        {
            var point = scene.Point(id);
            if (point is null) return [];
            positions.Add(point.Position);
        }

        return positions;
    }
}
