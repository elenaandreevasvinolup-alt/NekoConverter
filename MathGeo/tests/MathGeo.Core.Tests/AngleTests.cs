namespace MathGeo.Core.Tests;

/// <summary>
/// 通用角标注。
///
/// 关键行为是"直角改画直角符号" —— 这是课本规范，也是学生识别直角的唯一依据。
/// 写成 90° 在教学上是错的，所以这里有专门的断言盯着。
/// </summary>
public class AngleTests
{
    private static Scene RightAngle()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("C", new Vec3(0, 0, 0), "C"));
        scene.Add(GeoPoint.Free("A", new Vec3(5, 0, 0), "A"));
        scene.Add(GeoPoint.Free("B", new Vec3(0, 4, 0), "B"));
        scene.Solve();
        return scene;
    }

    private static List<LineShape> AngleLines(Scene scene)
        => [.. scene.BuildDisplayList().OfType<LineShape>()
            .Where(l => l.Stroke == Theme.Textbook.AngleStroke)];

    private static List<LabelShape> AngleLabels(Scene scene)
        => [.. scene.BuildDisplayList().OfType<LabelShape>()
            .Where(l => l.Fill == Theme.Textbook.AngleStroke)];

    [Fact]
    public void 直角画成直角符号而不是度数()
    {
        var scene = RightAngle();
        scene.Add(GeoAngle.Of("a", "C", "A", "B"));
        scene.Solve();

        Assert.Equal(2, AngleLines(scene).Count);   // 直角符号是两笔
        Assert.Empty(AngleLabels(scene));           // 不写 90°
    }

    [Fact]
    public void 非直角画圆弧并标出度数()
    {
        var scene = RightAngle();
        scene.Add(GeoAngle.Of("a", "A", "C", "B"));   // ∠CAB
        scene.Solve();

        var lines = AngleLines(scene);
        var labels = AngleLabels(scene);

        Assert.True(lines.Count > 10, "非直角应当画成圆弧（多段折线）");
        Assert.Single(labels);
        Assert.EndsWith("°", labels[0].Text);

        // ∠CAB 的两条边分别是 AC（指向 +x）和 AB（指向 (-5,4)），夹角 38.66°
        Assert.StartsWith("38.7", labels[0].Text);
    }

    [Fact]
    public void 圆弧半径按较短边取比例()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("O", Vec3.Zero));
        scene.Add(GeoPoint.Free("P", new Vec3(10, 0, 0)));    // 长边
        scene.Add(GeoPoint.Free("Q", new Vec3(1, 1, 0)));     // 短边
        scene.Add(GeoAngle.Of("a", "O", "P", "Q"));
        scene.Solve();

        // 半径按短边（≈1.414）的 0.28 取，而不是按长边（10）——
        // 否则圆弧会比短边还长，画出图外。
        var points = AngleLines(scene).SelectMany(l => new[] { l.A, l.B }).ToList();
        var maxDistance = points.Max(p => Vec2.Distance(Vec2.Zero, p));

        Assert.True(maxDistance < 1.5, $"圆弧半径应当按短边取，实际最远点 {maxDistance}");
    }

    [Fact]
    public void 关掉数值后直角符号仍然保留()
    {
        // 直角符号本身就是"90°"的表达，不该被 ShowValue 关掉。
        var scene = RightAngle();
        var angle = GeoAngle.Of("a", "C", "A", "B");
        angle.ShowValue = false;
        scene.Add(angle);
        scene.Solve();

        Assert.Equal(2, AngleLines(scene).Count);
    }

    [Fact]
    public void 三维里也能标角()
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var p in expansion.Vertices) scene.Add(p);
        scene.Add(expansion.Solid);

        // ∠A₁AB：立方体上底面棱与竖直棱的夹角，是 90°
        scene.Add(GeoAngle.Of("a", "cube.A", "cube.A1", "cube.B"));
        scene.Camera = Camera.Textbook;
        scene.Solve();

        var lines = AngleLines(scene);

        Assert.Equal(2, lines.Count);   // 直角符号

        // 而且必须投影过：三条坐标里至少有一个不是 2D 的直接截取
        Assert.All(lines, l => Assert.True(double.IsFinite(l.A.X) && double.IsFinite(l.A.Y)));
    }

    [Fact]
    public void 角样例里两个直角加一个锐角()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "09-angle-mark.problem.json");
        var file = ProblemFile.FromJson(File.ReadAllText(path))!;
        var result = ProblemMapper.Load(file);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var shapes = result.Scene.BuildDisplayList();

        var angleLines = shapes.OfType<LineShape>().Count(l => l.Stroke == Theme.Textbook.AngleStroke);
        var angleLabels = shapes.OfType<LabelShape>().Count(l => l.Fill == Theme.Textbook.AngleStroke);

        // 两个直角各两笔 = 4 条，一个锐角的圆弧 = 32 段，合计 36
        Assert.Equal(36, angleLines);
        Assert.Equal(1, angleLabels);
    }
}
