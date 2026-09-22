namespace MathGeo.Core;

/// <summary>一次展开的结果：顶点对象 + 多面体本体。调用方负责把它们加进 Scene。</summary>
public sealed record SolidExpansion(IReadOnlyList<GeoPoint> Vertices, GeoPolyhedron Solid);

/// <summary>
/// 规整后的基本体参数。
///
/// 之所以要有这个类型：结构校验必须在**展开之前**就知道一个基本体会生成哪些顶点 Id，
/// 否则文件里写 "cube.A" 会被误判成"引用了不存在的对象"。
/// 让展开和预判共用同一份参数与同一套命名规则，两边才不会漂。
/// </summary>
public readonly record struct SolidParameters(string Solid, int Sides, string Letters, string Apex)
{
    public bool IsBox => Solid is "box" or "cuboid" or "cube";

    public bool IsKnown => IsBox || Solid is "prism" or "pyramid";
}

/// <summary>
/// 参数化基本体。
///
/// 立体几何绝对不能逐点画 —— 老师在 PPT 里画一个正方体要拼 3 分钟，
/// 而这里应该是"选一个体、拖出尺寸、3 秒完事"。所以基本体是算出来的，不是画出来的。
///
/// 顶点命名一律按课本约定：正方体是 ABCD-A₁B₁C₁D₁，棱锥是 S-ABCD。
/// 这个细节看着小，但它决定了老师第一眼觉得"这是给我做的"还是"这是个通用 3D 软件"。
/// </summary>
public static class SolidPresets
{
    private const string SubscriptOne = "₁";
    private const string DefaultLetters = "ABCDEFGH";

    /// <summary>
    /// 顶点的两种名字。
    ///
    /// Id 用 ASCII 数字（cube.C1），标签用课本下标（C₁）——
    /// 这个区分是必须的：手写文件和 agent 都会自然而然地写 "cube.C1"，
    /// 如果 Id 里塞的是 Unicode 下标，那些引用会全部找不到。
    /// </summary>
    private readonly record struct VertexNaming(string IdSuffix, string Label);

    public static SolidParameters Normalize(string? solid, int? sides, string? prefix, string? apex)
    {
        var kind = (solid ?? string.Empty).Trim().ToLowerInvariant();
        var isBox = kind is "box" or "cuboid" or "cube";
        var count = isBox ? 4 : Math.Clamp(sides ?? 4, 3, 8);

        var letters = string.IsNullOrWhiteSpace(prefix) ? DefaultLetters : prefix.Trim();
        // 字母不够就整体退回默认表，而不是拼出一个半用户半默认的怪名字。
        if (letters.Length < count) letters = DefaultLetters;

        return new SolidParameters(kind, count, letters,
            string.IsNullOrWhiteSpace(apex) ? "S" : apex.Trim());
    }

    /// <summary>预判一个基本体会展开出哪些顶点 Id。结构校验用它，展开也用它。</summary>
    public static IReadOnlyList<string> PredictVertexIds(string id, SolidParameters parameters)
        => [.. Namings(parameters).Select(naming => $"{id}.{naming.IdSuffix}")];

    private static IReadOnlyList<VertexNaming> Namings(SolidParameters p)
    {
        if (p.IsBox || p.Solid == "prism")
        {
            var namings = new List<VertexNaming>(p.Sides * 2);
            for (var i = 0; i < p.Sides; i++)
                namings.Add(new VertexNaming(p.Letters[i].ToString(), p.Letters[i].ToString()));
            for (var i = 0; i < p.Sides; i++)
                namings.Add(new VertexNaming($"{p.Letters[i]}1", $"{p.Letters[i]}{SubscriptOne}"));
            return namings;
        }

        if (p.Solid == "pyramid")
        {
            var namings = new List<VertexNaming>(p.Sides + 1);
            for (var i = 0; i < p.Sides; i++)
                namings.Add(new VertexNaming(p.Letters[i].ToString(), p.Letters[i].ToString()));
            namings.Add(new VertexNaming(p.Apex, p.Apex));
            return namings;
        }

        return [];
    }

