namespace MathGeo.Core;

/// <summary>
/// 网格与坐标轴。
///
/// 三维图没有参照系就没法读 —— 学生看不出"这个点在哪个位置"、"这条棱朝哪个方向"。
/// 网格提供尺度感，坐标轴提供方向感，两者加起来才让立体图变成可以量的东西。
///
/// 渲染顺序很关键：网格和坐标轴**最先画**，于是半透明的面会盖在它上面。
/// 这正是课本里"透过体看到后面的网格"的观感；反过来画的话网格会浮在最前面，
/// 看起来像贴在屏幕上的一张纸。
/// </summary>
internal static class GridRenderer
{
    /// <summary>判断"正对着某个坐标面"的阈值：约 15°。</summary>
    private const double AxisAlignmentThreshold = 0.966;

    private enum Plane { Xy, Xz, Yz }

    // ————————————————————————— 二维 —————————————————————————

    public static void BuildPlanar(
        Scene scene, Func<Vec3, Vec2> project, Theme theme, List<Shape> shapes)
    {
        if (!scene.View.ShowGrid) return;

        AddPlane(scene, project, Plane.Xy, theme, shapes);
        AddAxes(scene, project, theme, shapes, threeDimensional: false);
    }

    // ————————————————————————— 三维 —————————————————————————

    public static void BuildSpatial(
        Scene scene, CameraView view, Theme theme, List<Shape> shapes)
    {
        if (!scene.View.ShowGrid) return;

        // z = 0 的 XY 网格：这是默认的那一层，也是老师最常要的那一层。
        AddPlane(scene, view.Project, Plane.Xy, theme, shapes);

        // 正对着某个坐标面时，补上那个面的网格。
        // 不补的话，从侧面看过去网格会退化成一条线，完全失去尺度感 ——
        // 那正是"三视图/正视图"最需要参照的时候。
        if (IsAligned(view, Vec3.UnitX)) AddPlane(scene, view.Project, Plane.Yz, theme, shapes);
        else if (IsAligned(view, Vec3.UnitY)) AddPlane(scene, view.Project, Plane.Xz, theme, shapes);

        AddAxes(scene, view.Project, theme, shapes, threeDimensional: true);
    }

    private static bool IsAligned(CameraView view, Vec3 axis)
        => Math.Abs(Vec3.Dot(view.Forward, axis)) >= AxisAlignmentThreshold;

    // ————————————————————————— 网格 —————————————————————————

    private static void AddPlane(
        Scene scene, Func<Vec3, Vec2> project, Plane plane, Theme theme, List<Shape> shapes)
    {
        var half = scene.View.HalfExtent;
        var step = Math.Max(scene.View.GridSpacing, 1e-6);
        var count = (int)Math.Ceiling(half / step);

        Vec3 At(double u, double v) => plane switch
        {
            Plane.Xy => new Vec3(u, v, 0),
            Plane.Xz => new Vec3(u, 0, v),
            _ => new Vec3(0, u, v),
        };

        for (var i = -count; i <= count; i++)
        {
            // 过原点的那两条留给坐标轴画，网格不重复画一遍。
            if (i == 0) continue;

            var t = i * step;

            shapes.Add(new LineShape(
                project(At(t, -half)), project(At(t, half)),
                theme.GridLine, theme.GridWidth));

            shapes.Add(new LineShape(
                project(At(-half, t)), project(At(half, t)),
                theme.GridLine, theme.GridWidth));
        }
    }

    // ————————————————————————— 坐标轴 —————————————————————————

    private static void AddAxes(
        Scene scene, Func<Vec3, Vec2> project, Theme theme, List<Shape> shapes, bool threeDimensional)
    {
        var half = scene.View.HalfExtent;

        AddAxis(project, new Vec3(half, 0, 0), theme.AxisX, "x", half, theme, shapes);
        AddAxis(project, new Vec3(0, half, 0), theme.AxisY, "y", half, theme, shapes);

        if (threeDimensional)
            AddAxis(project, new Vec3(0, 0, half), theme.AxisZ, "z", half, theme, shapes);
    }

    private static void AddAxis(
        Func<Vec3, Vec2> project, Vec3 end, string color, string name,
        double half, Theme theme, List<Shape> shapes)
    {
        var origin = project(Vec3.Zero);
        var tip = project(end);

        shapes.Add(new LineShape(origin, tip, color, theme.AxisWidth));

        var direction = (tip - origin).Normalized();
        if (direction.LengthSquared < 0.5) return;   // 轴正好指向相机，投影成一点

        // 箭头尺寸按网格尺度取：换一个尺寸的图不用重新调参，
        // 也不会出现"大图里箭头小得看不见、小图里箭头糊成一团"。
        var head = half * 0.055;
        var perpendicular = new Vec2(-direction.Y, direction.X);
        var back = tip - direction * head;

        shapes.Add(new PolygonShape(
            [tip, back + perpendicular * (head * 0.38), back - perpendicular * (head * 0.38)],
            color,
            null,
            0));

        shapes.Add(new LabelShape(
            tip + direction * (head * 1.15),
            name,
            LabelAnchor.Center,
            color,
            theme.LabelSize * 0.9,
            Italic: false));
    }
}
