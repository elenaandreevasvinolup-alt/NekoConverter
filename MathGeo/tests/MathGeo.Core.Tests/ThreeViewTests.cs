namespace MathGeo.Core.Tests;

/// <summary>
/// 三视图。
///
/// 这组测试盯的是两件事：
///   · 版面关系（长对正、高平齐）自动成立 —— 不靠手工对齐；
///   · 正交投影下的隐藏线判定正确 —— 正对一个面时应当恰好 8 条虚线。
///
/// 视图名称是唯一能区分三个版面的标记。它们用 HelperStroke 上色，
/// 而顶点标签用 LabelFill，所以可以靠颜色把它们挑出来，不必依赖界面语言。
/// </summary>
public class ThreeViewTests
{
    private static Scene CubeScene(bool withThreeViews = true, bool showNames = true)
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        if (withThreeViews)
            scene.Add(new GeoThreeViews { Id = "views", SolidIds = ["cube"], ShowNames = showNames });

        scene.Camera = Camera.Textbook;
        scene.Solve();
        return scene;
    }

    private static List<LabelShape> ViewNames(Scene scene)
        => [.. scene.BuildDisplayList().OfType<LabelShape>()
            .Where(l => l.Fill == Theme.Textbook.HelperStroke)];

    [Fact]
    public void 三个视图各画出八条棱()
    {
        var scene = CubeScene();

        var lines = scene.BuildDisplayList().OfType<LineShape>().Count();

        // 3D 立方体 12 条 + 正、侧、俯三个视图各 8 条。
        // 每个视图只有 8 条而不是 12 条：立方体有 4 条棱沿视线方向，
        // 投影后退化成一个点，不该被画出来。
        Assert.Equal(36, lines);
    }

    [Fact]
    public void 三视图是线框图_不带面填充()
    {
        var scene = CubeScene();

        // 只有 3D 那个立方体有面。投影图填了色就看不清棱了。
        Assert.Equal(6, scene.BuildDisplayList().OfType<PolygonShape>().Count());
    }

    [Fact]
    public void 每个正交视图的立方体有四条隐藏棱()
    {
        var scene = CubeScene();

        var dashed = scene.BuildDisplayList().OfType<LineShape>()
            .Count(l => l.Dash is { Length: > 0 });

        // 3D 一般视角 3 条 + 三个正交视图各 4 条 = 15。
        // 正对一个面时看不见的棱有 8 条，其中 4 条沿视线方向退化成点，所以只画 4 条。
        Assert.Equal(15, dashed);
    }

    [Fact]
    public void 长对正与高平齐自动成立()
    {
        var scene = CubeScene();
        var names = ViewNames(scene);

        Assert.Equal(3, names.Count);

        // 靠位置而不是文字区分三个视图：右边的是侧视图，最下面的是俯视图。
        var side = names.MaxBy(l => l.At.X)!;
        var top = names.MinBy(l => l.At.Y)!;
        var front = names.Except([side, top]).Single();

        // 长对正：正视图与俯视图的横向中线对齐
        Approx.Scalar(front.At.X, top.At.X, 1e-9);

        // 高平齐：正视图与侧视图的纵向位置对齐
        Approx.Scalar(front.At.Y, side.At.Y, 1e-9);

        // 侧视图在正视图右边，俯视图在正视图下面
        Assert.True(side.At.X > front.At.X);
        Assert.True(top.At.Y < front.At.Y);
    }

    [Fact]
    public void 版面偏移能把三视图挪到立体图旁边()
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        scene.Add(new GeoThreeViews
        {
            Id = "views",
            SolidIds = ["cube"],
            Offset = new Vec2(40, 0),
        });

        scene.Camera = Camera.Textbook;
        scene.Solve();

        // 整个三视图版面都该被推到 x > 30，不会压在立体图上
        var names = ViewNames(scene);
        Assert.Equal(3, names.Count);
        Assert.All(names, l => Assert.True(l.At.X > 30, $"视图名称在 x={l.At.X}，没有被偏移"));

        // 三视图的棱也全在 x > 30。注意立体图自己的棱还在原点附近，
        // 所以这里按"有没有落在左边的线"来判断，而不是取全局最小值。
        var lines = scene.BuildDisplayList().OfType<LineShape>().ToList();
        Assert.Contains(lines, l => l.A.X > 30 && l.B.X > 30);
        Assert.Contains(lines, l => l.A.X < 0 && l.B.X < 0);
    }

    [Fact]
    public void 可以关掉视图名称()
    {
        var scene = CubeScene(showNames: false);

        Assert.Empty(ViewNames(scene));

        // 关掉名称不该影响图形本身
        Assert.Equal(36, scene.BuildDisplayList().OfType<LineShape>().Count());
    }

    [Fact]
    public void 只投影点名的体()
    {
        var scene = new Scene();

        var cube = SolidPresets.Box("cube", 4, 4, 4);
        var other = SolidPresets.Box("other", 2, 2, 2, new Vec3(20, 0, 0));

        foreach (var p in cube.Vertices) scene.Add(p);
        foreach (var p in other.Vertices) scene.Add(p);
        scene.Add(cube.Solid);
        scene.Add(other.Solid);

        scene.Add(GeoThreeViews.Of("views", "cube"));   // 只投影 cube

        scene.Camera = Camera.Textbook;
        scene.Solve();

        // cube：3D 12 条 + 三个视图各 8 条 = 36；other 只贡献它自己的 3D 那 12 条
        Assert.Equal(48, scene.BuildDisplayList().OfType<LineShape>().Count());
    }

    [Fact]
    public void 俯视图是严格的从上往下看()
    {
        // 仰角必须允许正好 90° —— 否则俯视图会带上一点点侧向透视，
        // 在正方体上看不出来，但在"长对正"的严格对齐上会露馅。
        var camera = new Camera { AzimuthDeg = 90, ElevationDeg = 90, Orthographic = true };
        var view = camera.CreateView();

        var a = view.Project(new Vec3(-2, 5, -2));
        var b = view.Project(new Vec3(2, 5, -2));

        // 同一高度上的两个点，屏幕纵坐标必须完全相同
        Approx.Scalar(a.Y, b.Y, 1e-12);

        // 而且不出现 NaN（基向量退化时的兜底必须生效）
        Assert.True(double.IsFinite(a.X) && double.IsFinite(a.Y));
    }

    [Fact]
    public void 三视图样例能加载并画出四个版面()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "08-three-views.problem.json");
        var file = ProblemFile.FromJson(File.ReadAllText(path))!;
        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var shapes = result.Scene.BuildDisplayList();

        Assert.Equal(6, shapes.OfType<PolygonShape>().Count());      // 立体图的面
        Assert.Equal(3, shapes.OfType<LabelShape>()
            .Count(l => l.Fill == Theme.Textbook.HelperStroke));     // 三个视图名称
    }
}
