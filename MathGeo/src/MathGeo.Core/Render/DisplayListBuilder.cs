namespace MathGeo.Core;

/// <summary>
/// 把对象图翻译成显示列表。
///
/// 两条路径，共用同一个出口（显示列表）：
///   二维 —— 世界坐标直接就是绘图坐标，无限长的线裁到视野框里；
///   三维 —— 先经相机投影，再交给隐藏线渲染器判实线/虚线，然后面填充按深度排序。
///
/// 这一层不做任何"求解"，只做呈现决策。求解失败的对象会被跳过（而不是画出错位的东西），
/// 错误已经由求解器记成诊断了。
/// </summary>
internal static class DisplayListBuilder
{
    /// <summary>射线/直线被裁剪时，视野框向外放宽的比例。</summary>
    private const double ClipMargin = 0.35;

    public static IReadOnlyList<Shape> Build(Scene scene, Theme theme)
    {
        var solids = CollectSolids(scene);

        var shapes = solids.Count > 0
            ? BuildThreeDimensional(scene, solids, theme)
            : BuildTwoDimensional(scene, theme);

        // Y 轴反转用后处理，而不是让二维和三维各写一套：
        // 整个视图（图形 + 网格 + 坐标轴 + 标签）一起上下翻转，行为完全一致。
        return scene.View.InvertY ? [.. shapes.Select(FlipVertically)] : shapes;
    }

    /// <summary>把整个视图上下翻转。锚点方向也要跟着翻，否则标签会跑到错误的一侧。</summary>
    private static Shape FlipVertically(Shape shape) => shape switch
    {
        LineShape line => line with { A = Flip(line.A), B = Flip(line.B) },
        PolygonShape polygon => polygon with { Points = [.. polygon.Points.Select(Flip)] },
        CircleShape circle => circle with { Center = Flip(circle.Center) },
        DotShape dot => dot with { At = Flip(dot.At) },
        LabelShape label => label with { At = Flip(label.At), Anchor = FlipAnchor(label.Anchor) },

        // 圆弧：关于 X 轴镜像把角度 θ 变成 -θ，起始角取反、扫过角也取反。
        ArcShape arc => arc with
        {
            Center = Flip(arc.Center),
            StartRad = -arc.StartRad,
            SweepRad = -arc.SweepRad,
        },

        _ => shape,
    };

    private static Vec2 Flip(Vec2 point) => new(point.X, -point.Y);

    private static LabelAnchor FlipAnchor(LabelAnchor anchor) => anchor switch
    {
        LabelAnchor.Top => LabelAnchor.Bottom,
        LabelAnchor.Bottom => LabelAnchor.Top,
        LabelAnchor.TopLeft => LabelAnchor.BottomLeft,
        LabelAnchor.BottomLeft => LabelAnchor.TopLeft,
        LabelAnchor.TopRight => LabelAnchor.BottomRight,
        LabelAnchor.BottomRight => LabelAnchor.TopRight,
        _ => anchor,   // Center / Left / Right 在上下翻转下不变
    };

    // ——————————————————————————— 二维 ———————————————————————————

    private static List<Shape> BuildTwoDimensional(Scene scene, Theme theme)
    {
        var shapes = new List<Shape>(scene.Objects.Count * 2);

        // 网格与坐标轴最先画，于是图形会盖在它上面 —— 这正是课本里"透过图形看到网格"的观感。
        GridRenderer.BuildPlanar(scene, point => point.XY, theme, shapes);

        // 第一遍：先量出"有限图形"的范围。无限长的直线要靠它决定画多长 ——
        // 否则一条直线会把包围盒撑到几千个单位，整张图缩成一个点。
        var clipBox = ComputeFiniteBounds(scene);

        foreach (var obj in scene.Objects)
        {
            if (!obj.Visible) continue;

            switch (obj)
            {
                case GeoPolygon polygon:
                    AddPolygon(scene, polygon, theme, shapes);
                    break;

                case GeoCircle circle:
                    AddCircle(scene, circle, theme, shapes);
                    break;

                case GeoLine line:
                    AddLine(scene, line, theme, clipBox, shapes);
                    break;

                case GeoAngle angle:
                    AddAngle(scene, angle, point => point.XY, theme, shapes);
                    break;
            }
        }

        AddPointsAndLabels(scene, theme, shapes, p => p.XY, ComputePointCentroid(scene, p => p.XY));
        return shapes;
    }

