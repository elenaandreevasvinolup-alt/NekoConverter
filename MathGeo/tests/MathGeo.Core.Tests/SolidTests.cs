namespace MathGeo.Core.Tests;

/// <summary>
/// 参数化基本体与相机。
///
/// 这组测试里最重要的是"面连通性"那几条：顶点位置对但面连错，图形看上去只是"有点怪"，
/// 不会报任何错，但隐藏线判定会全乱。这种 bug 只能靠结构断言抓。
/// </summary>
public class SolidTests
{
    private static HashSet<(int, int)> EdgesOf(GeoPolyhedron solid)
    {
        var edges = new HashSet<(int, int)>();

        foreach (var face in solid.Faces)
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                edges.Add(a < b ? (a, b) : (b, a));
            }

        return edges;
    }

    private static int DegreeOf(HashSet<(int, int)> edges, int vertex)
        => edges.Count(e => e.Item1 == vertex || e.Item2 == vertex);

    [Fact]
    public void 正方体展开成八个顶点与六个面()
    {
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        Assert.Equal(8, expansion.Vertices.Count);
        Assert.Equal(6, expansion.Solid.Faces.Count);
        Assert.All(expansion.Solid.Faces, f => Assert.Equal(4, f.Length));
    }

    [Fact]
    public void 正方体的面连通性正确()
    {
        var solid = SolidPresets.Box("cube", 4, 4, 4).Solid;
        var edges = EdgesOf(solid);

        Assert.Equal(12, edges.Count);
        for (var v = 0; v < 8; v++) Assert.Equal(3, DegreeOf(edges, v));

        // 下底必须是 0,1,2,3 —— 面的下标就是按"先下底四个、再上底四个"写的。
        Assert.Equal([0, 1, 2, 3], solid.Faces[0]);
        Assert.Equal([4, 7, 6, 5], solid.Faces[1]);
    }

    [Fact]
    public void 顶点顺序是先四个下底再四个上底()
    {
        // 这条断言专门防"把两层循环合并"那个 bug：合并之后顶点位置仍然全对，
        // 但顺序变成 A, A₁, B, B₁…，面就连到错误的顶点上去了。
        var expansion = SolidPresets.Box("cube", 4, 4, 4);
        var labels = expansion.Vertices.Select(p => p.Label ?? string.Empty).ToArray();

        Assert.Equal(["A", "B", "C", "D", "A₁", "B₁", "C₁", "D₁"], labels);
    }

    [Fact]
    public void 顶点坐标符合尺寸与原点()
    {
        var expansion = SolidPresets.Box("cube", 4, 4, 4, new Vec3(10, 1, 0));

        Approx.Vec(new Vec3(8, 1, -2), expansion.Vertices[0].FreePosition);    // A
        Approx.Vec(new Vec3(12, 1, -2), expansion.Vertices[1].FreePosition);   // B
        Approx.Vec(new Vec3(12, 5, -2), expansion.Vertices[5].FreePosition);   // B₁
    }

    [Fact]
    public void 顶点Id用ASCII数字而标签用课本下标()
    {
        // Id 里塞 Unicode 下标的话，手写文件和 agent 写的 "cube.C1" 会全部找不到。
        var expansion = SolidPresets.Box("cube", 4, 4, 4);

        Assert.Contains(expansion.Vertices, p => p.Id == "cube.C1" && p.Label == "C₁");
        Assert.DoesNotContain(expansion.Vertices, p => p.Id.Contains('₁'));
    }

    [Fact]
    public void 预判的顶点Id与展开结果完全一致()
    {
        // 结构校验必须在展开之前就知道会生成哪些 Id，两边一旦漂了就会出现
        // "校验通过但求解报错"或者反过来的鬼故事。
        foreach (var (solid, sides, prefix, apex) in new[]
        {
            ("box", (int?)null, (string?)null, (string?)null),
            ("prism", (int?)3, (string?)null, (string?)null),
            ("prism", (int?)6, (string?)"UVWXYZ", (string?)null),
            ("pyramid", (int?)4, (string?)null, (string?)null),
            ("pyramid", (int?)3, (string?)"PQR", (string?)"T"),
        })
        {
            var parameters = SolidPresets.Normalize(solid, sides, prefix, apex);
            var predicted = SolidPresets.PredictVertexIds("s", parameters);

            var expansion = solid switch
            {
                "prism" => SolidPresets.Prism("s", sides ?? 3, 2, 3, default, parameters.Letters),
                "pyramid" => SolidPresets.Pyramid("s", sides ?? 3, 2, 3, default, parameters.Apex, parameters.Letters),
                _ => SolidPresets.Box("s", 4, 4, 4, default, parameters.Letters),
            };

            Assert.Equal(predicted, expansion.Vertices.Select(p => p.Id).ToArray());
        }
    }

    [Fact]
    public void 三棱柱有六个顶点五个面()
    {
        var expansion = SolidPresets.Prism("p", 3, 2, 3);
        var solid = expansion.Solid;

        Assert.Equal(6, expansion.Vertices.Count);
        Assert.Equal(5, solid.Faces.Count);
        Assert.Equal(9, EdgesOf(solid).Count);
        Assert.Equal(["A", "B", "C", "A₁", "B₁", "C₁"], expansion.Vertices.Select(p => p.Label));
    }

    [Fact]
    public void 四棱锥有五个顶点_顶点叫S()
    {
        var expansion = SolidPresets.Pyramid("s", 4, 2, 3);
        var solid = expansion.Solid;

        Assert.Equal(5, expansion.Vertices.Count);
        Assert.Equal(5, solid.Faces.Count);          // 一个底面 + 四个侧面
        Assert.Equal(8, EdgesOf(solid).Count);
        Assert.Equal(["A", "B", "C", "D", "S"], expansion.Vertices.Select(p => p.Label));
    }

    [Fact]
    public void 棱锥的底面绕向朝下_侧面绕向朝外()
    {
        var expansion = SolidPresets.Pyramid("s", 3, 2, 3);
        var solid = expansion.Solid;
        var v = expansion.Vertices.Select(p => p.FreePosition).ToArray();

        // 底面法向应当是 -Y
        var baseFace = solid.Faces[0];
        var baseNormal = Vec3.Cross(
            v[baseFace[1]] - v[baseFace[0]],
            v[baseFace[2]] - v[baseFace[0]]);
        Assert.True(baseNormal.Y < 0, $"底面法向应朝下，实际 {baseNormal}");

        // 侧面法向应当背离中轴（也就是与"面重心到轴"的方向同向）
        foreach (var face in solid.Faces.Skip(1))
        {
            var normal = Vec3.Cross(v[face[1]] - v[face[0]], v[face[2]] - v[face[0]]);
            var centroid = face.Aggregate(Vec3.Zero, (sum, i) => sum + v[i]) / face.Length;
            var outward = new Vec3(centroid.X, 0, centroid.Z);
            Assert.True(Vec3.Dot(normal, outward) > 0, $"侧面法向应朝外，实际 {normal}");
        }
    }

    // ————————————————————————————— 相机 —————————————————————————————

    [Fact]
    public void 正交正视下正方体投影成正方形()
    {
        var view = (Camera.Front with { Orthographic = true }).CreateView();

        var a = view.Project(new Vec3(2, 0, -2));
        var b = view.Project(new Vec3(2, 0, 2));
        var c = view.Project(new Vec3(2, 4, 2));
        var d = view.Project(new Vec3(2, 4, -2));

        Approx.Scalar(4, Vec2.Distance(a, b), 6);
        Approx.Scalar(4, Vec2.Distance(b, c), 6);
        Approx.Scalar(4, Vec2.Distance(c, d), 6);
        Approx.Scalar(4, Vec2.Distance(d, a), 6);
    }

    [Fact]
    public void 正交俯视下正方体也投影成正方形()
    {
        var view = (Camera.Top with { Orthographic = true }).CreateView();

        var a = view.Project(new Vec3(-2, 4, -2));
        var b = view.Project(new Vec3(2, 4, -2));
        var c = view.Project(new Vec3(2, 4, 2));
        var d = view.Project(new Vec3(-2, 4, 2));

        Approx.Scalar(4, Vec2.Distance(a, b), 6);
        Approx.Scalar(4, Vec2.Distance(b, c), 6);
    }

    [Fact]
    public void 弱透视在目标附近尺度与正交一致()
    {
        // 两种投影的尺度必须接近，否则老师切换"课本投影/真实透视"时图会突然跳大小。
        var orthographic = (Camera.Textbook with { Orthographic = true }).CreateView();
        var perspective = Camera.Textbook.CreateView();
        var target = Camera.Textbook.Target;

        Approx.Vec(orthographic.Project(target), perspective.Project(target), 1e-6);
    }

    [Fact]
    public void 仰角被限制在正负八十九度()
    {
        var camera = Camera.Textbook.Orbit(0, 1000);
        Assert.Equal(89, camera.ElevationDeg);

        camera = Camera.Textbook.Orbit(0, -1000);
        Assert.Equal(-89, camera.ElevationDeg);
    }

    [Fact]
    public void 方位角可以无限累加()
    {
        var camera = Camera.Textbook.Orbit(720, 0);
        Assert.Equal(Camera.Textbook.AzimuthDeg + 720, camera.AzimuthDeg);
    }

    [Fact]
    public void 正交正对时沿视线的棱投影成同一点()
    {
        // 这条测的是"退化棱"的判定基础：严格正对时，沿视线方向的棱必须投影成一个点。
        // cos(90°) 在浮点里是 6.1e-17 而不是 0，所以投影出来会有一点点残差 ——
        // 渲染器必须能把它和真实棱区分开，否则会画出零长度的虚线。
        var camera = new Camera
        {
            AzimuthDeg = 90,
            ElevationDeg = 0,
            Orthographic = true,
            Distance = 24,
            Target = new Vec3(0, 2, 0),
        };

        var view = camera.CreateView();

        var b = view.Project(new Vec3(2, 0, -2));
        var c = view.Project(new Vec3(2, 0, 2));

        var length = Vec2.Distance(b, c);

        // 残差应当只有浮点噪声的量级，远小于任何真实棱
        Assert.True(length < 1e-9, $"沿视线的棱投影长度是 {length}，不该超过浮点噪声量级");

        // 而垂直方向的棱必须保持真实长度
        var a = view.Project(new Vec3(-2, 0, -2));
        Approx.Scalar(4, Vec2.Distance(a, b), 6);
    }

    [Fact]
    public void 投影深度随离相机的距离单调增加()
    {
        // Camera.Front 的相机在 +X 轴上（eye = (24, 0, 0)），所以沿 -X 越远深度越大。
        var view = Camera.Front.CreateView();

        view.ProjectWithDepth(new Vec3(10, 0, 0), out var near);
        view.ProjectWithDepth(new Vec3(0, 0, 0), out var middle);
        view.ProjectWithDepth(new Vec3(-10, 0, 0), out var far);

        Assert.True(near < middle, $"深度应随距离增加：{near} 应小于 {middle}");
        Assert.True(middle < far, $"深度应随距离增加：{middle} 应小于 {far}");
    }
}
