namespace MathGeo.Core.Tests;

/// <summary>
/// 动点与二级动点。
///
/// 这是整个产品唯一没有对手的战场：一级动点看"沿线运动"，二级动点看"轨迹"。
/// 黑板和 PPT 都画不出轨迹，而轨迹只需要一个位置数组 —— 所以这组测试
/// 同时也是"体积不是问题"的证明。
/// </summary>
public class MovingPointTests
{
    [Fact]
    public void 路径点只能沿线滑动_拖到线外会被投影回来()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(10, 0, 0)));
        scene.Add(GeoLine.Segment("AB", "A", "B"));
        scene.Add(GeoPoint.OnPath("P", "AB", 0.5));

        scene.Solve();
        Approx.Vec(new Vec3(5, 0, 0), scene.Point("P")!.Position);

        // 拖到线外很高的地方：应当被投影回线段上，Y 保持 0
        scene.Drag("P", new Vec3(3, 999, 0));

        var p = scene.Point("P")!.Position;
        Approx.Scalar(0, p.Y, 1e-6);
        Approx.Scalar(3, p.X, 1e-3);
    }

    [Fact]
    public void 圆上的动点绕圆运动()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("O", new Vec3(0, 0, 0)));
        scene.Add(GeoCircle.Fixed("c", "O", 2));
        scene.Add(GeoPoint.OnPath("C", "c", 0));

        scene.Solve();
        Approx.Vec(new Vec3(2, 0, 0), scene.Point("C")!.Position, 1e-9);

        scene.Drive("C", 0.25);
        Approx.Vec(new Vec3(0, 2, 0), scene.Point("C")!.Position, 1e-9);

        scene.Drive("C", 0.5);
        Approx.Vec(new Vec3(-2, 0, 0), scene.Point("C")!.Position, 1e-9);
    }

    [Fact]
    public void 二级动点的轨迹是半径减半的圆()
    {
        // 经典题：A 为定点，C 在圆 O 上运动，D 是 AC 中点，求 D 的轨迹。
        var scene = new Scene();
        scene.Add(GeoPoint.Free("O", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("A", new Vec3(4, 0, 0)));
        scene.Add(GeoCircle.Fixed("c", "O", 2));

        var c = GeoPoint.OnPath("C", "c", 0);
        c.Driver = new Driver { Min = 0, Max = 1, Value = 0 };
        scene.Add(c);

        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "A", "C"));

        scene.Solve();

        // D 的轨迹应当是以 OA 中点为圆心、半径 1 的圆。
        var expectedCenter = new Vec3(2, 0, 0);
        const int samples = 72;

        for (var i = 0; i <= samples; i++)
        {
            scene.Drive("C", (double)i / samples);

            var d = scene.Point("D")!.Position;
            var radius = Vec3.Distance(d, expectedCenter);

            Assert.True(Math.Abs(radius - 1.0) < 1e-9,
                $"t={i}/{samples} 时 D={d}，到 {expectedCenter} 的距离是 {radius}，应当是 1");
        }
    }

    [Fact]
    public void 二级动点轨迹在圆上均匀取点时不退化()
    {
        // 顺便证明采样出来的轨迹不是一堆重合点 —— 否则"轨迹是个圆"会变成"轨迹是个点"。
        var scene = new Scene();
        scene.Add(GeoPoint.Free("O", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("A", new Vec3(4, 0, 0)));
        scene.Add(GeoCircle.Fixed("c", "O", 2));
        scene.Add(GeoPoint.OnPath("C", "c", 0));
        scene.Add(GeoPoint.Derived("D", DerivedKind.Midpoint, "A", "C"));

        scene.Solve();

        var positions = new List<Vec3>();
        for (var i = 0; i < 12; i++)
        {
            scene.Drive("C", i / 12.0);
            positions.Add(scene.Point("D")!.Position);
        }

        var distinct = positions.Distinct().Count();
        Assert.Equal(12, distinct);
    }

    [Fact]
    public void 拖动动点会同步驱动器_松手后不会跳回去()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(10, 0, 0)));
        scene.Add(GeoLine.Segment("AB", "A", "B"));

        var p = GeoPoint.OnPath("P", "AB", 0.5);
        p.Driver = new Driver { Min = 0, Max = 1, Value = 0.5 };
        scene.Add(p);

        scene.Solve();
        scene.Drag("P", new Vec3(8, 0, 0));

        // 再解一次，位置必须停在 8 附近，而不是弹回 5
        scene.Solve();
        Approx.Scalar(8, scene.Point("P")!.Position.X, 1e-3);
    }

    [Fact]
    public void 驱动器参数被限制在区间内()
    {
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(10, 0, 0)));
        scene.Add(GeoLine.Segment("AB", "A", "B"));

        var p = GeoPoint.OnPath("P", "AB", 0.5);
        p.Driver = new Driver { Min = 0.2, Max = 0.8, Value = 0.5 };
        scene.Add(p);

        scene.Solve();
        scene.Drive("P", 5.0);      // 远超上界

        Approx.Scalar(8, scene.Point("P")!.Position.X, 1e-6);
    }

    [Fact]
    public void 过定点的圆半径会跟着动点变()
    {
        // "以 A 为圆心、过 P 作圆"，P 一动圆就跟着变。
        var scene = new Scene();
        scene.Add(GeoPoint.Free("A", new Vec3(0, 0, 0)));
        scene.Add(GeoPoint.Free("B", new Vec3(10, 0, 0)));
        scene.Add(GeoLine.Segment("AB", "A", "B"));
        scene.Add(GeoPoint.OnPath("P", "AB", 0.5));
        scene.Add(GeoCircle.Through("c", "A", "P"));

        scene.Solve();
        Approx.Scalar(5, scene.Circle("c")!.ResolveRadius(scene));

        scene.Drag("P", new Vec3(3, 0, 0));
        Approx.Scalar(3, scene.Circle("c")!.ResolveRadius(scene), 1e-3);
    }
}
