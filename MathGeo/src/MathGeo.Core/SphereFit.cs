namespace MathGeo.Core;

/// <summary>球的切接：外接球与内切球。</summary>
public static class SphereFit
{
    /// <summary>
    /// 顶点集的最小包围球（外接球）。
    ///
    /// 最小包围球的边界一定由 2、3 或 4 个点决定（两点定直径球、三点定外接圆球、
    /// 四点定外接球），所以枚举所有 2/3/4 点子集、取"能装下全部点里最小的那个"
    /// 就是精确解。教学体的顶点数是十几个，O(n⁴) 完全跑得动，不需要 Welzl 那套随机增量。
    /// </summary>
    public static (Vec3 Center, double Radius)? MinimumEnclosing(IReadOnlyList<Vec3> points)
    {
        if (points.Count == 0) return null;
        if (points.Count == 1) return (points[0], 0);

        // 容差按点集尺度取相对值：绝对阈值在很小的图形上会误判包含关系。
        var extent = Extent(points);
        var tolerance = extent * 1e-9;

        (Vec3 Center, double Radius)? best = null;

        void Consider(Vec3 center, double radius)
        {
            if (!double.IsFinite(radius) || radius < 0) return;

            foreach (var point in points)
                if (Vec3.Distance(point, center) > radius + tolerance)
                    return;

            if (best is null || radius < best.Value.Radius) best = (center, radius);
        }

        var count = points.Count;

        // 两点：直径球
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
                Consider((points[i] + points[j]) * 0.5, Vec3.Distance(points[i], points[j]) * 0.5);

        // 三点：外接圆
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
                for (var k = j + 1; k < count; k++)
                    if (Circumcircle(points[i], points[j], points[k], extent) is { } circle)
                        Consider(circle.Center, circle.Radius);

        // 四点：外接球
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
                for (var k = j + 1; k < count; k++)
                    for (var l = k + 1; l < count; l++)
                        if (Circumsphere(points[i], points[j], points[k], points[l], extent) is { } ball)
                            Consider(ball.Center, ball.Radius);

        return best;
    }

    /// <summary>
    /// 凸多面体的内切球。
    ///
    /// "内切"的定义是**与所有面相切**，不是"装得下"。这两者不一样：
    /// 正三棱柱里最大的球只贴到三个侧面，上下底离它还有一段距离 ——
    /// 那是"体里最大的球"，不是内切球。所以校验必须要求距离**等于**半径。
    ///
    /// 球心到每个面的距离都等于半径，所以把"到四个面的距离相等"列成 4×4 线性方程组，
    /// 枚举四面的组合、解出来、再逐个面检查相切。没有内切球的多面体会返回 null ——
    /// 这不是失败，是事实。
    /// </summary>
    public static (Vec3 Center, double Radius)? Inscribed(
        IReadOnlyList<Vec3> vertices, IReadOnlyList<int[]> faces)
    {
        var planes = FacePlanes(vertices, faces);
        if (planes.Count < 4) return null;

        var extent = Extent(vertices);
        var tolerance = extent * 1e-9;

        // 相切判定要松一点：解方程带来的误差比几何退化判定大。
        var tangentTolerance = extent * 1e-6;

        (Vec3 Center, double Radius)? best = null;

        for (var a = 0; a < planes.Count; a++)
            for (var b = a + 1; b < planes.Count; b++)
                for (var c = b + 1; c < planes.Count; c++)
                    for (var d = c + 1; d < planes.Count; d++)
                    {
                        var solution = SolveIncenter(
                            planes[a], planes[b], planes[c], planes[d], tolerance);

                        if (solution is not { } candidate) continue;
                        if (candidate.Radius <= tangentTolerance) continue;

                        // 必须与每一个面都相切 —— 只要有一个面离得明显更远，
                        // 这个球就不是内切球，而是"体里最大的球"。
                        var tangentToAll = true;
                        foreach (var plane in planes)
                        {
                            if (Math.Abs(plane.DistanceTo(candidate.Center) - candidate.Radius) > tangentTolerance)
                            {
                                tangentToAll = false;
                                break;
                            }
                        }

                        if (tangentToAll && (best is null || candidate.Radius > best.Value.Radius))
                            best = candidate;
                    }

        return best;
    }