    // ——————————————————————————— 三维 ———————————————————————————

    private static List<Shape> BuildThreeDimensional(Scene scene, List<SolidGeometry> solids, Theme theme)
    {
        var shapes = new List<Shape>(solids.Count * 40);

        // 相机默认绕着整个场景的重心转 —— 老师拖一下就该"绕着体转"，
        // 而不是绕着一个跟图无关的世界原点转。Camera.Target 是相对重心的偏移。
        var centroid = ComputeWorldCentroid(scene);
        var camera = (scene.Camera ?? Camera.Textbook) with { Target = centroid + (scene.Camera?.Target ?? Vec3.Zero) };
        var view = camera.CreateView();

        // 球面线框当辅助曲线交给隐藏线渲染器：被体挡住的部分会自动变成虚线。
        // 这一步是"球的切接"能从背结论变成看出来的关键 —— 球真的在体上。
        var spheres = scene.Objects
            .OfType<GeoSphere>()
            .Where(s => s.Visible && s.IsSolved && s.Radius > 0)
            .ToList();

        var curves = new List<OccludedCurve>(spheres.Count * 4);

        foreach (var sphere in spheres)
            foreach (var circle in SphereFit.Wireframe(sphere.Center, sphere.Radius, sphere.Meridians))
                curves.Add(new OccludedCurve(circle, theme.HelperStroke, theme.HelperWidth));

        // 网格与坐标轴最先画：半透明的面会盖在它上面，
        // 于是"透过体看到后面的网格"，立体图才有空间感。
        GridRenderer.BuildSpatial(scene, view, theme, shapes);

        HiddenLineRenderer.Build(solids, curves, view, theme, shapes);

        foreach (var sphere in spheres)
            AddSphereAnnotations(scene, sphere, view, theme, shapes);

        // 体之外的辅助对象（对角线、高、外接圆的半径……）也要跟着投影，
        // 否则一转到侧面就会画到错误的位置上。
        foreach (var obj in scene.Objects)
        {
            if (!obj.Visible) continue;

            switch (obj)
            {
                case GeoLine line:
                {
                    var a = scene.Point(line.AId);
                    var b = scene.Point(line.BId);
                    if (a is null || b is null) break;

                    var style = ResolveStyle(line.Style, theme);
                    shapes.Add(new LineShape(
                        view.Project(a.Position), view.Project(b.Position),
                        style.Stroke, style.Width, style.Dash));
                    break;
                }

                case GeoCircle circle:
                    AddProjectedCircle(scene, circle, view, theme, shapes);
                    break;

                case GeoPolygon polygon:
                    AddProjectedPolygon(scene, polygon, view, theme, shapes);
                    break;

                case GeoSection section:
                    AddSection(scene, section, solids, view, theme, shapes);
                    break;

                case GeoDihedral dihedral:
                    AddDihedral(dihedral, solids, view, theme, shapes);
                    break;

                case GeoAngle angle:
                    AddAngle(scene, angle, view.Project, theme, shapes);
                    break;

                case GeoThreeViews threeViews:
                    ThreeViewRenderer.Build(solids, threeViews, theme, shapes);
                    break;
            }
        }

        var projectedCentroid = ComputePointCentroid(scene, p => view.Project(p));
        AddPointsAndLabels(scene, theme, shapes, p => view.Project(p), projectedCentroid);
        return shapes;
    }

