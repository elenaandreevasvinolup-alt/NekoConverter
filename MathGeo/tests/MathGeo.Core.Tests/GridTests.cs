namespace MathGeo.Core.Tests;

/// <summary>
/// 网格与坐标轴。
///
/// 关键约定：**内核默认不画网格**。内核是库，不该默认往图里加装饰 ——
/// 导出到 PPT 时多数时候只要图形本身。应用和预览各自决定要不要打开。
/// </summary>
public class GridTests
{
    private static Scene CubeScene(bool grid, bool invertY = false, Camera? camera = null)
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        scene.Camera = camera ?? Camera.Textbook;
        scene.View = scene.View with { ShowGrid = grid, InvertY = invertY };
        scene.Solve();
        return scene;
    }

    private static int GridLineCount(Scene scene)
        => scene.BuildDisplayList().OfType<LineShape>()
            .Count(l => l.Stroke == Theme.Textbook.GridLine);

    [Fact]
    public void 默认不画网格()
    {
        var scene = CubeScene(grid: false);

        // 只有立方体的 12 条棱，没有别的线
        Assert.Equal(12, scene.BuildDisplayList().OfType<LineShape>().Count());
        Assert.Equal(0, GridLineCount(scene));
    }

    [Fact]
    public void 打开网格后有三条坐标轴和箭头字母()
    {
        var scene = CubeScene(grid: true);
        var shapes = scene.BuildDisplayList();

        string[] axisColors = [Theme.Textbook.AxisX, Theme.Textbook.AxisY, Theme.Textbook.AxisZ];

        // 三条轴线
        foreach (var color in axisColors)
            Assert.Contains(shapes.OfType<LineShape>(), l => l.Stroke == color);

        // 三个箭头（实心三角）
        Assert.Equal(3, shapes.OfType<PolygonShape>().Count(p => axisColors.Contains(p.Fill)));

        // 三个字母
        foreach (var name in new[] { "x", "y", "z" })
            Assert.Contains(shapes.OfType<LabelShape>(), l => l.Text == name);

        // 网格线
        Assert.True(GridLineCount(scene) > 0);
    }

    [Fact]
    public void 二维场景只有两条坐标轴()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(3, 0, 0)));
        scene.View = scene.View with { ShowGrid = true };
        scene.Solve();

        var shapes = scene.BuildDisplayList();

        Assert.Contains(shapes.OfType<LineShape>(), l => l.Stroke == Theme.Textbook.AxisX);
        Assert.Contains(shapes.OfType<LineShape>(), l => l.Stroke == Theme.Textbook.AxisY);
        Assert.DoesNotContain(shapes.OfType<LineShape>(), l => l.Stroke == Theme.Textbook.AxisZ);
    }

    [Fact]
    public void 正对坐标面时补上那个面的网格()
    {
        // 从侧面看（视线沿 X）时，z=0 的 XY 网格会退化成一条线，完全失去尺度感。
        // 这时候必须补上 YZ 网格 —— 那正是三视图最需要参照的时候。
        var general = CubeScene(grid: true, camera: Camera.Textbook);
        var side = CubeScene(grid: true, camera: new Camera { AzimuthDeg = 0, ElevationDeg = 0 });
        var top = CubeScene(grid: true, camera: new Camera { AzimuthDeg = 90, ElevationDeg = 90 });

        Assert.True(GridLineCount(side) > GridLineCount(general),
            $"侧视应当补一个面的网格：一般视角 {GridLineCount(general)} 条，侧视 {GridLineCount(side)} 条");

        Assert.True(GridLineCount(top) > GridLineCount(general),
            $"俯视应当补一个面的网格：一般视角 {GridLineCount(general)} 条，俯视 {GridLineCount(top)} 条");
    }

    [Fact]
    public void 网格画在图形之前_会被半透明的面盖住()
    {
        var scene = CubeScene(grid: true);
        var shapes = scene.BuildDisplayList();

        var lastGrid = shapes.ToList().FindLastIndex(
            s => s is LineShape l && l.Stroke == Theme.Textbook.GridLine);
        var firstFace = shapes.ToList().FindIndex(s => s is PolygonShape);

        Assert.True(lastGrid < firstFace,
            "网格必须画在面之前 —— 否则网格会浮在体前面，看起来像贴在屏幕上的一张纸");
    }

    [Fact]
    public void Y轴反转把整个视图上下翻转()
    {
        var normal = CubeScene(grid: false);
        var flipped = CubeScene(grid: false, invertY: true);

        var a = normal.BuildDisplayList().OfType<DotShape>().First();
        var b = flipped.BuildDisplayList().OfType<DotShape>().First();

        Approx.Scalar(a.At.X, b.At.X, 1e-9);
        Approx.Scalar(-a.At.Y, b.At.Y, 1e-9);
    }

    [Fact]
    public void Y轴反转时标签锚点也跟着翻()
    {
        // 只翻坐标不翻锚点的话，标签会跑到点的错误一侧。
        var normal = CubeScene(grid: false);
        var flipped = CubeScene(grid: false, invertY: true);

        var a = normal.BuildDisplayList().OfType<LabelShape>().First();
        var b = flipped.BuildDisplayList().OfType<LabelShape>().First();

        var mirrored = a.Anchor switch
        {
            LabelAnchor.Top => LabelAnchor.Bottom,
            LabelAnchor.Bottom => LabelAnchor.Top,
            LabelAnchor.TopLeft => LabelAnchor.BottomLeft,
            LabelAnchor.BottomLeft => LabelAnchor.TopLeft,
            LabelAnchor.TopRight => LabelAnchor.BottomRight,
            LabelAnchor.BottomRight => LabelAnchor.TopRight,
            var other => other,
        };

        Assert.Equal(mirrored, b.Anchor);
    }
}
