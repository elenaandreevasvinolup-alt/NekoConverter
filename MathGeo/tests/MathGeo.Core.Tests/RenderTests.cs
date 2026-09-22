using System.Xml.Linq;

namespace MathGeo.Core.Tests;

/// <summary>
/// 渲染层：显示列表与 SVG 导出。
///
/// 关键断言是"导出的 SVG 是良构 XML" —— 老师会把它拖进 PPT、浏览器、希沃白板，
/// 任何一个解析器不认都会变成"这软件坏了"。
/// </summary>
public class RenderTests
{
    private static Scene Triangle()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0), "A"));
        scene.Add(GeoPoint.Free("B", new Vec3(4, 0, 0), "B"));
        scene.Add(GeoPoint.Free("C", new Vec3(0, 3, 0), "C"));
        scene.Add(GeoPolygon.Of("abc", "A", "B", "C"));
        scene.Add(GeoLine.Segment("AB", "A", "B"));
        scene.Add(GeoLine.Segment("BC", "B", "C"));
        scene.Add(GeoLine.Segment("CA", "C", "A"));
        scene.Solve();
        return scene;
    }

    [Fact]
    public void 显示列表包含多边形_线段_点与标签()
    {
        var shapes = Triangle().BuildDisplayList();

        Assert.Contains(shapes, s => s is PolygonShape);
        Assert.Contains(shapes, s => s is LineShape);
        Assert.Contains(shapes, s => s is DotShape);
        Assert.Contains(shapes, s => s is LabelShape { Text: "A" });
        Assert.Contains(shapes, s => s is LabelShape { Text: "B" });
        Assert.Contains(shapes, s => s is LabelShape { Text: "C" });
    }

    [Fact]
    public void 辅助线用虚线_所求对象用红色()
    {
        var scene = Triangle();

        var helper = GeoLine.Segment("mid", "A", "B");
        helper.Style = "helper";
        scene.Add(helper);

        var answer = GeoLine.Segment("answer", "A", "C");
        answer.Style = "answer";
        scene.Add(answer);

        scene.Solve();

        var shapes = scene.BuildDisplayList();

        var helperShape = shapes.OfType<LineShape>().Single(l => l.Dash is { Length: > 0 });
        Assert.Equal(Theme.Textbook.HelperStroke, helperShape.Stroke);

        var answerShape = shapes.OfType<LineShape>().Single(l => l.Stroke == Theme.Textbook.AnswerStroke);
        Assert.Null(answerShape.Dash);
    }

    [Fact]
    public void 导出的SVG是良构XML()
    {
        var svg = Triangle().ToSvg();

        var document = XDocument.Parse(svg);   // 解析失败会直接抛异常
        Assert.Equal("svg", document.Root!.Name.LocalName);
    }

    [Fact]
    public void 导出的SVG带上了标签文本与线宽()
    {
        var svg = Triangle().ToSvg();

        Assert.Contains(">A</text>", svg);
        Assert.Contains(">B</text>", svg);
        Assert.Contains(">C</text>", svg);
        Assert.Contains("font-style=\"italic\"", svg);
        Assert.Contains("<polygon", svg);
        Assert.Contains("stroke-linecap=\"round\"", svg);
    }

    [Fact]
    public void 标签里的特殊字符被转义()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0), "A<&>"));
        scene.Solve();

        var svg = scene.ToSvg();

        Assert.Contains("A&lt;&amp;&gt;", svg);
        XDocument.Parse(svg);
    }

    [Fact]
    public void 无限长的直线被裁到视野内而不是撑爆画布()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0), "A"));
        scene.Add(GeoPoint.Free("B", new Vec3(4, 0, 0), "B"));
        scene.Add(GeoPoint.Free("C", new Vec3(0, 3, 0), "C"));
        scene.Add(GeoLine.Infinite("l", "A", "B"));
        scene.Solve();

        var bounds = Bounds2.Of(scene.BuildDisplayList());

        Assert.True(bounds.Width < 100,
            $"包围盒宽度 {bounds.Width}，说明无限长的直线没有被裁剪");
    }

    [Fact]
    public void 射线只往一个方向延伸()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(1, 0, 0)));
        scene.Add(GeoLine.Ray("r", "A", "B"));
        scene.Solve();

        var ray = scene.BuildDisplayList().OfType<LineShape>().Single();

        // 起点必须是 A，终点在 B 的更外侧（而不是反方向）
        Assert.True(ray.A.X <= 1e-9);
        Assert.True(ray.B.X > 1);
    }

    [Fact]
    public void 暗色主题改背景色()
    {
        var svg = Triangle().ToSvg(Theme.Dark);
        Assert.Contains(Theme.Dark.Background, svg);
        Assert.DoesNotContain($"fill=\"{Theme.Textbook.Background}\"", svg);
    }

    [Fact]
    public void 隐藏的对象不出现在显示列表里()
    {
        var scene = Triangle();
        var hidden = GeoPoint.Free("H", new Vec3(9, 9, 0), "H");
        hidden.Visible = false;
        scene.Add(hidden);
        scene.Solve();

        Assert.DoesNotContain(scene.BuildDisplayList(), s => s is LabelShape { Text: "H" });
    }

    [Fact]
    public void 角度弧能输出成SVG路径()
    {
        var shapes = new List<Shape>
        {
            new ArcShape(new Vec2(0, 0), 20, 0, Math.PI / 3, "#000", 1.2),
        };

        var svg = SvgWriter.Write(shapes, Theme.Textbook);

        Assert.Contains("<path", svg);
        Assert.Contains(" A ", svg);
        XDocument.Parse(svg);
    }
}