    // ————————————————————————— 基本几何 —————————————————————————

    /// <summary>三点外接圆。三点共线时返回 null。</summary>
    private static (Vec3 Center, double Radius)? Circumcircle(
        Vec3 a, Vec3 b, Vec3 c, double extent)
    {
        var ab = b - a;
        var ac = c - a;
        var normal = Vec3.Cross(ab, ac);

        // 共线判定按"夹角的正弦"来 —— 那是个无量纲量，不受坐标系尺度影响。
        // 直接比叉积的长度是量纲错的（长度² 比长度）。
        if (normal.Length < extent * extent * 1e-9) return null;

        // 外心 = a + (|ac|²·(ab×n) + |ab|²·(n×ac)) / (2|n|²)
        var offset = (Vec3.Cross(ab, normal) * ac.LengthSquared
                      + Vec3.Cross(normal, ac) * ab.LengthSquared)
                     / (2 * normal.LengthSquared);

        var center = a + offset;
        return (center, Vec3.Distance(center, a));
    }

    /// <summary>四点外接球。四点共面时返回 null。</summary>
    private static (Vec3 Center, double Radius)? Circumsphere(
        Vec3 a, Vec3 b, Vec3 c, Vec3 d, double extent)
    {
        // 到四点等距 ⇔ 2·(b-a)·P = |b|² - |a|²，对 c、d 同理 —— 一个 3×3 线性方程组。
        var row1 = (b - a) * 2;
        var row2 = (c - a) * 2;
        var row3 = (d - a) * 2;

        var rhs = new Vec3(
            b.LengthSquared - a.LengthSquared,
            c.LengthSquared - a.LengthSquared,
            d.LengthSquared - a.LengthSquared);

        var determinant = Vec3.Dot(row1, Vec3.Cross(row2, row3));

        // 共面判定按相对量：行列式是长度³，阈值也要是长度³。
        if (Math.Abs(determinant) < extent * extent * extent * 1e-9) return null;

        // 克拉默法则要用**列**的三重积，不是行的：
        // "把第 k 列换成右端项之后的行列式" = b · (另两列的叉积)。
        // 用行的叉积在正方体、正四面体这类对称体上会碰巧算对，
        // 一般情形下会给出错误的球心 —— 而且错得很隐蔽，图看上去"也像个球"。
        var column0 = new Vec3(row1.X, row2.X, row3.X);
        var column1 = new Vec3(row1.Y, row2.Y, row3.Y);
        var column2 = new Vec3(row1.Z, row2.Z, row3.Z);

        var center = new Vec3(
            Vec3.Dot(rhs, Vec3.Cross(column1, column2)) / determinant,
            Vec3.Dot(rhs, Vec3.Cross(column2, column0)) / determinant,
            Vec3.Dot(rhs, Vec3.Cross(column0, column1)) / determinant);

        return (center, Vec3.Distance(center, a));
    }

    /// <summary>面所在平面：外向单位法向量 + 偏移（平面上的点满足 dot(n, p) = d）。</summary>
    private readonly record struct Plane(Vec3 Normal, double Offset)
    {
        /// <summary>点到平面的距离。法向朝外，所以内部为正。</summary>
        public double DistanceTo(Vec3 point) => Offset - Vec3.Dot(Normal, point);
    }

    private static List<Plane> FacePlanes(IReadOnlyList<Vec3> vertices, IReadOnlyList<int[]> faces)
    {
        var planes = new List<Plane>(faces.Count);

        foreach (var face in faces)
        {
            if (face.Length < 3) continue;

            var a = vertices[face[0]];
            var normal = Vec3.Cross(vertices[face[1]] - a, vertices[face[2]] - a);

            // 绕向约定是"从体外看逆时针"，所以叉积朝外。退化面直接跳过。
            if (normal.Length < MathUtil.Epsilon) continue;

            normal = normal.Normalized();
            planes.Add(new Plane(normal, Vec3.Dot(normal, a)));
        }

        return planes;
    }

