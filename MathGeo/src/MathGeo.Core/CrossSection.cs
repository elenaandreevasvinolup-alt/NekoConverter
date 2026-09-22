namespace MathGeo.Core;

/// <summary>
/// 截面：用一个平面去切一个多面体，求交线多边形。
///
/// 这是高中立体几何里最典型的"学生想象不出来"的题 —— 交线在哪儿、是什么形状，
/// 在黑板上靠嘴说是讲不清的，只能靠图。而且它也是"建系 + 法向量"那条路之前，
/// 必须先用眼睛看明白的那一步。
///
/// 算法：遍历所有棱，找出与平面相交的点，去重后按平面内的极角排序成环。
/// 极角排序对凸截面是精确的；教学里的截面几乎都是凸的。
/// （非凸截面需要按线段首尾相接走环，那是下一步的事，先不为它增加复杂度。）
/// </summary>
public static class CrossSection
{
    /// <summary>求交线多边形。平面用"一点 + 法向量"给出，顶点按平面内逆时针排好。</summary>
    public static IReadOnlyList<Vec3>? Of(
        IReadOnlyList<Vec3> vertices, IReadOnlyList<int[]> faces, Vec3 planePoint, Vec3 planeNormal)
    {
        var normal = planeNormal.Normalized();
        if (normal.LengthSquared < 0.5) return null;   // 三点共线，定不出平面

        // 容差按体本身的尺度取相对值，否则换一个尺寸的正方体就得重新调参。
        var scale = Diagonal(vertices);
        var onPlane = scale * 1e-9;
        var dedup = scale * 1e-6;

        var edges = new HashSet<(int, int)>();
        foreach (var face in faces)
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                edges.Add(a < b ? (a, b) : (b, a));
            }

        var points = new List<Vec3>();

        foreach (var (a, b) in edges)
        {
            var da = Vec3.Dot(vertices[a] - planePoint, normal);
            var db = Vec3.Dot(vertices[b] - planePoint, normal);

            // 整条棱躺在平面上：无数交点，给不出确定的截面，跳过。
            if (Math.Abs(da) <= onPlane && Math.Abs(db) <= onPlane) continue;

            // 两端同侧：不相交。
            if (da > onPlane && db > onPlane) continue;
            if (da < -onPlane && db < -onPlane) continue;

            // 端点正好落在平面上：直接用这个顶点，避免除零。
            if (Math.Abs(da) <= onPlane) { AddUnique(points, vertices[a], dedup); continue; }
            if (Math.Abs(db) <= onPlane) { AddUnique(points, vertices[b], dedup); continue; }

            var t = da / (da - db);
            AddUnique(points, Vec3.Lerp(vertices[a], vertices[b], t), dedup);
        }

        return points.Count < 3 ? null : OrderByAngle(points, normal);
    }

    /// <summary>用三个点定平面。返回 (平面上一点, 法向量)，三点共线时法向量为零。</summary>
    public static (Vec3 Point, Vec3 Normal) PlaneFrom(Vec3 a, Vec3 b, Vec3 c)
        => (a, Vec3.Cross(b - a, c - a));

    /// <summary>
    /// 从场景里解析出截面的交线多边形。
    /// 渲染、测量、MCP 工具都走这一个入口 —— 三处各算一遍迟早会不一致。
    /// </summary>
    public static IReadOnlyList<Vec3>? Of(Scene scene, GeoSection section)
    {
        if (scene.Find(section.SolidId) is not GeoPolyhedron solid) return null;

        var vertices = new Vec3[solid.VertexIds.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var point = scene.Point(solid.VertexIds[i]);
            if (point is null) return null;
            vertices[i] = point.Position;
        }

        var plane = new List<Vec3>(3);
        foreach (var id in section.PlanePointIds)
        {
            var point = scene.Point(id);
            if (point is null) return null;
            plane.Add(point.Position);
        }

        if (plane.Count < 3) return null;

        var (origin, normal) = PlaneFrom(plane[0], plane[1], plane[2]);
        return Of(vertices, solid.Faces, origin, normal);
    }

    private static IReadOnlyList<Vec3> OrderByAngle(List<Vec3> points, Vec3 normal)
    {
        var centroid = Vec3.Zero;
        foreach (var point in points) centroid += point;
        centroid /= points.Count;

        // 在平面内取一组正交基。种子方向要避开与法向量平行的方向，否则叉积退化。
        var seed = Math.Abs(normal.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
        var u = Vec3.Cross(normal, seed).Normalized();
        var v = Vec3.Cross(normal, u);

        return [.. points.OrderBy(p =>
            Math.Atan2(Vec3.Dot(p - centroid, v), Vec3.Dot(p - centroid, u)))];
    }

    private static void AddUnique(List<Vec3> points, Vec3 candidate, double tolerance)
    {
        foreach (var existing in points)
            if (Vec3.Distance(existing, candidate) <= tolerance)
                return;

        points.Add(candidate);
    }

    private static double Diagonal(IReadOnlyList<Vec3> vertices)
    {
        if (vertices.Count == 0) return 1;

        var min = vertices[0];
        var max = vertices[0];

        foreach (var v in vertices)
        {
            min = new Vec3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new Vec3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }

        var diagonal = Vec3.Distance(min, max);
        return diagonal > 0 ? diagonal : 1;
    }
}
