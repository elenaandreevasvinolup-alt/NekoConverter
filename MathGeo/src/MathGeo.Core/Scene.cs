namespace MathGeo.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// 一条求解诊断。
///
/// 注意它**只带 code 和参数，不带文案**。文案在渲染时按当前语言查表。
/// 内核不做"弹错误框"这种事 —— 它只把问题记下来交给 UI，
/// UI 再翻译成"未识别的条件芯片"之类的可修复提示。这是无 AI 也要好用的一部分。
/// </summary>
public sealed record SolveDiagnostic(
    DiagnosticSeverity Severity, string? ObjectId, string Code, IReadOnlyList<string> Args)
{
    /// <summary>按当前语言渲染的消息。</summary>
    public string Message => DiagnosticText.Format(Code, Args);

    /// <summary>按指定语言渲染。导出、预览这类"要固定语言"的场景用它。</summary>
    public string Localize(Localizer localizer)
        => localizer.Format(DiagnosticText.KeyFor(Code), [.. Args.Cast<object?>()]);

    public override string ToString() => $"[{Severity}] {Code} {ObjectId ?? "-"}: {Message}";

    public static SolveDiagnostic Error(string? objectId, string code, params object?[] args)
        => new(DiagnosticSeverity.Error, objectId, code, Stringify(args));

    public static SolveDiagnostic Warning(string? objectId, string code, params object?[] args)
        => new(DiagnosticSeverity.Warning, objectId, code, Stringify(args));

    public static SolveDiagnostic Info(string? objectId, string code, params object?[] args)
        => new(DiagnosticSeverity.Info, objectId, code, Stringify(args));

    private static IReadOnlyList<string> Stringify(object?[] args)
        => args.Length == 0 ? [] : [.. args.Select(a => a?.ToString() ?? string.Empty)];
}

public sealed class SolveResult
{
    public bool Success { get; init; }
    public IReadOnlyList<SolveDiagnostic> Diagnostics { get; init; } = [];

    public IEnumerable<SolveDiagnostic> Errors
        => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public static SolveResult Ok { get; } = new() { Success = true };
}

/// <summary>
/// 一张图。对象图 + 求解 + 渲染入口。
///
/// 这个类是内核的门面：UI 只跟它打交道，而它对 UI 一无所知。
/// </summary>
public sealed class Scene
{
    private readonly Dictionary<string, GeoObject> _byId = new(StringComparer.Ordinal);
    private readonly List<GeoObject> _order = [];

    public IReadOnlyList<GeoObject> Objects => _order;
    public IReadOnlyDictionary<string, GeoObject> ById => _byId;

    public bool IsSolved { get; internal set; }

    /// <summary>
    /// 三维相机。场景里一旦出现多面体就走三维渲染路径；相机为 null 时用课本默认视角。
    /// 二维场景保持为 null，二维路径不会为此多付任何代价。
    /// </summary>
    public Camera? Camera { get; set; }

    /// <summary>
    /// 视图显示选项（网格、坐标轴、Y 轴方向）。
    /// 和 Camera 一样属于"怎么看"，不写进题目文件 —— 它是每台机器上的个人偏好。
    /// </summary>
    public ViewSettings View { get; set; } = ViewSettings.Default;

    /// <summary>最近一次求解的诊断。UI 用它画"待修复条件"。</summary>
    public IReadOnlyList<SolveDiagnostic> LastDiagnostics { get; internal set; } = [];

    public void Add(GeoObject obj)
    {
        if (!_byId.TryAdd(obj.Id, obj))
            throw new InvalidOperationException($"对象 Id 重复：'{obj.Id}'");
        _order.Add(obj);
    }

    public void AddRange(IEnumerable<GeoObject> objects)
    {
        foreach (var obj in objects) Add(obj);
    }