    /// <summary>
    /// 解"到四个面距离相等"的球心与半径。
    /// 未知量是 (Px, Py, Pz, r)，方程是 dot(nᵢ, P) + r = dᵢ。
    /// </summary>
    private static (Vec3 Center, double Radius)? SolveIncenter(
        Plane a, Plane b, Plane c, Plane d, double tolerance)
    {
        double[,] matrix =
        {
            { a.Normal.X, a.Normal.Y, a.Normal.Z, 1, a.Offset },
            { b.Normal.X, b.Normal.Y, b.Normal.Z, 1, b.Offset },
            { c.Normal.X, c.Normal.Y, c.Normal.Z, 1, c.Offset },
            { d.Normal.X, d.Normal.Y, d.Normal.Z, 1, d.Offset },
        };

        return Solve4(matrix, tolerance);
    }

    /// <summary>高斯消元（列主元）解 4 元线性方程组。matrix 的第 5 列是右端项。</summary>
    private static (Vec3 Center, double Radius)? Solve4(double[,] matrix, double tolerance)
    {
        const int size = 4;

        for (var column = 0; column < size; column++)
        {
            // 列主元：教学体里出现病态矩阵时，不选主元会放大误差。
            var pivot = column;
            for (var row = column + 1; row < size; row++)
                if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column]))
                    pivot = row;

            if (Math.Abs(matrix[pivot, column]) < tolerance) return null;

            if (pivot != column)
                for (var k = column; k <= size; k++)
                    (matrix[column, k], matrix[pivot, k]) = (matrix[pivot, k], matrix[column, k]);

            for (var row = column + 1; row < size; row++)
            {
                var factor = matrix[row, column] / matrix[column, column];
                for (var k = column; k <= size; k++)
                    matrix[row, k] -= factor * matrix[column, k];
            }
        }

        var solution = new double[size];

        for (var row = size - 1; row >= 0; row--)
        {
            var sum = matrix[row, size];
            for (var k = row + 1; k < size; k++)
                sum -= matrix[row, k] * solution[k];

            solution[row] = sum / matrix[row, row];
        }

        var center = new Vec3(solution[0], solution[1], solution[2]);
        if (!double.IsFinite(center.X) || !double.IsFinite(solution[3])) return null;

        return (center, solution[3]);
    }

    private static double Extent(IReadOnlyList<Vec3> points)
    {
        if (points.Count == 0) return 1;

        var min = points[0];
        var max = points[0];

        foreach (var point in points)
        {
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        var extent = Vec3.Distance(min, max);
        return extent > 0 ? extent : 1;
    }

    // ————————————————————————— 球面线框 —————————————————————————

    /// <summary>
    /// 球面线框：一条赤道 + 若干条经线。
    ///
    /// 为什么不画实心球面：教学图要的是"看见球在哪、球心在哪"，
    /// 一个半透明实体只会把体挡住。赤道加经线是课本画球的标准做法，
    /// 而且每条线都能参与隐藏线判定，被体挡住的部分自动变成虚线。
    /// </summary>
    public static IReadOnlyList<Vec3[]> Wireframe(Vec3 center, double radius, int meridians, int samples = 64)
    {
        var curves = new List<Vec3[]>(meridians + 1)
        {
            GreatCircle(center, radius, Vec3.UnitY, samples),   // 赤道
        };

        // 经线的法向量都躺在水平面里，于是每条都穿过南北极。
        for (var m = 0; m < meridians; m++)
        {
            var angle = Math.PI * m / meridians;
            curves.Add(GreatCircle(center, radius, new Vec3(Math.Cos(angle), 0, Math.Sin(angle)), samples));
        }

        return curves;
    }

    private static Vec3[] GreatCircle(Vec3 center, double radius, Vec3 normal, int samples)
    {
        var axis = normal.Normalized();

        // 在垂直于法向量的平面里取一组正交基。种子方向要避开与法向量平行的方向。
        var seed = Math.Abs(axis.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
        var u = Vec3.Cross(axis, seed).Normalized();
        var v = Vec3.Cross(axis, u);

        var points = new Vec3[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var t = 2 * Math.PI * i / samples;
            points[i] = center + (u * Math.Cos(t) + v * Math.Sin(t)) * radius;
        }

        return points;
    }
}