    private static List<SolidGeometry> CollectSolids(Scene scene)
    {
        var result = new List<SolidGeometry>();

        foreach (var obj in scene.Objects)
        {
            if (obj is not GeoPolyhedron { Visible: true } poly) continue;

            var vertices = new Vec3[poly.VertexIds.Count];
            var complete = true;

            for (var i = 0; i < vertices.Length; i++)
            {
                var point = scene.Point(poly.VertexIds[i]);
                if (point is null) { complete = false; break; }
                vertices[i] = point.Position;
            }

            if (complete) result.Add(new SolidGeometry(poly, vertices));
        }

        return result;
    }

    /// <summary>
    /// 三维里的圆投影后是椭圆，没有现成的 SVG 图元。
    /// 采样成折线最省事，而且和 SVG 导出的结果一致（屏幕上也是这么画的）。
    /// </summary>
    private static void AddProjectedCircle(
        Scene scene, GeoCircle circle, CameraView view, Theme theme, List<Shape> shapes)
    {
        var center = scene.Point(circle.CenterId);
        if (center is null) return;

        var radius = circle.ResolveRadius(scene);
        var style = ResolveStyle(circle.Style, theme);

        const int samples = 72;
        var previous = Vec2.Zero;

        for (var i = 0; i <= samples; i++)
        {
            var angle = 2 * Math.PI * i / samples;
            var world = new Vec3(
                center.Position.X + radius * Math.Cos(angle),
                center.Position.Y + radius * Math.Sin(angle),
                center.Position.Z);

            var screen = view.Project(world);
            if (i > 0) shapes.Add(new LineShape(previous, screen, style.Stroke, style.Width, style.Dash));
            previous = screen;
        }
    }

    /// <summary>球心、半径、数值。半径线段画到第一个顶点方向上 —— 外接球的半径本来就该"落到顶点上"。</summary>
    private static void AddSphereAnnotations(
        Scene scene, GeoSphere sphere, CameraView view, Theme theme, List<Shape> shapes)
    {
        var projected = view.Project(sphere.Center);

        if (sphere.ShowCenter)
        {
            shapes.Add(new DotShape(projected, theme.PointRadius, theme.HighlightStroke));
            shapes.Add(new LabelShape(projected, sphere.Label ?? "O", LabelAnchor.BottomLeft,
                theme.HighlightStroke, theme.LabelSize));
        }

        if (sphere.ShowRadius && scene.Find(sphere.SolidId) is GeoPolyhedron solid
                               && SphereTangent(scene, sphere, solid) is { } tangent)
        {
            // 半径线段本身就是定义：外接球连到顶点，内切球垂直于面连到切点。
            shapes.Add(new LineShape(
                projected, view.Project(tangent), theme.HighlightStroke, theme.HelperWidth));
        }

        if (!sphere.ShowValue) return;

        var name = sphere.Kind == SphereKind.Circumscribed ? "R" : "r";
        var text = $"{name} = {sphere.Radius.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";

        shapes.Add(new LabelShape(
            view.Project(sphere.Center + new Vec3(0, sphere.Radius, 0)),
            text,
            LabelAnchor.Top,
            theme.HighlightStroke,
            theme.LabelSize * 0.85,
            Italic: false));
    }

    /// <summary>
    /// 半径线段的另一个端点。
    /// 外接球落到第一个顶点上；内切球垂直于第一个面、落到切点上 ——
    /// 两者不能混：内切球连到顶点就比半径长了，图会画错。
    /// </summary>
    private static Vec3? SphereTangent(Scene scene, GeoSphere sphere, GeoPolyhedron solid)
    {
        if (sphere.Kind == SphereKind.Circumscribed)
        {
            foreach (var vertexId in solid.VertexIds)
                if (scene.Point(vertexId) is { } vertex)
                    return vertex.Position;

            return null;
        }

        foreach (var face in solid.Faces)
        {
            if (face.Length < 3) continue;

            var a = scene.Point(solid.VertexIds[face[0]])?.Position;
            var b = scene.Point(solid.VertexIds[face[1]])?.Position;
            var c = scene.Point(solid.VertexIds[face[2]])?.Position;

            if (a is null || b is null || c is null) continue;

            var normal = Vec3.Cross(b.Value - a.Value, c.Value - a.Value).Normalized();
            if (normal.LengthSquared < 0.5) continue;

            return sphere.Center + normal * sphere.Radius;
        }

        return null;
    }

