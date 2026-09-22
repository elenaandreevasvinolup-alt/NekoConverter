using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MathGeo.Core;

/// <summary>
/// .problem.json 的根对象。
///
/// 这是整个产品的唯一契约：内核读它画图，外部 agent 写它出题。
/// 所以它刻意做得"扁"和"啰嗦" —— 每种对象把该用的字段平铺在同一个 DTO 上，
/// 而不是嵌套判别类型。agent 生成这种 JSON 的出错率低得多，
/// 而且人肉看一眼就知道写了什么。
/// </summary>
public sealed class ProblemFile
{
    /// <summary>格式版本。外部工具据此判断能不能读。</summary>
    public int Schema { get; set; } = 1;

    public string? Id { get; set; }

    /// <summary>2d-geometry | 3d-solid | function | calculus</summary>
    public string Kind { get; set; } = "2d-geometry";

    public string? Lang { get; set; }

    public string? Title { get; set; }

    /// <summary>题干原文。人看的，内核不解析它。</summary>
    public string? Statement { get; set; }

    public List<ProblemObjectDto> Objects { get; set; } = [];

    /// <summary>问什么。与答案分离，于是"隐藏答案"就是学生自学模式。</summary>
    public ProblemAskDto? Ask { get; set; }

    public ProblemAnswerDto? Answer { get; set; }

    /// <summary>视角与版式。3D 的相机参数、2D 的视野范围都记在这里。</summary>
    public ProblemViewDto? View { get; set; }

    /// <summary>溯源。agent 生成的题必须能追回原始 PDF 和模型名 —— 老师信任的前提。</summary>
    public ProblemSourceDto? Source { get; set; }

    /// <summary>
    /// 文件读写共用的一组设置。
    ///
    /// 关键是 UnsafeRelaxedJsonEscaping：默认编码器会把中文转义成 \u25B3 这样的码点，
    /// 于是老师打开 .problem.json 看到的是一堆乱码 —— 看不懂就改不动，改不动就不敢信
    /// agent 生成的题。这里是"可读性即信任"的落点。
    ///
    /// 它叫 Unsafe 只是针对 HTML 注入场景；我们写的是 .json 文件、用 STJ 自己读回来，
    /// 不存在那个上下文。
    /// </summary>
    private static readonly JsonSerializerOptions FileJson =
        new(MathGeoJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = MathGeoJsonContext.Default,
        };

    /// <summary>
    /// 显式取出 JsonTypeInfo 再序列化，而不是走 (值, options) 那个重载。
    /// 后者带 RequiresUnreferencedCode / RequiresDynamicCode 标注，在裁剪 + AOT 的
    /// 发行方式下会在运行期静默失效 —— 这正是 IsAotCompatible 要挡住的坑。
    /// </summary>
    private static readonly JsonTypeInfo<ProblemFile> FileTypeInfo =
        (JsonTypeInfo<ProblemFile>)FileJson.GetTypeInfo(typeof(ProblemFile));

    public string ToJson() => JsonSerializer.Serialize(this, FileTypeInfo);

    public static ProblemFile? FromJson(string json) => JsonSerializer.Deserialize(json, FileTypeInfo);
}

public sealed class ProblemObjectDto
{
    public string Id { get; set; } = "";

    /// <summary>point | segment | ray | line | circle | polygon</summary>
    public string Type { get; set; } = "";

    /// <summary>显示标签，如 "A"、"A₁"。省略则不画。</summary>
    public string? Label { get; set; }

    /// <summary>样式角色：helper（辅助线）| answer（所求）| highlight。</summary>
    public string? Style { get; set; }

    public bool? Visible { get; set; }

    // —— 点 ——

    /// <summary>free | path | derived</summary>
    public string? PointKind { get; set; }

    /// <summary>自由点坐标：[x, y] 或 [x, y, z]。</summary>
    public double[]? At { get; set; }

    /// <summary>路径点的宿主路径 Id。</summary>
    public string? Path { get; set; }

    /// <summary>沿路径的归一化参数 [0,1]。</summary>
    public double? Param { get; set; }

    /// <summary>派生方式：midpoint | centroid | intersection | foot | reflection | rotate | scale | translate | onSegment</summary>
    public string? Derived { get; set; }

    /// <summary>派生方式的参数，元素是对象 Id 或数值字符串。</summary>
    public string[]? Args { get; set; }

    /// <summary>非空即为动点，可播放。</summary>
    public ProblemDriverDto? Driver { get; set; }

    // —— 线 ——

    public string? A { get; set; }
    public string? B { get; set; }

    // —— 圆 ——

    public string? Center { get; set; }
    public double? Radius { get; set; }

    /// <summary>圆上一点（"以 A 为圆心过 B 作圆"）。给了它，radius 就忽略。</summary>
    public string? Through { get; set; }

    // —— 多边形 ——

    public string[]? Vertices { get; set; }
    public bool? Filled { get; set; }

    // —— 立体 ——
    //
    // 两种写法都支持，因为它们服务的是不同的人：
    //   solid: "box"  —— 参数化基本体，agent 写一行就够，老师拖一下就能改尺寸；
    //   polyhedron    —— 显式顶点 + 面，用于"正方体切掉一个角"这种自定义体。

