namespace MathGeo.Core.Tests;

/// <summary>
/// 二面角。
///
/// 断言的都是课本上写死的结论：正方体相邻面 90°、正四面体任意两面 arccos(1/3)。
/// 内核算出来和课本一致，说明"平面角"这个构造是真的做对了，而不是凑出了一个像样的数字。
/// </summary>
public class DihedralTests
{
    private const double RegularTetrahedronEdge = 3;

    /// <summary>棱长 3 的正四面体：底面外接圆半径 √3，高 √6。</summary>
    private static SolidExpansion Tetrahedron()
        => SolidPresets.Pyramid("tetra", 3, Math.Sqrt(3), Math.Sqrt(6));

    private static Vec3[] Positions(SolidExpansion expansion)
        => [.. expansion.Vertices.Select(p => p.FreePosition)];

    [Fact]
    public void 正四面体的任意两面二面角是arccos三分之一()
    {
        var expansion = Tetrahedron();
        var vertices = Positions(expansion);
        var expected = MathUtil.RadToDeg(Math.Acos(1.0 / 3));

        // 四个面两两相邻，任意一对都该是这个值
        for (var a = 0; a < 4; a++)
            for (var b = a + 1; b < 4; b++)
            {
                var geometry = Dihedral.Of(vertices, expansion.Solid.Faces, a, b);
                Assert.NotNull(geometry);
                Approx.Scalar(expected, geometry!.Degrees, 1e-9);
            }

        Approx.Scalar(70.528779, expected, 1e-6);
    }

    [Fact]
    public void 正四面体的棱长与设定一致()
    {
        var expansion = Tetrahedron();
        var vertices = Positions(expansion);

        // 底边
        for (var i = 0; i < 3; i++)
            Approx.Scalar(RegularTetrahedronEdge, Vec3.Distance(vertices[i], vertices[(i + 1) % 3]), 1e-6);

        // 侧棱
        for (var i = 0; i < 3; i++)
            Approx.Scalar(RegularTetrahedronEdge, Vec3.Distance(vertices[3], vertices[i]), 1e-6);
    }

    [Fact]
    public void 正方体相邻面的二面角都是90度()
    {
        var expansion = SolidPresets.Box("cube", 4, 4, 4);
        var vertices = Positions(expansion);
        var adjacentPairs = 0;

        for (var a = 0; a < expansion.Solid.Faces.Count; a++)
            for (var b = a + 1; b < expansion.Solid.Faces.Count; b++)
            {
                var geometry = Dihedral.Of(vertices, expansion.Solid.Faces, a, b);
                if (geometry is null) continue;   // 相对的两个面没有公共棱

                adjacentPairs++;
                Approx.Scalar(90, geometry.Degrees, 1e-9);
            }

        // 正方体 6 个面，12 对相邻
        Assert.Equal(12, adjacentPairs);
    }

    [Fact]
    public void 没有公共棱的两个面求不出二面角()
    {
        var expansion = SolidPresets.Box("cube", 4, 4, 4);
        var vertices = Positions(expansion);

        // 下底与上底平行，没有公共棱
        Assert.Null(Dihedral.Of(vertices, expansion.Solid.Faces, 0, 1));
    }

    [Fact]
    public void 同一个面或者越界的下标都返回空()
    {
        var expansion = Tetrahedron();
        var vertices = Positions(expansion);
        var faces = expansion.Solid.Faces;

        Assert.Null(Dihedral.Of(vertices, faces, 0, 0));
        Assert.Null(Dihedral.Of(vertices, faces, 0, 99));
        Assert.Null(Dihedral.Of(vertices, faces, -1, 0));
    }

    [Fact]
    public void 平面角的圆弧落在以棱中点为心的圆上()
    {
        var expansion = Tetrahedron();
        var geometry = Dihedral.Of(Positions(expansion), expansion.Solid.Faces, 0, 1)!;

        const double radius = 1.25;
        var arc = Dihedral.ArcPoints(geometry, radius);

        Assert.True(arc.Count > 10, "圆弧应当被采样成多段折线");
        foreach (var point in arc)
            Approx.Scalar(radius, Vec3.Distance(point, geometry.Vertex), 1e-9);

        // 起点落在方向 A 上，终点落在方向 B 上
        Approx.Vec(geometry.Vertex + geometry.DirectionA * radius, arc[0], 1e-9);
        Approx.Vec(geometry.Vertex + geometry.DirectionB * radius, arc[^1], 1e-9);
    }

