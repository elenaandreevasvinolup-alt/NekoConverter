namespace MathGeo.Core;

/// <summary>点的三种定义方式。第四种"动点"不是并列的一种，而是"可动点 + 驱动器"的组合。</summary>
public enum PointKind
{
    /// <summary>自由点：可以拖到任意位置。</summary>
    Free,

    /// <summary>约束点：被绑在一条路径上，只能沿路径滑动。这是动点问题的主力。</summary>
    OnPath,

    /// <summary>派生点：由别的对象算出来，自己不能拖。</summary>
    Derived,
}

/// <summary>
/// 派生点的算法。用"枚举 + 参数 Id 列表"而不是子类，是为了让 .problem.json
/// 可以被外部 agent 直接生成 —— agent 只需要写一个字符串和几个 Id，不需要懂类型系统。
/// </summary>
public enum DerivedKind
{
    /// <summary>Args: [a, b] —— 中点。</summary>
    Midpoint,

    /// <summary>Args: [a, b, c] —— 重心。</summary>
    Centroid,

    /// <summary>Args: [obj1, obj2] 或 [obj1, obj2, index] —— 交点。</summary>
    Intersection,

    /// <summary>Args: [point, line] —— 到直线的垂足。</summary>
    Foot,

    /// <summary>Args: [point, center] 或 [point, line] —— 对称点。</summary>
    Reflection,

    /// <summary>Args: [point, center, angleDeg] —— 绕点旋转。</summary>
    Rotate,

    /// <summary>Args: [point, center, factor] —— 位似。</summary>
    Scale,

    /// <summary>Args: [point, from, to] —— 平移。</summary>
    Translate,

    /// <summary>Args: [a, b, ratio] —— 定比分点，ratio = AP/AB。</summary>
    OnSegment,
}

public enum LineKind
{
    /// <summary>线段：两端封闭。</summary>
    Segment,

    /// <summary>射线：从 A 出发经过 B 无限延伸。</summary>
    Ray,

    /// <summary>直线：两端无限。</summary>
    Infinite,
}

/// <summary>
/// 判断 args 里的一个字符串是"对象引用"还是"数值字面量"。
///
/// 这个判断必须只有一份实现：依赖解析和结构校验都要用它，两边一旦不一致，
/// 就会出现"校验通过但求解报错"或者反过来的鬼故事，而那种 bug 最难查。
/// </summary>
public static class GeoRef
{
    public static bool LooksLikeId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // 能按数字解析的就不是对象引用。用"能否解析成数字"而不是"是否含字母"，
        // 是因为 "1e5" 含字母但显然是数值，"p1" 不含数字但显然是 Id。
        return !double.TryParse(
            text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}

/// <summary>动点驱动器。挂在点上就表示这个点可以自动播放，并生成一根参数滑块。</summary>
public sealed class Driver
{
    /// <summary>参数下界。</summary>
    public double Min { get; set; }

    /// <summary>参数上界。</summary>
    public double Max { get; set; } = 1;

    /// <summary>当前值。对 OnPath 点就是沿路径的归一化参数。</summary>
    public double Value { get; set; } = 0.5;

    /// <summary>播放速度，单位是"每秒走完多少比例"。</summary>
    public double Speed { get; set; } = 0.5;

    /// <summary>往返播放（讲动点问题时几乎总是要的）。</summary>
    public bool PingPong { get; set; } = true;
}

/// <summary>
/// 对象图里的一个几何对象。
///
/// 对象之间只用 Id 互相引用，不用运行期指针 —— 于是整张图可以直接序列化成
/// 一个 JSON 文件，也可以被外部 agent 生成。这是"agent 出题、内核画图"的接缝。
/// </summary>
public abstract class GeoObject
{
    public required string Id { get; init; }

    /// <summary>显示标签，如 "A"、"A₁"、"l"。为 null 则不画标签。</summary>
    public string? Label { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>
    /// 样式角色名，交给 Theme 翻译成颜色线型。
    /// 约定：null=普通，"helper"=辅助线（虚线灰），"answer"=所求对象（高亮），
    /// "highlight"=当前选中/被强调。
    /// </summary>
    public string? Style { get; set; }

    /// <summary>本对象直接依赖的对象 Id。求解器据此拓扑排序，渲染器据此决定重绘范围。</summary>
    public abstract IReadOnlyList<string> Dependencies { get; }

    public override string ToString() => $"{GetType().Name}({Id})";
}

/// <summary>
/// 点。同一个类型承载"自由 / 约束在路径上 / 派生"三种定义，
/// 因为它们在文件里就是同一种东西，分成三个类会让序列化多出三层判别。
/// </summary>
public sealed class GeoPoint : GeoObject
{
    public PointKind Kind { get; private init; }