    private static void AddProjectedPolygon(
        Scene scene, GeoPolygon polygon, CameraView view, Theme theme, List<Shape> shapes)
    {
        var points = new List<Vec2>(polygon.VertexIds.Count);

        foreach (var id in polygon.VertexIds)
        {
            var vertex = scene.Point(id);
            if (vertex is null) return;
            points.Add(view.Project(vertex.Position));
        }

        if (points.Count < 3) return;

        shapes.Add(new PolygonShape(
            points,
            polygon.Filled ? theme.PolygonFill : null,
            theme.PolygonStroke,
            theme.LineWidth));
    }

    /// <summary>
    /// 截面。画在面和棱之后，也就是叠在最上面 —— 它是"答案"，
    /// 被半透明的面盖住反而看不清交线在哪。
    /// </summary>
    private static void AddSection(
        Scene scene, GeoSection section, List<SolidGeometry> solids,
        CameraView view, Theme theme, List<Shape> shapes)
    {
        if (!solids.Any(s => s.Source.Id == section.SolidId)) return;

        var polygon = CrossSection.Of(scene, section);
        if (polygon is null) return;

        var projected = new List<Vec2>(polygon.Count);
        foreach (var vertex in polygon) projected.Add(view.Project(vertex));

        shapes.Add(new PolygonShape(
            projected,
            section.Filled ? theme.SectionFill : null,
            theme.SectionStroke,
            theme.LineWidth * 1.2));
    }

    /// <summary>
    /// 二面角的平面角。
    ///
    /// 平面角的顶点取在棱的中点，两条边分别指向两个面内部 —— 这就是平面角的定义。
    /// 直角符号、圆弧、度数标签全部复用通用角标注，这里只负责把三个点算出来。
    /// </summary>
    private static void AddDihedral(
        GeoDihedral dihedral, List<SolidGeometry> solids,
        CameraView view, Theme theme, List<Shape> shapes)
    {
        var solid = solids.FirstOrDefault(s => s.Source.Id == dihedral.SolidId);
        if (solid is null) return;

        var geometry = Dihedral.Of(solid.Vertices, solid.Source.Faces, dihedral.FaceA, dihedral.FaceB);
        if (geometry is null) return;

        AngleMark.Build(
            geometry.Vertex,
            geometry.Vertex + geometry.DirectionA,
            geometry.Vertex + geometry.DirectionB,
            geometry.EdgeLength * dihedral.RadiusRatio,
            dihedral.ShowValue,
            view.Project,
            theme,
            shapes);
    }

    /// <summary>通用角标注。二维三维共用同一份绘制，只是投影函数不同。</summary>
    private static void AddAngle(
        Scene scene, GeoAngle angle, Func<Vec3, Vec2> project, Theme theme, List<Shape> shapes)
    {
        var vertex = scene.Point(angle.VertexId);
        var from = scene.Point(angle.FromId);
        var to = scene.Point(angle.ToId);
        if (vertex is null || from is null || to is null) return;

        // 半径按两条边中较短者取比例：边短的时候圆弧才不会比边本身还长。
        var shortest = Math.Min(
            Vec3.Distance(vertex.Position, from.Position),
            Vec3.Distance(vertex.Position, to.Position));

        AngleMark.Build(
            vertex.Position, from.Position, to.Position,
            shortest * angle.RadiusRatio, angle.ShowValue, project, theme, shapes);
    }

    private static Vec3 ComputeWorldCentroid(Scene scene)
    {
        double x = 0, y = 0, z = 0;
        var count = 0;

        foreach (var obj in scene.Objects)
        {
            if (obj is not GeoPoint { Visible: true } point) continue;
            x += point.Position.X;
            y += point.Position.Y;
            z += point.Position.Z;
            count++;
        }

        return count == 0 ? Vec3.Zero : new Vec3(x / count, y / count, z / count);
    }

    // ————————————————————————— 共用部件 —————————————————————————

