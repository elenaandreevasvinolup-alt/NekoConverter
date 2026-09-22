namespace MathGeo.Core;

/// <summary>
/// 三视图渲染。
///
/// 做法很直接：用正交相机把体分别从三个方向投影一遍，再把三份显示列表平移到
/// 各自的版面上。因为显示列表就是屏幕无关的，所以"三视图"在这里不是一种特殊的渲染，
/// 只是三次普通渲染加一次平移。
///
/// 版面按第一角投影法（中国教材）：
///
///     正视图   侧视图
///     俯视图
///
/// 三者共用同一比例（正交投影下世界单位就是屏幕单位），于是
/// "长对正、高平齐、宽相等"自动成立 —— 不需要手工对齐，也不会因为
/// 某个视图的尺寸算错而错位。
/// </summary>
internal static class ThreeViewRenderer
{
    public static void Build(
        IReadOnlyList<SolidGeometry> solids, GeoThreeViews views, Theme theme, List<Shape> shapes)
    {
        if (solids.Count == 0) return;

        // 只保留被点名的体。没点名就全都画。
        var selected = views.SolidIds.Count == 0
            ? solids
            : [.. solids.Where(s => views.SolidIds.Contains(s.Source.Id))];

        if (selected.Count == 0) return;

        var front = ProjectView(selected, 90, 0, theme);
        var side = ProjectView(selected, 180, 0, theme);
        var top = ProjectView(selected, 90, 90, theme);

        if (front.Shapes.Count == 0 && side.Shapes.Count == 0 && top.Shapes.Count == 0) return;

        // 间距按整体尺寸取，换一个尺寸的体不用重新调参。
        var extent = Math.Max(
            Math.Max(front.Bounds.Width, side.Bounds.Width),
            Math.Max(front.Bounds.Width, front.Bounds.Height));
        var gap = Math.Max(extent, 1) * views.Gap;

        // 整个版面的平移：把三视图摆到立体图旁边，而不是压在它身上。
        var offsetX = views.Offset.X;
        var offsetY = views.Offset.Y;

        // 正视图不动（只受版面偏移影响）。
        AddShifted(front, offsetX, offsetY, shapes);
        AddName(front, Localizer.Current["view.front"], views, theme, offsetX, offsetY, shapes);

        // 侧视图放右边，顶边与正视图对齐（高平齐）。
        var sideDx = offsetX + front.Bounds.MaxX + gap - side.Bounds.MinX;
        var sideDy = offsetY + front.Bounds.MaxY - side.Bounds.MaxY;
        AddShifted(side, sideDx, sideDy, shapes);
        AddName(side, Localizer.Current["view.side"], views, theme, sideDx, sideDy, shapes);

        // 俯视图放下方，左边与正视图对齐（长对正）。
        var topDx = offsetX + front.Bounds.MinX - top.Bounds.MinX;
        var topDy = offsetY + front.Bounds.MinY - gap - top.Bounds.MaxY;
        AddShifted(top, topDx, topDy, shapes);
        AddName(top, Localizer.Current["view.top"], views, theme, topDx, topDy, shapes);
    }

    private static ViewProjection ProjectView(
        IReadOnlyList<SolidGeometry> solids, double azimuth, double elevation, Theme theme)
    {
        var camera = new Camera
        {
            AzimuthDeg = azimuth,
            ElevationDeg = elevation,
            Orthographic = true,
            Distance = 100,
        };

        var local = new List<Shape>();
        HiddenLineRenderer.Build(solids, [], camera.CreateView(), theme, local);

        // 三视图是线框图：面填充会把棱盖住，投影图就看不明白了。
        // 注意深度缓冲仍然是用面算的（隐藏线判定需要），这里只是不画出来。
        local.RemoveAll(shape => shape is PolygonShape);

        return new ViewProjection(local, Bounds2.Of(local));
    }

    private static void AddShifted(ViewProjection view, double dx, double dy, List<Shape> shapes)
    {
        foreach (var shape in view.Shapes)
            shapes.Add(Shift(shape, dx, dy));
    }

    private static void AddName(
        ViewProjection view, string name, GeoThreeViews views, Theme theme,
        double dx, double dy, List<Shape> shapes)
    {
        if (!views.ShowNames) return;

        var center = new Vec2(
            (view.Bounds.MinX + view.Bounds.MaxX) / 2 + dx,
            view.Bounds.MinY + dy);

        // 名称放在视图正下方，再留一点空隙，避免压住最下面那条棱。
        var offset = Math.Max(view.Bounds.Height, 1) * 0.12 + 0.2;

        shapes.Add(new LabelShape(
            new Vec2(center.X, center.Y - offset),
            name,
            LabelAnchor.Top,
            theme.HelperStroke,
            theme.LabelSize * 0.85,
            Italic: false));
    }

    private static Shape Shift(Shape shape, double dx, double dy)
    {
        Vec2 Move(Vec2 point) => new(point.X + dx, point.Y + dy);

        return shape switch
        {
            LineShape line => line with { A = Move(line.A), B = Move(line.B) },
            PolygonShape polygon => polygon with { Points = [.. polygon.Points.Select(Move)] },
            CircleShape circle => circle with { Center = Move(circle.Center) },
            DotShape dot => dot with { At = Move(dot.At) },
            LabelShape label => label with { At = Move(label.At) },
            ArcShape arc => arc with { Center = Move(arc.Center) },
            _ => shape,
        };
    }

    private sealed record ViewProjection(IReadOnlyList<Shape> Shapes, Bounds2 Bounds);
}