    /// <summary>参数化基本体：box | prism | pyramid</summary>
    public string? Solid { get; set; }

    /// <summary>box 的三边 [宽, 高, 深]。</summary>
    public double[]? Size { get; set; }

    /// <summary>prism / pyramid 的底面边数。</summary>
    public int? Sides { get; set; }

    /// <summary>高（prism / pyramid）。底面的外接圆半径复用上面的 Radius 字段。</summary>
    public double? Height { get; set; }

    /// <summary>底面中心位置，默认原点。</summary>
    public double[]? Origin { get; set; }

    /// <summary>棱锥顶点字母，默认 S（课本约定 S-ABCD）。</summary>
    public string? Apex { get; set; }

    /// <summary>底面顶点字母前缀，默认 ABCD。</summary>
    public string? Prefix { get; set; }

    /// <summary>显式多面体的面（顶点下标环）。绕向从体外看逆时针。</summary>
    public int[][]? Faces { get; set; }

    /// <summary>要强调的面下标。讲截面、二面角时用。</summary>
    public int[]? HighlightFaces { get; set; }

    /// <summary>是否画虚线隐藏棱。false 时只留可见棱，用于分层讲解。</summary>
    public bool? ShowHiddenEdges { get; set; }

    // —— 截面 ——

    /// <summary>截面所切的多面体 Id。</summary>
    public string? Of { get; set; }

    /// <summary>三点定面：确定截面平面的三个点 Id。</summary>
    public string[]? Plane { get; set; }

    // —— 二面角 ——

    /// <summary>二面角的第一个面在体的 Faces 里的下标。</summary>
    public int? FaceA { get; set; }

    public int? FaceB { get; set; }

    /// <summary>平面角圆弧半径，按棱长的比例。</summary>
    public double? RadiusRatio { get; set; }

    /// <summary>是否标注度数。直角会自动改用直角符号。</summary>
    public bool? ShowValue { get; set; }

    // —— 角的标注 ——

    /// <summary>角的顶点 Id。</summary>
    public string? Vertex { get; set; }

    /// <summary>角的一条边上的点 Id。</summary>
    public string? From { get; set; }

    /// <summary>角的另一条边上的点 Id。</summary>
    public string? To { get; set; }

    // —— 三视图 ——

    /// <summary>参与投影的体 Id 列表。</summary>
    public string[]? Solids { get; set; }

    /// <summary>视图间距比例。</summary>
    public double? Gap { get; set; }

    /// <summary>是否在每个视图下方写名称。</summary>
    public bool? ShowNames { get; set; }

    // —— 球的切接 ——

    /// <summary>circumscribed（外接球）| inscribed（内切球）。</summary>
    public string? Sphere { get; set; }

    /// <summary>经线条数。</summary>
    public int? Meridians { get; set; }

    /// <summary>是否画球心。</summary>
    public bool? ShowCenter { get; set; }

    /// <summary>是否画球心到顶点的半径线段。</summary>
    public bool? ShowRadius { get; set; }
}

public sealed class ProblemDriverDto
{
    public double Min { get; set; }
    public double Max { get; set; } = 1;
    public double Value { get; set; } = 0.5;
    public double Speed { get; set; } = 0.5;
    public bool PingPong { get; set; } = true;
}

public sealed class ProblemAskDto
{
    /// <summary>所求对象的 Id。</summary>
    public string? Target { get; set; }

    /// <summary>length | angle | area | volume | locus | proof</summary>
    public string? Measure { get; set; }
}

public sealed class ProblemAnswerDto
{
    public string? Value { get; set; }
    public List<string> Steps { get; set; } = [];
}

public sealed class ProblemViewDto
{
    /// <summary>二维视野：[minX, minY, maxX, maxY]。省略则自动适配内容。</summary>
    public double[]? Bounds { get; set; }

    /// <summary>三维相机：[方位角, 仰角, 距离]。省略则用课本默认视角。</summary>
    public double[]? Camera { get; set; }

    /// <summary>相机是否用正交投影（斜二测、正等测这类课本画法）。</summary>
    public bool? Orthographic { get; set; }

    /// <summary>相机绕的基准点（相对图形重心的偏移）。</summary>
    public double[]? CameraTarget { get; set; }

    public double? TargetWidth { get; set; }
}

public sealed class ProblemSourceDto
{
    /// <summary>manual | agent | scan</summary>
    public string? Type { get; set; }

    public string? File { get; set; }
    public string? Page { get; set; }
    public string? Exercise { get; set; }
    public string? Textbook { get; set; }
    public string? Model { get; set; }
    public string? Created { get; set; }
}

/// <summary>
/// System.Text.Json 源生成上下文。
/// 必须用源生成而不是反射：发行版是裁剪 + AOT 的单文件，反射序列化会在运行时静默失效。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = false)]
[JsonSerializable(typeof(ProblemFile))]
[JsonSerializable(typeof(ProblemObjectDto))]
[JsonSerializable(typeof(ProblemDriverDto))]
[JsonSerializable(typeof(ProblemAskDto))]
[JsonSerializable(typeof(ProblemAnswerDto))]
[JsonSerializable(typeof(ProblemViewDto))]
[JsonSerializable(typeof(ProblemSourceDto))]
public sealed partial class MathGeoJsonContext : JsonSerializerContext;