    // —— Free ——
    public Vec3 FreePosition { get; set; }

    // —— OnPath ——
    public string? PathId { get; private init; }

    /// <summary>沿路径的归一化参数，通常落在 [0,1]。</summary>
    public double Param { get; set; }

    // —— Derived ——
    public DerivedKind DerivedKind { get; private init; }
    public IReadOnlyList<string> Args { get; private init; } = [];

    /// <summary>非空即表示这是动点，可以自动播放。</summary>
    public Driver? Driver { get; set; }

    /// <summary>求解结果。只允许求解器写。</summary>
    public Vec3 Position { get; internal set; }

    /// <summary>
    /// 老师能不能直接用手拖。派生点不能拖 —— 它只能被父对象带着走，
    /// 这正是"二级动点"能自动联动的原因。
    /// </summary>
    public bool IsDraggable => Kind is PointKind.Free or PointKind.OnPath;

    /// <summary>
    /// 依赖只包含"对象引用"。派生点的 args 里混着数值字面量（"90"、"2"），
    /// 那些不是依赖 —— 把它们也算进去，拓扑排序就会报"引用了不存在的对象 90"。
    /// </summary>
    public override IReadOnlyList<string> Dependencies => Kind switch
    {
        PointKind.OnPath => PathId is null ? [] : [PathId],
        PointKind.Derived => Args.Where(GeoRef.LooksLikeId).ToArray(),
        _ => [],
    };

    public static GeoPoint Free(string id, Vec3 position, string? label = null)
        => new() { Id = id, Kind = PointKind.Free, FreePosition = position, Position = position, Label = label };

    public static GeoPoint OnPath(string id, string pathId, double param = 0.5, string? label = null)
        => new() { Id = id, Kind = PointKind.OnPath, PathId = pathId, Param = param, Label = label };

    public static GeoPoint Derived(string id, DerivedKind kind, params string[] args)
        => new() { Id = id, Kind = PointKind.Derived, DerivedKind = kind, Args = args };
}

/// <summary>线段 / 射线 / 直线。三者只差一个取值范围的约束，所以合成一个类型。</summary>
public sealed class GeoLine : GeoObject
{
    public required string AId { get; init; }
    public required string BId { get; init; }
    public LineKind Kind { get; init; } = LineKind.Segment;

    public override IReadOnlyList<string> Dependencies => [AId, BId];

    public static GeoLine Segment(string id, string a, string b, string? label = null)
        => new() { Id = id, AId = a, BId = b, Kind = LineKind.Segment, Label = label };

    public static GeoLine Ray(string id, string a, string b, string? label = null)
        => new() { Id = id, AId = a, BId = b, Kind = LineKind.Ray, Label = label };

    public static GeoLine Infinite(string id, string a, string b, string? label = null)
        => new() { Id = id, AId = a, BId = b, Kind = LineKind.Infinite, Label = label };
}

/// <summary>圆。半径可以是常量，也可以"过某点"从而随该点动态变化。</summary>
public sealed class GeoCircle : GeoObject
{
    public required string CenterId { get; init; }

    /// <summary>半径。当 ThroughId 非空时忽略。</summary>
    public double Radius { get; init; }

    /// <summary>圆上一点，用于"以 A 为圆心过 B 作圆"这种课本常见构图。</summary>
    public string? ThroughId { get; init; }

    public override IReadOnlyList<string> Dependencies
        => ThroughId is null ? [CenterId] : [CenterId, ThroughId];

    public static GeoCircle Fixed(string id, string centerId, double radius, string? label = null)
        => new() { Id = id, CenterId = centerId, Radius = radius, Label = label };