    private static void AddPointsAndLabels(
        Scene scene, Theme theme, List<Shape> shapes,
        Func<Vec3, Vec2> project, Vec2 centroid)
    {
        foreach (var obj in scene.Objects)
        {
            if (!obj.Visible || obj is not GeoPoint point) continue;

            var at = project(point.Position);
            shapes.Add(new DotShape(at, theme.PointRadius, ResolveStyle(point.Style, theme).Stroke));

            if (point.Label is { Length: > 0 } label)
            {
                shapes.Add(new LabelShape(
                    at, label, ChooseAnchor(at, centroid), theme.LabelFill, theme.LabelSize));
            }
        }
    }

    private static Vec2 ComputePointCentroid(Scene scene, Func<Vec3, Vec2> project)
    {
        double sumX = 0, sumY = 0;
        var count = 0;

        foreach (var obj in scene.Objects)
        {
            if (obj is not GeoPoint { Visible: true } point) continue;
            var at = project(point.Position);
            sumX += at.X;
            sumY += at.Y;
            count++;
        }

        return count == 0 ? Vec2.Zero : new Vec2(sumX / count, sumY / count);
    }

    /// <summary>只统计有限对象的范围：点、线段、圆、多边形。</summary>
    private static Bounds2 ComputeFiniteBounds(Scene scene)
    {
        var finite = new List<Shape>();

        foreach (var obj in scene.Objects)
        {
            if (!obj.Visible) continue;

            switch (obj)
            {
                case GeoPoint point:
                    finite.Add(new DotShape(point.Position.XY, 0, "#000"));
                    break;

                case GeoLine { Kind: LineKind.Segment } line:
                {
                    var a = scene.Point(line.AId);
                    var b = scene.Point(line.BId);
                    if (a is not null && b is not null)
                        finite.Add(new LineShape(a.Position.XY, b.Position.XY, "#000", 1));
                    break;
                }

                case GeoCircle circle:
                {
                    var center = scene.Point(circle.CenterId);
                    if (center is not null)
                        finite.Add(new CircleShape(center.Position.XY, circle.ResolveRadius(scene), null, null, 0));
                    break;
                }

                case GeoPolygon polygon:
                {
                    var points = polygon.VertexIds
                        .Select(id => scene.Point(id))
                        .Where(p => p is not null)
                        .Select(p => p!.Position.XY)
                        .ToList();
                    if (points.Count >= 3) finite.Add(new PolygonShape(points, null, null, 0));
                    break;
                }
            }
        }

        var bounds = Bounds2.Of(finite);
        if (!double.IsFinite(bounds.MinX) || bounds.Width <= 0 && bounds.Height <= 0)
            return new Bounds2(-1, -1, 1, 1);

        var margin = Math.Max(bounds.Width, bounds.Height) * ClipMargin + 1;
        return new Bounds2(bounds.MinX - margin, bounds.MinY - margin, bounds.MaxX + margin, bounds.MaxY + margin);
    }

    private static void AddPolygon(Scene scene, GeoPolygon polygon, Theme theme, List<Shape> shapes)
    {
        var points = new List<Vec2>(polygon.VertexIds.Count);
        foreach (var id in polygon.VertexIds)
        {
            var vertex = scene.Point(id);
            if (vertex is null) return;   // 顶点缺失：整块不画，诊断已在求解阶段记录
            points.Add(vertex.Position.XY);
        }

        if (points.Count < 3) return;

        shapes.Add(new PolygonShape(
            points,
            polygon.Filled ? theme.PolygonFill : null,
            theme.PolygonStroke,
            theme.LineWidth));
    }

    private static void AddCircle(Scene scene, GeoCircle circle, Theme theme, List<Shape> shapes)
    {
        var center = scene.Point(circle.CenterId);
        if (center is null) return;

        var style = ResolveStyle(circle.Style, theme);
        shapes.Add(new CircleShape(
            center.Position.XY,
            circle.ResolveRadius(scene),
            null,
            style.Stroke,
            style.Width));
    }