    /// <summary>
    /// 移除一个对象。
    /// 给"临时往场景里加东西"用 —— 比如点视角立方体中间的方块切进三视图，
    /// 再点一次切回来，那份三视图对象要能被摘掉，而不是永久留在题目里。
    /// </summary>
    public bool Remove(string id)
    {
        if (!_byId.Remove(id)) return false;
        _order.RemoveAll(obj => string.Equals(obj.Id, id, StringComparison.Ordinal));
        return true;
    }

    public GeoObject? Find(string id) => _byId.GetValueOrDefault(id);

    public GeoPoint? Point(string id) => Find(id) as GeoPoint;

    public GeoLine? Line(string id) => Find(id) as GeoLine;

    public GeoCircle? Circle(string id) => Find(id) as GeoCircle;

    public GeoPolygon? Polygon(string id) => Find(id) as GeoPolygon;

    public SolveResult Solve()
    {
        var result = Solver.Solve(this);
        IsSolved = result.Success;
        LastDiagnostics = result.Diagnostics;
        return result;
    }

    /// <summary>
    /// 拖动一个点。自由点直接改坐标；路径上的点则投影到路径上，只更新参数。
    ///
    /// 这个方法是"活图"的唯一入口 —— 老师拖一下，派生对象在 Solve() 里自动跟上。
    /// </summary>
    public SolveResult Drag(string pointId, Vec3 target)
    {
        var point = Point(pointId)
            ?? throw new InvalidOperationException($"找不到点 '{pointId}'");

        switch (point.Kind)
        {
            case PointKind.Free:
                point.FreePosition = target;
                break;

            case PointKind.OnPath:
            {
                var path = Find(point.PathId!)
                    ?? throw new InvalidOperationException($"点 '{pointId}' 的路径 '{point.PathId}' 不存在");
                var param = ProjectOntoPath(path, target);
                point.Param = param;

                // 拖动动点等于拖动它的滑块：驱动器和参数必须同步，
                // 否则松手后一播放动画，点会跳回旧位置。
                if (point.Driver is { } driver)
                    driver.Value = MathUtil.Clamp(param, driver.Min, driver.Max);
                break;
            }

            default:
                throw new InvalidOperationException($"派生点 '{pointId}' 不能直接拖动，只能拖动它的父对象");
        }

        return Solve();
    }

    /// <summary>
    /// 把一个世界坐标投影成路径参数。先粗采样再局部细化：
    /// 教学规模下足够准，而且对任何路径类型都成立，不需要每加一种曲线就写一套解析投影。
    /// </summary>
    public double ProjectOntoPath(GeoObject path, Vec3 target, int coarseSamples = 256)
    {
        var best = 0.0;
        var bestDistance = double.PositiveInfinity;

        for (var i = 0; i <= coarseSamples; i++)
        {
            var t = (double)i / coarseSamples;
            var d = Vec3.Distance(PathGeometry.PointAt(this, path, t), target);
            if (d < bestDistance) { bestDistance = d; best = t; }
        }

        // 局部细化：在最优采样点附近再扫两轮，把误差压到视觉上不可见。
        var span = 1.0 / coarseSamples;
        for (var round = 0; round < 2; round++)
        {
            var lo = Math.Max(0, best - span);
            var hi = Math.Min(1, best + span);
            span /= 4;
            for (var i = 0; i <= 16; i++)
            {
                var t = lo + (hi - lo) * i / 16.0;
                var d = Vec3.Distance(PathGeometry.PointAt(this, path, t), target);
                if (d < bestDistance) { bestDistance = d; best = t; }
            }
        }

        return best;
    }

    /// <summary>驱动一个动点并重解。播放动画时每帧调它。</summary>
    public SolveResult Drive(string pointId, double value)
    {
        var point = Point(pointId)
            ?? throw new InvalidOperationException($"找不到点 '{pointId}'");
        if (point.Kind != PointKind.OnPath)
            throw new InvalidOperationException($"只有路径上的点可以被驱动：'{pointId}'");

        // 驱动器是参数的权威来源，所以驱动必须写回驱动器，不能只改 Param
        // —— 求解器会从驱动器读回参数，只改 Param 会被下一帧覆盖掉。
        if (point.Driver is { } driver)
        {
            driver.Value = MathUtil.Clamp(value, driver.Min, driver.Max);
            point.Param = driver.Value;
        }
        else
        {
            point.Param = value;
        }

        return Solve();
    }