    public static GeoCircle Through(string id, string centerId, string throughId, string? label = null)
        => new() { Id = id, CenterId = centerId, ThroughId = throughId, Label = label };
}

/// <summary>多边形。顶点顺序即边界顺序，用于面填充与面积。</summary>
public sealed class GeoPolygon : GeoObject
{
    public required IReadOnlyList<string> VertexIds { get; init; }

    public bool Filled { get; init; } = true;

    public override IReadOnlyList<string> Dependencies => VertexIds;

    public static GeoPolygon Of(string id, params string[] vertexIds)
        => new() { Id = id, VertexIds = vertexIds };
}

/// <summary>
/// 多面体。用"顶点 Id 列表 + 面（顶点下标）"表示。
///
/// 面用下标而不是 Id：一是文件里短得多，二是面天然就是"这个体的第 k 个面"，
/// 只有用下标才写得出"高亮第 2 个面"这种操作 —— 而讲二面角、讲截面时天天要用。
///
/// 面的绕向统一是"从体外看逆时针"，这样法向量朝外。
/// 当前的隐藏线算法用深度缓冲，不依赖绕向；但绕向正确了，以后加光照、
/// 加背面剔除、加体积计算都不用返工。
/// </summary>
public sealed class GeoPolyhedron : GeoObject
{
    public required IReadOnlyList<string> VertexIds { get; init; }

    /// <summary>每个面是顶点下标组成的环。</summary>
    public required IReadOnlyList<int[]> Faces { get; init; }

    public bool Filled { get; set; } = true;

    /// <summary>要强调的面下标。讲截面、二面角、面面关系时用。</summary>
    public IReadOnlyList<int> HighlightFaces { get; set; } = [];

    /// <summary>
    /// 是否画被遮挡的棱（虚线）。可以关掉，用于"先给线框、再给面"的分层讲解 ——
    /// 让学生自己从线框想出体，再揭示面，这是立体几何入门的常规教法。
    /// </summary>
    public bool ShowHiddenEdges { get; set; } = true;

    public override IReadOnlyList<string> Dependencies => VertexIds;

    public static GeoPolyhedron Of(string id, IReadOnlyList<string> vertexIds, IReadOnlyList<int[]> faces,
        string? label = null)
        => new() { Id = id, VertexIds = vertexIds, Faces = faces, Label = label };
}

/// <summary>
/// 截面：用一个平面去切一个多面体，交线多边形由内核算出来。
///
/// 平面用三个点确定（三点定面，这是课本的做法，也是老师在图上最容易指的方式）。
/// 学生看不出交线在哪儿的时候，这个对象就是答案本身。
/// </summary>
public sealed class GeoSection : GeoObject
{
    /// <summary>被切的多面体 Id。</summary>
    public required string SolidId { get; init; }

    /// <summary>确定平面的三个点 Id。</summary>
    public required IReadOnlyList<string> PlanePointIds { get; init; }

    public bool Filled { get; set; } = true;

    public override IReadOnlyList<string> Dependencies => [SolidId, .. PlanePointIds];

    public static GeoSection Of(string id, string solidId, params string[] planePointIds)
        => new() { Id = id, SolidId = solidId, PlanePointIds = planePointIds };
}

/// <summary>
/// 二面角标注：取多面体的两个相邻面，自动求出平面角并画出来。
///
/// 学生卡住的从来不是"二面角是什么"，而是"平面角在哪儿"。这个构造是机械的，
/// 交给软件；老师省下来的时间用来讲为什么这样构造。
/// </summary>
public sealed class GeoDihedral : GeoObject
{
    /// <summary>多面体 Id。</summary>
    public required string SolidId { get; init; }

    /// <summary>第一个面在 GeoPolyhedron.Faces 里的下标。</summary>
    public required int FaceA { get; init; }

    public required int FaceB { get; init; }

    /// <summary>平面角圆弧半径，按棱长的比例取。太大盖住图形，太小看不清。</summary>
    public double RadiusRatio { get; set; } = 0.35;

    /// <summary>是否标注度数。直角会自动改用直角符号，不需要这个开关。</summary>
    public bool ShowValue { get; set; } = true;

    public override IReadOnlyList<string> Dependencies => [SolidId];

