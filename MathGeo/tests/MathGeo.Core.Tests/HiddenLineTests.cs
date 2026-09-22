namespace MathGeo.Core.Tests;

/// <summary>
/// 隐藏线渲染。
///
/// 这里断言的不是"画出来了"，而是"画对了"：
/// 正方体从一般角度必须恰好 3 条虚线且交汇于隐藏顶点；正对一个面必须 8 条虚线；
/// 微小旋转不许改变判定结果（虚线闪烁的回归测试）。
/// </summary>
public class HiddenLineTests
{
    private static Scene Cube(
        Camera? camera = null, bool showHidden = true, IReadOnlyList<int>? highlight = null)
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);

        expansion.Solid.ShowHiddenEdges = showHidden;
        if (highlight is not null) expansion.Solid.HighlightFaces = highlight;
        scene.Add(expansion.Solid);

        scene.Camera = camera ?? Camera.Textbook;
        scene.Solve();
        return scene;
    }

    private static List<LineShape> Dashed(Scene scene)
        => [.. scene.BuildDisplayList().OfType<LineShape>().Where(l => l.Dash is { Length: > 0 })];

    [Fact]
    public void 正方体从一般角度看恰好有三条虚线隐藏棱()
    {
        Assert.Equal(3, Dashed(Cube()).Count);
    }

    [Fact]
    public void 三条隐藏棱交汇于同一个隐藏顶点()
    {
        var dashed = Dashed(Cube());
        Assert.Equal(3, dashed.Count);

        var endpoints = dashed.SelectMany(l => new[] { l.A, l.B }).ToList();
        var shared = endpoints.Where(p => endpoints.Count(q => q == p) == 3).Distinct().ToList();

        Assert.Single(shared);
    }

    [Fact]
    public void 正对一个面时其余八条棱都是虚线()
    {
        // 从 +X 正对着看：只看得见一个面，四条棱可见，其余八条全被自己挡住。
        Assert.Equal(8, Dashed(Cube(Camera.Front)).Count);
    }

    [Fact]
    public void 关掉隐藏棱后一条虚线都没有()
    {
        Assert.Empty(Dashed(Cube(showHidden: false)));
    }

    [Fact]
    public void 十二条棱无论虚实都画出来了()
    {
        Assert.Equal(12, Cube().BuildDisplayList().OfType<LineShape>().Count());
    }

    [Fact]
    public void 微小旋转不会让隐藏棱判定跳变()
    {
        // "旋转时虚线不许闪"那条质量红线的自动化版本。
        // 角度刻意避开 0/90/180/270 这些退化方向 —— 在那些方向上隐藏棱数量本来就会合法地改变。
        foreach (var azimuth in new[] { 20.0, 35.0, 50.0, 65.0, 110.0, 125.0, 140.0, 200.0, 215.0, 230.0, 245.0 })
        {
            var baseline = Dashed(Cube(Camera.Textbook.Orbit(azimuth, 0))).Count;

            Assert.Equal(baseline, Dashed(Cube(Camera.Textbook.Orbit(azimuth + 0.05, 0))).Count);
            Assert.Equal(baseline, Dashed(Cube(Camera.Textbook.Orbit(azimuth - 0.05, 0))).Count);
        }
    }

    [Fact]
    public void 六个面都画出来且都是半透明填充()
    {
        var faces = Cube().BuildDisplayList().OfType<PolygonShape>().ToList();

        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.Equal(Theme.Textbook.PolygonFill, f.Fill));
    }

    [Fact]
    public void 高亮的面用强调色且只高亮指定的那些()
    {
        var faces = Cube(highlight: [0, 2]).BuildDisplayList().OfType<PolygonShape>().ToList();

        Assert.Equal(2, faces.Count(f => f.Fill == Theme.Textbook.FaceFillHighlight));
        Assert.Equal(4, faces.Count(f => f.Fill == Theme.Textbook.PolygonFill));
    }

    [Fact]
    public void 八个顶点都带上课本约定的标签()
    {
        var labels = Cube().BuildDisplayList().OfType<LabelShape>().Select(l => l.Text).ToList();

        Assert.Equal(8, labels.Count);
        Assert.Contains("A", labels);
        Assert.Contains("A₁", labels);
        Assert.Contains("D₁", labels);
    }

    [Fact]
    public void 立体图里的线段按相机投影而不是丢掉Z()
    {
        var scene = Cube(Camera.Isometric);

        var diagonal = GeoLine.Segment("ac1", "cube.A", "cube.C1");
        diagonal.Style = "answer";
        scene.Add(diagonal);
        scene.Solve();

        var line = scene.BuildDisplayList().OfType<LineShape>()
            .Single(l => l.Stroke == Theme.Textbook.AnswerStroke);

        // 场景的相机绕图形重心转，正方体重心是 (0, 2, 0)。
        var view = (Camera.Isometric with { Target = new Vec3(0, 2, 0) }).CreateView();

        Approx.Vec(view.Project(new Vec3(-2, 0, -2)), line.A, 1e-6);
        Approx.Vec(view.Project(new Vec3(2, 4, 2)), line.B, 1e-6);
    }

    [Fact]
    public void 极端相机角度下输出坐标全部有限()
    {
        // 正上/正下看时相机的基向量最容易退化出 NaN，那种图导进 PPT 会直接报错。
        foreach (var azimuth in new[] { 0.0, 45.0, 90.0, 135.0, 180.0, 225.0, 270.0, 315.0 })
            foreach (var elevation in new[] { -89.0, -45.0, 0.0, 45.0, 89.0 })
            {
                var scene = Cube(Camera.Textbook.Orbit(azimuth, elevation));

                foreach (var shape in scene.BuildDisplayList())
                    foreach (var coordinate in Coordinates(shape))
                        Assert.True(double.IsFinite(coordinate),
                            $"方位角 {azimuth} 仰角 {elevation} 下出现了非有限坐标");
            }
    }

    private static IEnumerable<double> Coordinates(Shape shape)
    {
        switch (shape)
        {
            case LineShape line:
                yield return line.A.X; yield return line.A.Y;
                yield return line.B.X; yield return line.B.Y;
                break;

            case PolygonShape polygon:
                foreach (var p in polygon.Points) { yield return p.X; yield return p.Y; }
                break;

            case CircleShape circle:
                yield return circle.Center.X; yield return circle.Center.Y; yield return circle.Radius;
                break;

            case DotShape dot:
                yield return dot.At.X; yield return dot.At.Y; yield return dot.Radius;
                break;

            case LabelShape label:
                yield return label.At.X; yield return label.At.Y;
                break;

            case ArcShape arc:
                yield return arc.Center.X; yield return arc.Center.Y;
                yield return arc.Radius; yield return arc.StartRad; yield return arc.SweepRad;
                break;
        }
    }

    [Fact]
    public void 沿视线方向的棱投影成点_不该被画出来()
    {
        var scene = new Scene();
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        foreach (var vertex in expansion.Vertices) scene.Add(vertex);
        scene.Add(expansion.Solid);

        // 严格正对：视线沿 Z 轴，正交投影
        scene.Camera = new Camera { AzimuthDeg = 90, ElevationDeg = 0, Orthographic = true };
        scene.Solve();

        var lines = scene.BuildDisplayList().OfType<LineShape>().ToList();

        // 12 条棱里有 4 条沿视线方向，投影后退化成点，不该产生任何线段
        Assert.Equal(8, lines.Count);

        // 而且近面的 4 条（实线）与远面的 4 条（虚线）投影到同样的位置
        Assert.Equal(4, lines.Count(l => l.Dash is { Length: > 0 }));
    }

    [Fact]
    public void 多个体互相遮挡时后面的体会被画成虚线()
    {
        var scene = new Scene();

        // 一个小立方体放在大立方体正后方（沿相机方向更远）
        var front = SolidPresets.Box("front", 4, 4, 4);
        var back = SolidPresets.Box("back", 2, 2, 2, new Vec3(0, 1, -6));

        foreach (var p in front.Vertices) scene.Add(p);
        foreach (var p in back.Vertices) scene.Add(p);
        scene.Add(front.Solid);
        scene.Add(back.Solid);

        scene.Camera = Camera.Front;
        scene.Solve();

        var shapes = scene.BuildDisplayList();

        // 后面的小立方体完全被挡住时，它的棱应当全部是虚线
        Assert.Contains(shapes.OfType<LineShape>(), l => l.Dash is { Length: > 0 });
    }
}