    public IReadOnlyList<Shape> BuildDisplayList(Theme? theme = null)
        => DisplayListBuilder.Build(this, theme ?? Theme.Textbook);

    public Bounds2 ComputeBounds()
        => Bounds2.Of(BuildDisplayList());

    /// <summary>导出 SVG。这是"一键贴进 PPT"的实现。</summary>
    public string ToSvg(Theme? theme = null, SvgOptions? options = null)
        => SvgWriter.Write(BuildDisplayList(theme), theme ?? Theme.Textbook, options);
}

/// <summary>
/// 求解器。
///
/// 核心洞察：中学几何题的作图是"尺规作图顺序"，不是联立方程组。
/// 所以求解就是按依赖拓扑排序、逐个算坐标，O(n)，拖动时能轻松跑满帧。
/// 只有真正的约束求解（"任意三角形"这类）才需要迭代，那不是 v1 的范围。
/// </summary>
internal static class Solver
{
    public static SolveResult Solve(Scene scene)
    {
        var diagnostics = new List<SolveDiagnostic>();

        var order = TopologicalOrder(scene, diagnostics);
        if (order is null)
            return new SolveResult { Success = false, Diagnostics = diagnostics };

        foreach (var obj in order)
        {
            try
            {
                switch (obj)
                {
                    case GeoPoint point:
                        ResolvePoint(scene, point, diagnostics);
                        break;

                    case GeoSphere sphere:
                        ResolveSphere(scene, sphere, diagnostics);
                        break;
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(SolveDiagnostic.Error(obj.Id, "solve.failed", ex.Message));
            }
        }

        var success = diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
        return new SolveResult { Success = success, Diagnostics = diagnostics };
    }

    /// <summary>Kahn 拓扑排序。有环就报出来，不要静默死循环。</summary>
    private static List<GeoObject>? TopologicalOrder(Scene scene, List<SolveDiagnostic> diagnostics)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var obj in scene.Objects)
        {
            inDegree[obj.Id] = 0;
            dependents[obj.Id] = [];
        }

        foreach (var obj in scene.Objects)
        {
            foreach (var dependency in obj.Dependencies)
            {
                if (!scene.ById.ContainsKey(dependency))
                {
                    diagnostics.Add(SolveDiagnostic.Error(obj.Id, "ref.missing", dependency));
                    continue;
                }

                inDegree[obj.Id]++;
                dependents[dependency].Add(obj.Id);
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var ordered = new List<GeoObject>(scene.Objects.Count);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            ordered.Add(scene.ById[id]);
            foreach (var dependent in dependents[id])
                if (--inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
        }

        if (ordered.Count != scene.Objects.Count)
        {
            var cyclic = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key);
            diagnostics.Add(SolveDiagnostic.Error(null, "ref.cycle", string.Join(", ", cyclic)));
            return null;
        }

        return ordered;
    }

    private static void ResolvePoint(Scene scene, GeoPoint point, List<SolveDiagnostic> diagnostics)
    {
        switch (point.Kind)
        {
            case PointKind.Free:
                point.Position = point.FreePosition;
                break;

            case PointKind.OnPath:
            {
                var path = point.PathId is null ? null : scene.Find(point.PathId);
                if (path is null)
                {
                    diagnostics.Add(SolveDiagnostic.Error(point.Id, "ref.path", point.PathId));
                    return;
                }

                // 驱动器是参数的权威来源：有驱动器时以驱动器为准。
                if (point.Driver is { } driver)
                    point.Param = MathUtil.Clamp(driver.Value, driver.Min, driver.Max);

                point.Position = PathGeometry.PointAt(scene, path, point.Param);
                break;
            }

            case PointKind.Derived:
                ResolveDerived(scene, point, diagnostics);
                break;
        }
    }

