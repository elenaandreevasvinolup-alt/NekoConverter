namespace MathGeo.Core.Tests;

/// <summary>
/// 截面与测量。
///
/// 这组测试的价值在于"答案是可以被内核自己验证的"：
/// 正方体过中心、垂直于体对角线的截面必须算出 6 条等长边和 12√3 的面积 ——
/// 这是课本上的结论，内核算出来一致，说明几何是真的对。
/// </summary>
public class CrossSectionTests
{
    private static Scene Cube()
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        scene.Camera = Camera.Textbook;
        scene.Solve();
        return scene;
    }

    private static GeoSection AddSection(Scene scene, params string[] planePoints)
    {
        var section = GeoSection.Of("sec", "cube", planePoints);
        scene.Add(section);
        scene.Solve();
        return section;
    }

    private static IReadOnlyList<Vec3> Polygon(Scene scene, GeoSection section)
        => CrossSection.Of(scene, section) ?? [];

    [Fact]
    public void 过中心且垂直于体对角线的截面是正六边形()
    {
        var scene = Cube();

        // 棱 BC、CD、A₁B₁ 的中点定出的平面过正方体中心
        scene.Add(GeoPoint.Derived("M1", DerivedKind.Midpoint, "cube.B", "cube.C"));
        scene.Add(GeoPoint.Derived("M2", DerivedKind.Midpoint, "cube.A1", "cube.B1"));
        scene.Add(GeoPoint.Derived("M3", DerivedKind.Midpoint, "cube.C", "cube.D"));
        scene.Solve();

        var polygon = Polygon(scene, AddSection(scene, "M1", "M2", "M3"));

        Assert.Equal(6, polygon.Count);

        for (var i = 0; i < polygon.Count; i++)
        {
            var next = (i + 1) % polygon.Count;
            Approx.Scalar(2 * Math.Sqrt(2), Vec3.Distance(polygon[i], polygon[next]), 1e-9);
        }

        Approx.Scalar(12 * Math.Sqrt(3), Measure.PolygonArea(polygon), 1e-9);
    }

    [Fact]
    public void 过三个两两不相邻顶点的截面是正三角形()
    {
        var scene = Cube();
        var polygon = Polygon(scene, AddSection(scene, "cube.A", "cube.C", "cube.B1"));

        Assert.Equal(3, polygon.Count);

        // A、C、B₁ 两两之间都是面对角线 = 4√2
        for (var i = 0; i < 3; i++)
            Approx.Scalar(4 * Math.Sqrt(2), Vec3.Distance(polygon[i], polygon[(i + 1) % 3]), 1e-9);

        Approx.Scalar(8 * Math.Sqrt(3), Measure.PolygonArea(polygon), 1e-9);
    }

    [Fact]
    public void 过一条棱和对棱中点的截面是矩形()
    {
        var scene = Cube();

        // 过棱 AA₁ 与棱 BC 的中点
        scene.Add(GeoPoint.Derived("M", DerivedKind.Midpoint, "cube.B", "cube.C"));
        scene.Solve();

        var polygon = Polygon(scene, AddSection(scene, "cube.A", "cube.A1", "M"));

        // 截面是矩形：两条边是棱长 4，另两条是 √(4² + 2²) = 2√5。
        // 注意棱 AA₁ 整条躺在平面上，被当作"无数交点"跳过了 —— 这条断言同时覆盖那个分支。
        Assert.Equal(4, polygon.Count);
        Approx.Scalar(8 * Math.Sqrt(5), Measure.PolygonArea(polygon), 1e-9);
    }

    [Fact]
    public void 平面与体不相交时没有截面()
    {
        var scene = Cube();

        scene.Add(GeoPoint.Free("P1", new Vec3(100, 100, 100)));
        scene.Add(GeoPoint.Free("P2", new Vec3(101, 100, 100)));
        scene.Add(GeoPoint.Free("P3", new Vec3(100, 101, 100)));
        scene.Solve();

        Assert.Null(CrossSection.Of(scene, AddSection(scene, "P1", "P2", "P3")));
    }

    [Fact]
    public void 三点共线时定不出平面()
    {
        var scene = Cube();

        scene.Add(GeoPoint.Free("P1", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("P2", new Vec3(1, 0, 0)));
        scene.Add(GeoPoint.Free("P3", new Vec3(2, 0, 0)));
        scene.Solve();

        Assert.Null(CrossSection.Of(scene, AddSection(scene, "P1", "P2", "P3")));
    }

    [Fact]
    public void 截面在显示列表里用独立颜色且叠在最上层()
    {
        var scene = Cube();
        AddSection(scene, "cube.A", "cube.C", "cube.B1");

        var shapes = scene.BuildDisplayList().ToList();
        var section = shapes.OfType<PolygonShape>().Single(p => p.Stroke == Theme.Textbook.SectionStroke);

        Assert.Equal(3, section.Points.Count);

        // 截面是"答案"，必须画在所有的面与棱之后，否则会被半透明的面盖住看不清交线。
        var lastEdge = shapes.FindLastIndex(s => s is LineShape);
        Assert.True(shapes.IndexOf(section) > lastEdge, "截面应当画在棱之后");
    }

    [Fact]
    public void 截面跟着相机转_顶点数不变()
    {
        var scene = Cube();
        AddSection(scene, "cube.A", "cube.C", "cube.B1");

        // 旋转过程中截面形状（顶点数、面积）必须稳定，不能因为投影角度变化而抖。
        for (var azimuth = 0.0; azimuth < 360; azimuth += 30)
        {
            scene.Camera = Camera.Textbook.Orbit(azimuth, 0);
            scene.Solve();

            var section = (GeoSection)scene.Find("sec")!;
            var polygon = CrossSection.Of(scene, section)!;

            Assert.Equal(3, polygon.Count);
            Approx.Scalar(8 * Math.Sqrt(3), Measure.PolygonArea(polygon), 1e-9);
        }
    }

    // ————————————————————————————— 测量 —————————————————————————————

    [Fact]
    public void 测量线段长度()
    {
        var scene = Cube();
        scene.Add(GeoLine.Segment("ac1", "cube.A", "cube.C1"));
        scene.Solve();

        Approx.Scalar(4 * Math.Sqrt(3), Measure.Length(scene, "ac1")!.Value, 1e-9);
    }

    [Fact]
    public void 测量多边形面积与周长()
    {
        var scene = Cube();
        scene.Add(GeoPolygon.Of("bottom", "cube.A", "cube.B", "cube.C", "cube.D"));
        scene.Solve();

        Approx.Scalar(16, Measure.Area(scene, "bottom")!.Value, 1e-9);
        Approx.Scalar(16, Measure.Perimeter(scene, "bottom")!.Value, 1e-9);
    }

    [Fact]
    public void 测量圆的面积()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("O", Vec3.Zero));
        scene.Add(GeoCircle.Fixed("c", "O", 3));
        scene.Solve();

        Approx.Scalar(Math.PI * 9, Measure.Area(scene, "c")!.Value, 1e-9);
    }

    [Fact]
    public void 三维平面多边形面积用Newell法()
    {
        // 空间里的一张平面上的直角三角形，不依赖它平行于哪个坐标面
        Approx.Scalar(6, Measure.PolygonArea([new Vec3(0, 0, 0), new Vec3(3, 0, 0), new Vec3(0, 4, 0)]), 1e-12);

        // 倾斜平面上的 2×2 正方形：两条边分别沿 (√2,√2,0)/2 与 (0,0,1)，面积仍然是 4
        var sqrt2 = Math.Sqrt(2);
        Approx.Scalar(4, Measure.PolygonArea([
            new Vec3(0, 0, 0),
            new Vec3(sqrt2, sqrt2, 0),
            new Vec3(sqrt2, sqrt2, 2),
            new Vec3(0, 0, 2),
        ]), 1e-12);
    }

    [Fact]
    public void 测不了就返回null而不是零()
    {
        var scene = Cube();
        scene.Solve();

        Assert.Null(Measure.Length(scene, "cube"));        // 多面体没有"长度"
        Assert.Null(Measure.Length(scene, "不存在"));
        Assert.Null(Measure.Area(scene, "cube.A"));        // 点没有面积
    }

    [Fact]
    public void 截面样例算出正六边形()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "06-hexagon-section.problem.json");
        var file = ProblemFile.FromJson(File.ReadAllText(path))!;
        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var section = (GeoSection)result.Scene.Find("sec")!;
        var polygon = CrossSection.Of(result.Scene, section)!;

        Assert.Equal(6, polygon.Count);
        Approx.Scalar(12 * Math.Sqrt(3), Measure.PolygonArea(polygon), 1e-9);
    }
}
