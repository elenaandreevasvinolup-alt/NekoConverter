namespace MathGeo.Core.Tests;

/// <summary>
/// 球的切接。
///
/// 断言的都是课本上写死的结论：正方体 R = (√3/2)a、r = a/2；
/// 正四面体 R = (√6/4)a、r = (√6/12)a。算出来一致，说明球心和半径是真的解对了，
/// 而不是凑了一个"看起来像"的数字。
/// </summary>
public class SphereTests
{
    /// <summary>棱长 3 的正四面体：底面外接圆半径 √3，高 √6。</summary>
    private static SolidExpansion Tetrahedron() => SolidPresets.Pyramid("t", 3, Math.Sqrt(3), Math.Sqrt(6));

    private static Vec3[] Positions(SolidExpansion expansion)
        => [.. expansion.Vertices.Select(p => p.FreePosition)];

    /// <summary>点到面的距离。面用前三个顶点定，绕向约定朝外。</summary>
    private static double DistanceToFace(Vec3 point, Vec3 a, Vec3 b, Vec3 c)
    {
        var normal = Vec3.Cross(b - a, c - a).Normalized();
        return Math.Abs(Vec3.Dot(point - a, normal));
    }

    // ————————————————————————— 算法 —————————————————————————

    [Fact]
    public void 参数四舍五入后仍然求得出外接球()
    {
        // 样例文件里写的是 1.7320508 / 2.4494897 这样的近似值（人是不会手打 √3 的），
        // 于是正四面体变成了"几乎正"的四面体。外接球依然存在 ——
        // 任意四个不共面的点都有唯一外接球，算法不该因为"不够正"就放弃。
        var expansion = SolidPresets.Pyramid("tetra", 3, 1.7320508, 2.4494897);
        var fit = SphereFit.MinimumEnclosing(Positions(expansion));

        Assert.NotNull(fit);
        Approx.Scalar(1.837117, fit!.Value.Radius, 1e-5);
    }

    [Fact]
    public void 正方体的外接球半径是体对角线的一半()
    {
        var cube = SolidPresets.Box("cube", 4, 4, 4);
        var fit = SphereFit.MinimumEnclosing(Positions(cube));

        Assert.NotNull(fit);

        Approx.Vec(new Vec3(0, 2, 0), fit!.Value.Center);
        Approx.Scalar(2 * Math.Sqrt(3), fit.Value.Radius, 1e-9);
    }

    [Fact]
    public void 外接球过所有顶点()
    {
        var cube = SolidPresets.Box("cube", 4, 4, 4);
        var vertices = Positions(cube);
        var fit = SphereFit.MinimumEnclosing(vertices)!.Value;

        foreach (var vertex in vertices)
            Approx.Scalar(fit.Radius, Vec3.Distance(vertex, fit.Center), 1e-9);
    }

    [Fact]
    public void 正方体的内切球半径是棱长的一半()
    {
        var cube = SolidPresets.Box("cube", 4, 4, 4);
        var fit = SphereFit.Inscribed(Positions(cube), cube.Solid.Faces);

        Assert.NotNull(fit);

        Approx.Vec(new Vec3(0, 2, 0), fit!.Value.Center);
        Approx.Scalar(2, fit.Value.Radius, 1e-9);
    }

    [Fact]
    public void 内切球与所有面相切()
    {
        var cube = SolidPresets.Box("cube", 4, 4, 4);
        var vertices = Positions(cube);
        var fit = SphereFit.Inscribed(vertices, cube.Solid.Faces)!.Value;

        foreach (var face in cube.Solid.Faces)
            Approx.Scalar(fit.Radius,
                DistanceToFace(fit.Center, vertices[face[0]], vertices[face[1]], vertices[face[2]]), 1e-9);
    }

    [Fact]
    public void 正四面体的外接球半径是四分之根号六乘棱长()
    {
        var tetra = Tetrahedron();
        var fit = SphereFit.MinimumEnclosing(Positions(tetra));

        Assert.NotNull(fit);
        Approx.Scalar(3 * Math.Sqrt(6) / 4, fit!.Value.Radius, 1e-9);
    }

    [Fact]
    public void 正四面体的内切球半径是十二分之根号六乘棱长()
    {
        var tetra = Tetrahedron();
        var fit = SphereFit.Inscribed(Positions(tetra), tetra.Solid.Faces);

        Assert.NotNull(fit);
        Approx.Scalar(3 * Math.Sqrt(6) / 12, fit!.Value.Radius, 1e-9);
    }

    [Fact]
    public void 正四面体的内外接球同心()
    {
        // 正四面体是中心对称性很强的体：内外接球共用球心。这条能同时验证两个算法。
        var tetra = Tetrahedron();
        var vertices = Positions(tetra);

        var outer = SphereFit.MinimumEnclosing(vertices)!.Value;
        var inner = SphereFit.Inscribed(vertices, tetra.Solid.Faces)!.Value;

        Approx.Vec(outer.Center, inner.Center, 1e-9);

        // 外接球半径是内切球半径的三倍
        Approx.Scalar(3, outer.Radius / inner.Radius, 1e-9);
    }