    /// <summary>
    /// 球的切接。球心和半径是从体算出来的，不是文件里写死的 ——
    /// 这样老师拖一下顶点，球会跟着变。
    /// </summary>
    private static void ResolveSphere(Scene scene, GeoSphere sphere, List<SolveDiagnostic> diagnostics)
    {
        sphere.IsSolved = false;

        if (scene.Find(sphere.SolidId) is not GeoPolyhedron solid)
        {
            diagnostics.Add(SolveDiagnostic.Error(sphere.Id, "ref.missing", sphere.SolidId));
            return;
        }

        var vertices = new Vec3[solid.VertexIds.Count];

        for (var i = 0; i < vertices.Length; i++)
        {
            var point = scene.Point(solid.VertexIds[i]);
            if (point is null)
            {
                diagnostics.Add(SolveDiagnostic.Error(sphere.Id, "ref.missing", solid.VertexIds[i]));
                return;
            }

            vertices[i] = point.Position;
        }

        var fit = sphere.Kind == SphereKind.Circumscribed
            ? SphereFit.MinimumEnclosing(vertices)
            : SphereFit.Inscribed(vertices, solid.Faces);

        if (fit is null)
        {
            // 没有内切球的多面体是事实，不是错误 —— 但要让老师知道为什么图上没球。
            diagnostics.Add(SolveDiagnostic.Error(sphere.Id, sphere.Kind == SphereKind.Circumscribed
                ? "geom.sphere.circumscribed"
                : "geom.sphere.inscribed"));
            return;
        }

        sphere.Center = fit.Value.Center;
        sphere.Radius = fit.Value.Radius;
        sphere.IsSolved = true;
    }

    private static void ResolveDerived(Scene scene, GeoPoint point, List<SolveDiagnostic> diagnostics)
    {
        var args = point.Args;

        switch (point.DerivedKind)
        {
            case DerivedKind.Midpoint:
            {
                if (!TryPoints(scene, point, args, 2, diagnostics, out var p)) return;
                point.Position = Vec3.Lerp(p[0], p[1], 0.5);
                break;
            }

            case DerivedKind.Centroid:
            {
                if (!TryPoints(scene, point, args, 3, diagnostics, out var p)) return;
                point.Position = new Vec3(
                    (p[0].X + p[1].X + p[2].X) / 3,
                    (p[0].Y + p[1].Y + p[2].Y) / 3,
                    (p[0].Z + p[1].Z + p[2].Z) / 3);
                break;
            }

            case DerivedKind.Intersection:
            {
                if (args.Count < 2)
                {
                    Fail(point, diagnostics, "geom.intersection.args");
                    return;
                }

                var first = scene.Find(args[0]);
                var second = scene.Find(args[1]);
                if (first is null || second is null)
                {
                    Fail(point, diagnostics, "geom.intersection.missing", args[0], args[1]);
                    return;
                }

                var hits = Intersect.Of(scene, first, second);
                if (hits.Count == 0)
                {
                    // 这是"条件自相矛盾"或"暂时无交点"，属于可修复状态，不是崩溃。
                    Fail(point, diagnostics, "geom.noIntersection", args[0], args[1]);
                    return;
                }

                var index = 0;
                if (args.Count >= 3 && int.TryParse(args[2], out var parsed)) index = parsed;
                if (index < 0 || index >= hits.Count) index = 0;

                point.Position = hits[index];
                break;
            }

            case DerivedKind.Foot:
            {
                if (!TryPoint(scene, point, args, 0, diagnostics, out var source)) return;
                if (!TryLinePoints(scene, point, args, 1, diagnostics, out var a, out var b)) return;
                point.Position = Vec3.FootOnLine(source, a, b);
                break;
            }

            case DerivedKind.Reflection:
            {
                if (!TryPoint(scene, point, args, 0, diagnostics, out var source)) return;

                var mirror = args.Count > 1 ? scene.Find(args[1]) : null;
                switch (mirror)
                {
                    case GeoPoint center:
                        point.Position = Vec3.ReflectAboutPoint(source, center.Position);
                        break;

                    case GeoLine line:
                    {
                        var a = scene.Point(line.AId)!.Position;
                        var b = scene.Point(line.BId)!.Position;
                        var foot = Vec3.FootOnLine(source, a, b);
                        point.Position = foot * 2 - source;
                        break;
                    }

                    default:
                        Fail(point, diagnostics, "geom.mirror", args.ElementAtOrDefault(1));
                        return;
                }
                break;
            }

            case DerivedKind.Rotate:
            {
                if (!TryPoint(scene, point, args, 0, diagnostics, out var source)) return;
                if (!TryPoint(scene, point, args, 1, diagnostics, out var center)) return;
                var degrees = ParseDouble(args.ElementAtOrDefault(2), 0);
                point.Position = Vec3.RotateAboutZ(source, center, MathUtil.DegToRad(degrees));
                break;
            }

            case DerivedKind.Scale:
            {
                if (!TryPoint(scene, point, args, 0, diagnostics, out var source)) return;
                if (!TryPoint(scene, point, args, 1, diagnostics, out var center)) return;
                var factor = ParseDouble(args.ElementAtOrDefault(2), 1);
                point.Position = center + (source - center) * factor;
                break;
            }

            case DerivedKind.Translate:
            {
                if (!TryPoint(scene, point, args, 0, diagnostics, out var source)) return;
                if (!TryPoint(scene, point, args, 1, diagnostics, out var from)) return;
                if (!TryPoint(scene, point, args, 2, diagnostics, out var to)) return;
                point.Position = source + (to - from);
                break;
            }

            case DerivedKind.OnSegment:
            {
                if (!TryPoints(scene, point, args, 2, diagnostics, out var p)) return;
                var ratio = ParseDouble(args.ElementAtOrDefault(2), 0.5);
                point.Position = Vec3.Lerp(p[0], p[1], ratio);
                break;
            }

            default:
                Fail(point, diagnostics, "geom.unsupported", point.DerivedKind);
                break;
        }
    }