    private static void AddLine(Scene scene, GeoLine line, Theme theme, Bounds2 clipBox, List<Shape> shapes)
    {
        var a = scene.Point(line.AId);
        var b = scene.Point(line.BId);
        if (a is null || b is null) return;

        var clipped = ClipToBox(a.Position.XY, b.Position.XY, line.Kind, clipBox);
        if (clipped is null) return;

        var style = ResolveStyle(line.Style, theme);
        shapes.Add(new LineShape(clipped.Value.From, clipped.Value.To, style.Stroke, style.Width, style.Dash));
    }

    /// <summary>
    /// Liang–Barsky 裁剪：把线段/射线/直线裁到视野框里。
    /// 直线在数学上是无限的，但画布不是 —— 这一步让"作一条直线"这种条件能正常出图。
    /// </summary>
    private static (Vec2 From, Vec2 To)? ClipToBox(Vec2 a, Vec2 b, LineKind kind, Bounds2 box)
    {
        var d = b - a;

        var t0 = kind == LineKind.Ray ? 0.0 : double.NegativeInfinity;
        var t1 = double.PositiveInfinity;

        if (!ClipAxis(a.X, d.X, box.MinX, box.MaxX, ref t0, ref t1)) return null;
        if (!ClipAxis(a.Y, d.Y, box.MinY, box.MaxY, ref t0, ref t1)) return null;

        if (kind == LineKind.Segment)
        {
            t0 = Math.Max(t0, 0);
            t1 = Math.Min(t1, 1);
        }

        if (t1 < t0) return null;

        return (new Vec2(a.X + d.X * t0, a.Y + d.Y * t0),
                new Vec2(a.X + d.X * t1, a.Y + d.Y * t1));
    }

    private static bool ClipAxis(double origin, double delta, double min, double max, ref double t0, ref double t1)
    {
        if (Math.Abs(delta) < MathUtil.Epsilon)
            return origin >= min && origin <= max;   // 与该轴平行：只要在范围内就整条保留

        var tA = (min - origin) / delta;
        var tB = (max - origin) / delta;
        if (tA > tB) (tA, tB) = (tB, tA);

        t0 = Math.Max(t0, tA);
        t1 = Math.Min(t1, tB);
        return t0 <= t1;
    }

    private static (string Stroke, double Width, double[]? Dash) ResolveStyle(string? style, Theme theme)
        => style switch
        {
            "helper" => (theme.HelperStroke, theme.HelperWidth, theme.HelperDash),
            "answer" => (theme.AnswerStroke, theme.LineWidth * 1.15, null),
            "highlight" => (theme.HighlightStroke, theme.LineWidth * 1.15, null),
            _ => (theme.LineStroke, theme.LineWidth, null),
        };

    /// <summary>
    /// 标签往"远离图形重心"的方向放 —— 这是课本排版的习惯做法，
    /// 也是"标签不压线、不互相叠"最简单有效的启发式。
    /// 立体图旋转时重心跟着投影走，所以标签方向会自动保持合理。
    /// </summary>
    private static LabelAnchor ChooseAnchor(Vec2 at, Vec2 centroid)
    {
        var dx = at.X - centroid.X;
        var dy = at.Y - centroid.Y;

        if (Math.Abs(dx) < MathUtil.Epsilon && Math.Abs(dy) < MathUtil.Epsilon)
            return LabelAnchor.TopRight;   // 点正好在重心上，给个固定方向

        var degrees = MathUtil.RadToDeg(Math.Atan2(dy, dx));

        return degrees switch
        {
            >= -22.5 and < 22.5 => LabelAnchor.Right,
            >= 22.5 and < 67.5 => LabelAnchor.TopRight,
            >= 67.5 and < 112.5 => LabelAnchor.Top,
            >= 112.5 and < 157.5 => LabelAnchor.TopLeft,
            >= 157.5 or < -157.5 => LabelAnchor.Left,
            >= -157.5 and < -112.5 => LabelAnchor.BottomLeft,
            >= -112.5 and < -67.5 => LabelAnchor.Bottom,
            _ => LabelAnchor.BottomRight,
        };
    }
}
