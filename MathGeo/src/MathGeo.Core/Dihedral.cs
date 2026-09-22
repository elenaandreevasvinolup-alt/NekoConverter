namespace MathGeo.Core;

/// <summary>二面角的平面角几何。</summary>
public sealed record DihedralGeometry(
    Vec3 Vertex,          // 棱上取的点（棱的中点）
    Vec3 DirectionA,      // 面 A 内垂直于棱的单位方向
    Vec3 DirectionB,      // 面 B 内垂直于棱的单位方向
    Vec3 EdgeDirection,   // 棱的单位方向
    double EdgeLength,
    double Degrees);

/// <summary>
/// 二面角。
///
/// 这是高中立体几何最典型的"怪物"：学生知道二面角是什么，但找不到平面角在哪。
/// 而平面角的定义完全是机械的 —— "在棱上取一点，分别在两个面内作垂直于棱的射线" ——
/// 正是应该交给软件做的事。老师要的是把它画出来，然后专心讲为什么这样构造。
/// </summary>
public static class Dihedral
{
    public static DihedralGeometry? Of(
        IReadOnlyList<Vec3> vertices, IReadOnlyList<int[]> faces, int faceA, int faceB)
    {
        if (faceA < 0 || faceA >= faces.Count) return null;
        if (faceB < 0 || faceB >= faces.Count) return null;
        if (faceA == faceB) return null;

        var a = faces[faceA];
        var b = faces[faceB];

        // 两个面必须有且只有一条公共棱。只共一个顶点（比如棱锥的两个侧面之外的组合）
        // 定不出二面角，直接返回 null 让上层报诊断。
        var shared = a.Intersect(b).ToArray();
        if (shared.Length != 2) return null;

        var p0 = vertices[shared[0]];
        var p1 = vertices[shared[1]];
        var edge = p1 - p0;
        var edgeLength = edge.Length;
        if (edgeLength < MathUtil.Epsilon) return null;

        var edgeDirection = edge / edgeLength;
        var vertex = (p0 + p1) * 0.5;

        var directionA = InFacePerpendicular(vertices, a, shared, edgeDirection, vertex);
        var directionB = InFacePerpendicular(vertices, b, shared, edgeDirection, vertex);
        if (directionA is null || directionB is null) return null;

        var cosine = MathUtil.Clamp(Vec3.Dot(directionA.Value, directionB.Value), -1, 1);

        return new DihedralGeometry(
            vertex, directionA.Value, directionB.Value, edgeDirection, edgeLength,
            MathUtil.RadToDeg(Math.Acos(cosine)));
    }

    /// <summary>在面内取"垂直于棱、指向面内部"的方向。</summary>
    private static Vec3? InFacePerpendicular(
        IReadOnlyList<Vec3> vertices, int[] face, int[] sharedEdge, Vec3 edgeDirection, Vec3 edgeMidpoint)
    {
        // 面上任意一个不在棱上的顶点，把它到棱的偏移去掉平行分量，就得到面内垂线方向。
        // 面是平的，所以取哪个顶点结果都一样（至多差一个正负号）。
        foreach (var index in face)
        {
            if (sharedEdge.Contains(index)) continue;

            var offset = vertices[index] - edgeMidpoint;
            var perpendicular = offset - edgeDirection * Vec3.Dot(offset, edgeDirection);

            if (perpendicular.Length > MathUtil.Epsilon)
                return perpendicular.Normalized();
        }

        return null;
    }

    /// <summary>
    /// 采样平面角的圆弧。委托给通用的角标注实现 —— 圆弧的采样逻辑只该有一份。
    /// </summary>
    public static IReadOnlyList<Vec3> ArcPoints(DihedralGeometry geometry, double radius, int samples = 48)
        => AngleMark.ArcPoints(geometry.Vertex, geometry.DirectionA, geometry.DirectionB, radius, samples);
}