    // —— 取参数的辅助函数：把"参数缺失/类型不对"统一变成诊断，而不是异常 ——

    private static bool TryPoint(Scene scene, GeoPoint owner, IReadOnlyList<string> args, int index,
        List<SolveDiagnostic> diagnostics, out Vec3 position)
    {
        position = default;
        var id = args.ElementAtOrDefault(index);
        var point = id is null ? null : scene.Point(id);
        if (point is null)
        {
            Fail(owner, diagnostics, "geom.point", index, id);
            return false;
        }

        position = point.Position;
        return true;
    }

    private static bool TryPoints(Scene scene, GeoPoint owner, IReadOnlyList<string> args, int count,
        List<SolveDiagnostic> diagnostics, out Vec3[] positions)
    {
        positions = new Vec3[count];
        for (var i = 0; i < count; i++)
            if (!TryPoint(scene, owner, args, i, diagnostics, out positions[i]))
                return false;
        return true;
    }

    private static bool TryLinePoints(Scene scene, GeoPoint owner, IReadOnlyList<string> args, int index,
        List<SolveDiagnostic> diagnostics, out Vec3 a, out Vec3 b)
    {
        a = default;
        b = default;

        var id = args.ElementAtOrDefault(index);
        if (id is null || scene.Find(id) is not GeoLine line)
        {
            Fail(owner, diagnostics, "geom.line", index, id);
            return false;
        }

        var pa = scene.Point(line.AId);
        var pb = scene.Point(line.BId);
        if (pa is null || pb is null)
        {
            Fail(owner, diagnostics, "ref.lineEndpoints", id);
            return false;
        }

        a = pa.Position;
        b = pb.Position;
        return true;
    }

    private static double ParseDouble(string? text, double fallback)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static void Fail(GeoPoint point, List<SolveDiagnostic> diagnostics, string code, params object?[] args)
        => diagnostics.Add(SolveDiagnostic.Error(point.Id, code, args));
}
