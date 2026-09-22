namespace MathGeo.Core.Tests;

/// <summary>
/// 求解器的基础行为：派生点算得对、拖一个点全图跟着走。
/// 这一组测试就是"活图"这个产品承诺的最小证明。
/// </summary>
public class PointSolvingTests
{
    private static Scene Triangle()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0), "A"));
        scene.Add(GeoPoint.Free("B", new Vec3(4, 0, 0), "B"));
        scene.Add(GeoPoint.Free("C", new Vec3(0, 3, 0), "C"));
        return scene;
    }

    [Fact]
    public void 中点按两端点平均()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "B", "C"));

        var result = scene.Solve();

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Approx.Vec(new Vec3(2, 1.5, 0), scene.Point("D")!.Position);
    }

    [Fact]
    public void 拖动顶点时中点自动跟随()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "B", "C"));
        scene.Solve();

        scene.Drag("B", new Vec3(10, 0, 0));

        Approx.Vec(new Vec3(5, 1.5, 0), scene.Point("D")!.Position);
    }

    [Fact]
    public void 拖动是连锁的_二级派生也跟着走()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "B", "C"));
        scene.Add(GeoPoint.Derived("E", DerivedKind.Midpoint, "A", "D"));   // D 的派生点
        scene.Solve();

        scene.Drag("C", new Vec3(0, 7, 0));

        // D = ((4+0)/2, (0+7)/2) = (2, 3.5)；E = ((0+2)/2, (0+3.5)/2) = (1, 1.75)
        Approx.Vec(new Vec3(2, 3.5, 0), scene.Point("D")!.Position);
        Approx.Vec(new Vec3(1, 1.75, 0), scene.Point("E")!.Position);
    }

    [Fact]
    public void 重心是三个顶点的平均()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("G", DerivedKind.Centroid, "A", "B", "C"));

        scene.Solve();

        Approx.Vec(new Vec3(4.0 / 3, 1, 0), scene.Point("G")!.Position);
    }

    [Fact]
    public void 垂足落在直线上且连线垂直于它()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Free("P", new Vec3(1, 5, 0), "P"));
        scene.Add(GeoLine.Infinite("l", "A", "B"));                 // x 轴
        scene.Add(GeoPoint.Derived("H", DerivedKind.Foot, "P", "l"));

        scene.Solve();

        Approx.Vec(new Vec3(1, 0, 0), scene.Point("H")!.Position);
    }

    [Fact]
    public void 关于直线的对称点()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Free("P", new Vec3(1, 5, 0), "P"));
        scene.Add(GeoLine.Infinite("l", "A", "B"));
        scene.Add(GeoPoint.Derived("P1", DerivedKind.Reflection, "P", "l"));

        scene.Solve();

        Approx.Vec(new Vec3(1, -5, 0), scene.Point("P1")!.Position);
    }

    [Fact]
    public void 关于点的中心对称()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("C1", DerivedKind.Reflection, "C", "A"));

        scene.Solve();

        Approx.Vec(new Vec3(0, -3, 0), scene.Point("C1")!.Position);
    }

    [Fact]
    public void 两线段交点在范围内时才成立()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(4, 4, 0)));
        scene.Add(GeoPoint.Free("C", new Vec3(0, 4, 0)));
        scene.Add(GeoPoint.Free("D", new Vec3(4, 0, 0)));
        scene.Add(GeoLine.Segment("s1", "A", "B"));
        scene.Add(GeoLine.Segment("s2", "C", "D"));
        scene.Add(GeoPoint.Derived("X", DerivedKind.Intersection, "s1", "s2"));

        var result = scene.Solve();

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Approx.Vec(new Vec3(2, 2, 0), scene.Point("X")!.Position);
    }

    [Fact]
    public void 无交点时给出诊断而不是抛异常()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(1, 0, 0)));
        scene.Add(GeoPoint.Free("C", new Vec3(0, 1, 0)));
        scene.Add(GeoPoint.Free("D", new Vec3(1, 1, 0)));
        scene.Add(GeoLine.Segment("s1", "A", "B"));     // 两条平行线段
        scene.Add(GeoLine.Segment("s2", "C", "D"));
        scene.Add(GeoPoint.Derived("X", DerivedKind.Intersection, "s1", "s2"));

        var result = scene.Solve();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "geom.noIntersection");
        Assert.Equal("X", Assert.Single(result.Errors).ObjectId);
    }

    [Fact]
    public void 引用不存在的对象会被拓扑排序挡住()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "B", "C"));

        var result = scene.Solve();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "ref.missing");
    }

    [Fact]
    public void 循环依赖被检出而不是死循环()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("B", new Vec3(4, 0, 0)));
        // P 挂在直线 l 上，而 l 的一个端点又是 P —— 环
        scene.Add(GeoLine.Segment("l", "P", "B"));
        scene.Add(GeoPoint.OnPath("P", "l", 0.5));

        var result = scene.Solve();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "ref.cycle");
    }

    [Fact]
    public void 派生点不能直接拖动()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "B", "C"));
        scene.Solve();

        Assert.False(scene.Point("D")!.IsDraggable);
        Assert.Throws<InvalidOperationException>(() => scene.Drag("D", new Vec3(1, 1, 0)));
    }

    [Fact]
    public void 平移_旋转_位似()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("T", DerivedKind.Translate, "C", "A", "B"));      // C + (B - A)
        scene.Add(GeoPoint.Derived("R", DerivedKind.Rotate, "B", "A", "90"));       // 绕 A 逆时针 90°
        scene.Add(GeoPoint.Derived("S", DerivedKind.Scale, "B", "A", "2"));         // 以 A 为中心放大 2 倍

        var result = scene.Solve();
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        Approx.Vec(new Vec3(4, 3, 0), scene.Point("T")!.Position);
        Approx.Vec(new Vec3(0, 4, 0), scene.Point("R")!.Position, 1e-9);
        Approx.Vec(new Vec3(8, 0, 0), scene.Point("S")!.Position);
    }

    [Fact]
    public void 定比分点()
    {
        var scene = Triangle();
        scene.Add(GeoPoint.Derived("P", DerivedKind.OnSegment, "A", "B", "0.25"));

        scene.Solve();

        Approx.Vec(new Vec3(1, 0, 0), scene.Point("P")!.Position);
    }
}