    [Fact]
    public void 两个方向平行时圆弧退化成两个端点()
    {
        // 二面角 0° 的退化情形：不能在这里除以零。
        var flat = new DihedralGeometry(
            Vec3.Zero, Vec3.UnitX, Vec3.UnitX, Vec3.UnitY, 1, 0);

        var arc = Dihedral.ArcPoints(flat, 1);

        Assert.Equal(2, arc.Count);
        Approx.Vec(new Vec3(1, 0, 0), arc[0]);
        Approx.Vec(new Vec3(1, 0, 0), arc[1]);
    }

    [Fact]
    public void 直角用直角符号而不是写90度()
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var p in expansion.Vertices) scene.Add(p);
        scene.Add(expansion.Solid);
        scene.Add(GeoDihedral.Of("d", "cube", 0, 3));   // 下底与右面
        scene.Camera = Camera.Textbook;
        scene.Solve();

        var shapes = scene.BuildDisplayList();

        var angleLines = shapes.OfType<LineShape>()
            .Where(l => l.Stroke == Theme.Textbook.AngleStroke).ToList();
        var angleLabels = shapes.OfType<LabelShape>()
            .Where(l => l.Fill == Theme.Textbook.AngleStroke).ToList();

        Assert.Equal(2, angleLines.Count);   // 直角符号是两笔
        Assert.Empty(angleLabels);           // 不写 90°
    }

    [Fact]
    public void 非直角画圆弧并标注度数()
    {
        var scene = new Scene();
        var expansion = Tetrahedron();

        foreach (var p in expansion.Vertices) scene.Add(p);
        scene.Add(expansion.Solid);
        scene.Add(GeoDihedral.Of("d", "tetra", 0, 1));
        scene.Camera = Camera.Textbook;
        scene.Solve();

        var shapes = scene.BuildDisplayList();

        var angleLines = shapes.OfType<LineShape>()
            .Where(l => l.Stroke == Theme.Textbook.AngleStroke).ToList();
        var label = shapes.OfType<LabelShape>()
            .Single(l => l.Fill == Theme.Textbook.AngleStroke);

        Assert.True(angleLines.Count > 10, "非直角应当画成圆弧");
        Assert.Equal("70.5°", label.Text);
    }

    [Fact]
    public void 关掉数值标注后不出现度数标签()
    {
        var scene = new Scene();
        var expansion = Tetrahedron();

        foreach (var p in expansion.Vertices) scene.Add(p);
        scene.Add(expansion.Solid);

        var dihedral = GeoDihedral.Of("d", "tetra", 0, 1);
        dihedral.ShowValue = false;
        scene.Add(dihedral);

        scene.Camera = Camera.Textbook;
        scene.Solve();

        Assert.DoesNotContain(
            scene.BuildDisplayList().OfType<LabelShape>(),
            l => l.Fill == Theme.Textbook.AngleStroke);
    }

    [Fact]
    public void 二面角样例算出70_53度()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "07-dihedral.problem.json");
        var file = ProblemFile.FromJson(File.ReadAllText(path))!;
        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var dihedral = (GeoDihedral)result.Scene.Find("dihedral")!;
        var solid = (GeoPolyhedron)result.Scene.Find("tetra")!;

        var vertices = solid.VertexIds.Select(id => result.Scene.Point(id)!.Position).ToArray();
        var geometry = Dihedral.Of(vertices, solid.Faces, dihedral.FaceA, dihedral.FaceB)!;

        Approx.Scalar(MathUtil.RadToDeg(Math.Acos(1.0 / 3)), geometry.Degrees, 1e-6);
        Approx.Scalar(3, geometry.EdgeLength, 1e-6);
    }
}