    /// <summary>
    /// 长方体（宽=高=深 时即正方体）。
    /// 原点取底面中心，这样绕原点旋转时体是稳的，不会甩来甩去。
    /// </summary>
    public static SolidExpansion Box(
        string id, double width, double height, double depth,
        Vec3 origin = default, string prefix = "ABCD")
    {
        var parameters = Normalize("box", null, prefix, null);
        var namings = Namings(parameters);

        var halfWidth = width / 2;
        var halfDepth = depth / 2;

        var bottom = new[]
        {
            new Vec3(-halfWidth, 0, -halfDepth),
            new Vec3(halfWidth, 0, -halfDepth),
            new Vec3(halfWidth, 0, halfDepth),
            new Vec3(-halfWidth, 0, halfDepth),
        };

        var points = new List<GeoPoint>(8);

        // 顺序必须是"先四个下底、再四个上底"：面的下标就是按这个顺序写的。
        // 把两个循环合并成"每层各放一个"会得到一个连错顶点的怪体 —— 顶点位置都对，
        // 但面是交叉的，看上去像一个诡异的斜体，而且隐藏线判定会全乱。
        for (var i = 0; i < 4; i++)
            points.Add(GeoPoint.Free($"{id}.{namings[i].IdSuffix}", origin + bottom[i], namings[i].Label));

        for (var i = 0; i < 4; i++)
            points.Add(GeoPoint.Free($"{id}.{namings[i + 4].IdSuffix}",
                origin + bottom[i] + new Vec3(0, height, 0), namings[i + 4].Label));

        // 绕向统一"从体外看逆时针"，法向量朝外。
        int[][] faces =
        [
            [0, 1, 2, 3],   // 下底
            [4, 7, 6, 5],   // 上底
            [0, 4, 5, 1],   // 前面
            [1, 5, 6, 2],   // 右面
            [2, 6, 7, 3],   // 后面
            [3, 7, 4, 0],   // 左面
        ];

        return new SolidExpansion(points, GeoPolyhedron.Of(id, [.. points.Select(p => p.Id)], faces));
    }

    /// <summary>正 n 棱柱。</summary>
    public static SolidExpansion Prism(
        string id, int sides, double radius, double height,
        Vec3 origin = default, string prefix = "ABCDEFGH")
    {
        var parameters = Normalize("prism", sides, prefix, null);
        var namings = Namings(parameters);
        var count = parameters.Sides;

        var points = new List<GeoPoint>(count * 2);
        var bottom = new Vec3[count];

        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            bottom[i] = origin + new Vec3(radius * Math.Cos(angle), 0, radius * Math.Sin(angle));
            points.Add(GeoPoint.Free($"{id}.{namings[i].IdSuffix}", bottom[i], namings[i].Label));
        }

        for (var i = 0; i < count; i++)
            points.Add(GeoPoint.Free($"{id}.{namings[i + count].IdSuffix}",
                bottom[i] + new Vec3(0, height, 0), namings[i + count].Label));

        var faces = new List<int[]>(count + 2)
        {
            Enumerable.Range(0, count).ToArray(),                    // 下底
            Enumerable.Range(count, count).Reverse().ToArray(),      // 上底
        };

        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            faces.Add([i, i + count, next + count, next]);
        }

        return new SolidExpansion(points, GeoPolyhedron.Of(id, [.. points.Select(p => p.Id)], faces));
    }

    /// <summary>正 n 棱锥，顶点默认叫 S（课本约定 S-ABCD）。</summary>
    public static SolidExpansion Pyramid(
        string id, int sides, double radius, double height,
        Vec3 origin = default, string apexLabel = "S", string prefix = "ABCDEFGH")
    {
        var parameters = Normalize("pyramid", sides, prefix, apexLabel);
        var namings = Namings(parameters);
        var count = parameters.Sides;

        var points = new List<GeoPoint>(count + 1);

        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            var vertex = origin + new Vec3(radius * Math.Cos(angle), 0, radius * Math.Sin(angle));
            points.Add(GeoPoint.Free($"{id}.{namings[i].IdSuffix}", vertex, namings[i].Label));
        }

        points.Add(GeoPoint.Free($"{id}.{namings[count].IdSuffix}",
            origin + new Vec3(0, height, 0), namings[count].Label));

        var faces = new List<int[]>(count + 1)
        {
            Enumerable.Range(0, count).ToArray(),   // 底面
        };

        for (var i = 0; i < count; i++)
            faces.Add([count, (i + 1) % count, i]);   // 侧面

        return new SolidExpansion(points, GeoPolyhedron.Of(id, [.. points.Select(p => p.Id)], faces));
    }
}