    public static GeoDihedral Of(string id, string solidId, int faceA, int faceB)
        => new() { Id = id, SolidId = solidId, FaceA = faceA, FaceB = faceB };
}

/// <summary>
/// 角的标注：顶点 + 两条边上的各一点。
///
/// 二维三维都能用 —— 它只依赖三个点的坐标，投影差异由渲染层吸收。
/// 直角会自动改画直角符号，不写 90°。
/// </summary>
public sealed class GeoAngle : GeoObject
{
    public required string VertexId { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }

    /// <summary>圆弧半径，按两条边中较短者的比例。太大盖住图形，太小看不清。</summary>
    public double RadiusRatio { get; set; } = 0.28;

    public bool ShowValue { get; set; } = true;

    public override IReadOnlyList<string> Dependencies => [VertexId, FromId, ToId];

    public static GeoAngle Of(string id, string vertexId, string fromId, string toId)
        => new() { Id = id, VertexId = vertexId, FromId = fromId, ToId = toId };
}

/// <summary>
/// 三视图：把体按正、侧、俯三个方向做正投影，并排画出来。
///
/// 这是高中立体几何最典型的"想象不出来"的题 —— 学生看着立体图想不出三视图，
/// 或者看着三视图想不出立体。让两者同屏，是最直接的解法。
///
/// 布局按第一角投影法（中国教材）：正视图在左上，侧视图在右上，俯视图在左下。
/// 三个视图共用同一比例，于是"长对正、高平齐、宽相等"自动成立，不用手工对齐。
/// </summary>
public sealed class GeoThreeViews : GeoObject
{
    /// <summary>参与投影的体。可以多个，它们会一起出现在每个视图里。</summary>
    public required IReadOnlyList<string> SolidIds { get; init; }

    /// <summary>视图之间的间距，按图形尺寸的比例。</summary>
    public double Gap { get; init; } = 0.35;

    /// <summary>是否在每个视图下方写名称（正视图 / 侧视图 / 俯视图）。</summary>
    public bool ShowNames { get; init; } = true;

    /// <summary>是否画被遮挡的棱（虚线）。三视图里通常要画。</summary>
    public bool ShowHiddenEdges { get; init; } = true;

    /// <summary>
    /// 整个版面的平移。
    /// 用来把三视图摆到立体图旁边而不是压在它身上 —— "立体图 + 三视图同屏"
    /// 才是这个知识点最有效的呈现方式。
    /// </summary>
    public Vec2 Offset { get; init; }

    public override IReadOnlyList<string> Dependencies => SolidIds;

    public static GeoThreeViews Of(string id, params string[] solidIds)
        => new() { Id = id, SolidIds = solidIds };
}

public enum SphereKind
{
    /// <summary>外接球：过体的所有顶点。</summary>
    Circumscribed,

    /// <summary>内切球：与体的所有面相切。</summary>
    Inscribed,
}

/// <summary>
/// 球的切接。高中立体几何公认最难的一类 ——
/// 学生靠背结论（"正方体外接球半径 = (√3/2)a"），因为图上根本看不出球在哪、
/// 球心在哪。把球和球心画出来，才有可能从"背下来"变成"看出来"。
///
/// 球心与半径由求解器算出来填进 Center / Radius，不是文件里写死的 ——
/// 老师拖一下顶点，球会跟着变。
/// </summary>
public sealed class GeoSphere : GeoObject
{
    public required string SolidId { get; init; }

    public SphereKind Kind { get; init; } = SphereKind.Circumscribed;

    /// <summary>经线条数。赤道 + 三条经线就足够读出球体感，再多只是涨体积。</summary>
    public int Meridians { get; init; } = 3;

    public bool ShowCenter { get; init; } = true;

    /// <summary>是否画出球心到某个顶点的半径线段。</summary>
    public bool ShowRadius { get; init; } = true;

    public bool ShowValue { get; set; } = true;

    /// <summary>求解结果。只有求解器会写。</summary>
    public Vec3 Center { get; internal set; }

    public double Radius { get; internal set; }

    /// <summary>求解是否成功。失败时不该画出一个半径 0 的球。</summary>
    public bool IsSolved { get; internal set; }

    public override IReadOnlyList<string> Dependencies => [SolidId];

    public static GeoSphere Of(string id, string solidId, SphereKind kind = SphereKind.Circumscribed)
        => new() { Id = id, SolidId = solidId, Kind = kind };
}