    [Fact]
    public void 没有内切球的多面体返回空而不是硬凑一个()
    {
        // 正三棱柱：要让球同时贴到上下底和三个侧面，高必须等于底面内切圆直径。
        // 半径 2、高 3 时高不匹配，所以它没有内切球 —— 这是事实，不是算法失败。
        var prism = SolidPresets.Prism("p", 3, 2, 3);

        Assert.Null(SphereFit.Inscribed(Positions(prism), prism.Solid.Faces));
    }

    [Fact]
    public void 顶点太少时求不出球()
    {
        Assert.Null(SphereFit.MinimumEnclosing([]));
        Assert.Null(SphereFit.Inscribed([new Vec3(0, 0, 0), new Vec3(1, 0, 0)], []));
    }

    // ————————————————————————— 渲染 —————————————————————————

    private static Scene SphereScene(SphereKind kind, out GeoSphere sphere)
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        sphere = GeoSphere.Of("ball", "cube", kind);
        scene.Add(sphere);

        scene.Camera = Camera.Textbook;
        scene.Solve();
        return scene;
    }

    [Fact]
    public void 球面线框是赤道加若干经线()
    {
        var curves = SphereFit.Wireframe(Vec3.Zero, 2, 3);

        Assert.Equal(4, curves.Count);                        // 赤道 + 3 条经线
        Assert.All(curves, c => Assert.Equal(65, c.Length));  // 每条 64 段

        // 所有采样点都落在球面上
        foreach (var point in curves.SelectMany(c => c))
            Approx.Scalar(2, point.Length, 1e-9);
    }

    [Fact]
    public void 球面线框被体挡住的部分画成虚线()
    {
        var scene = SphereScene(SphereKind.Circumscribed, out var sphere);

        Assert.True(sphere.IsSolved);

        var lines = scene.BuildDisplayList().OfType<LineShape>().ToList();

        // 外接球比立方体大，一定有露在外面的部分（实线）和被体挡住的部分（虚线）
        Assert.Contains(lines, l => l.Dash is null);
        Assert.Contains(lines, l => l.Dash is { Length: > 0 });
    }

    [Fact]
    public void 球心与半径都画出来()
    {
        var scene = SphereScene(SphereKind.Circumscribed, out var sphere);
        var shapes = scene.BuildDisplayList();

        // 球心圆点 + 半径线段 + 球心标签 + 半径数值，都用强调色
        Assert.Contains(shapes.OfType<DotShape>(), d => d.Fill == Theme.Textbook.HighlightStroke);
        Assert.Contains(shapes.OfType<LineShape>(), l => l.Stroke == Theme.Textbook.HighlightStroke);
        Assert.Contains(shapes.OfType<LabelShape>(),
            l => l.Fill == Theme.Textbook.HighlightStroke && l.Text.StartsWith('R'));
    }

    [Fact]
    public void 内切球的半径线段垂直于面而不是连到顶点()
    {
        // 内切球连到顶点就比半径长了 —— 那样画出来的图会直接教错。
        var scene = SphereScene(SphereKind.Inscribed, out var sphere);
        var shapes = scene.BuildDisplayList();

        var radiusLine = shapes.OfType<LineShape>()
            .Single(l => l.Stroke == Theme.Textbook.HighlightStroke);

        // 半径线段的长度就是半径（在正交投影下不会超过半径）
        var projected = Vec2.Distance(radiusLine.A, radiusLine.B);
        Assert.True(projected <= sphere.Radius * 1.001,
            $"半径线段投影长度 {projected} 不该超过半径 {sphere.Radius}");
    }

    [Fact]
    public void 求不出球时给出诊断而不是画一个半径零的球()
    {
        var scene = new Scene();
        var prism = SolidPresets.Prism("p", 3, 2, 3);

        foreach (var vertex in prism.Vertices) scene.Add(vertex);
        scene.Add(prism.Solid);

        var sphere = GeoSphere.Of("ball", "p", SphereKind.Inscribed);
        scene.Add(sphere);

        scene.Camera = Camera.Textbook;
        var result = scene.Solve();

        Assert.False(sphere.IsSolved);
        Assert.Contains(result.Diagnostics, d => d.Code == "geom.sphere.inscribed");
    }

    [Fact]
    public void 球样例算出外接球二倍根号三与内切球二()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "10-spheres.problem.json");
        var file = ProblemFile.FromJson(File.ReadAllText(path))!;
        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var outer = (GeoSphere)result.Scene.Find("outer")!;
        var inner = (GeoSphere)result.Scene.Find("inner")!;

        Assert.True(outer.IsSolved);
        Assert.True(inner.IsSolved);

        Approx.Scalar(2 * Math.Sqrt(3), outer.Radius, 1e-6);
        Approx.Scalar(2, inner.Radius, 1e-6);
        Approx.Vec(outer.Center, inner.Center, 1e-9);   // 正方体的内外接球同心
    }
}
