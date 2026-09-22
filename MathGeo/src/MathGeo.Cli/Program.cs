using MathGeo.Cli;
using MathGeo.Core;

// MathGeo 无头命令行。
//
// 它存在的理由有两个：
//   1. MCP agent 要批量把题目渲成图，不能每次都开窗口；
//   2. 内核的正确性必须能在 CI 里验证，不依赖任何 UI。
//
// 所以这个项目只允许依赖 MathGeo.Core —— 一旦它引用了 UI，这两条就都不成立了。

// --lang 是全局选项，出现在命令前后都认。语言影响内核渲染诊断与命令行自身的文案。
var (language, remaining) = ExtractLanguage(args);
if (language is not null) Localizer.Use(language);

var command = remaining.Length > 0 ? remaining[0] : null;

return command switch
{
    "render" => Render(remaining[1..]),
    "validate" => Validate(remaining[1..]),
    "inspect" => Inspect(remaining[1..]),
    "gallery" => Gallery.Run(remaining[1..]),
    "interactive" => Interactive.Run(remaining[1..]),
    null or "help" or "-h" or "--help" => Usage(),
    _ => Unknown(command),
};

/// <summary>把 --lang 从参数里摘出来。摘掉之后各子命令拿到的就是不认识的参数。</summary>
static (string? Language, string[] Remaining) ExtractLanguage(string[] source)
{
    string? language = null;
    var rest = new List<string>(source.Length);

    for (var i = 0; i < source.Length; i++)
    {
        if (source[i] == "--lang" && i + 1 < source.Length)
        {
            language = source[++i];
            continue;
        }

        rest.Add(source[i]);
    }

    return (language, [.. rest]);
}

// ————————————————————————————————————————————————————————————

static int Render(string[] rest)
{
    var (input, output, dark, width) = ParseRenderArgs(rest);
    if (input is null)
    {
        Console.Error.WriteLine(Localizer.Current["cli.error.missingInput"]);
        return 2;
    }

    if (!TryReadProblem(input, out var file, out var readError)) return readError;

    var result = ProblemMapper.Load(file!);
    ReportDiagnostics(result.Diagnostics);

    if (!result.Scene.IsSolved)
    {
        Console.Error.WriteLine(Localizer.Current["cli.render.partial"]);
    }

    var svg = result.Scene.ToSvg(
        dark ? Theme.Dark : Theme.Textbook,
        new SvgOptions { TargetWidth = width });

    var destination = output ?? Path.ChangeExtension(input, ".svg");
    File.WriteAllText(destination, svg);

    Console.WriteLine(Localizer.Current.Format("cli.render.done", destination, svg.Length));
    return result.Success ? 0 : 1;
}

static int Validate(string[] rest)
{
    if (rest.Length == 0)
    {
        Console.Error.WriteLine(Localizer.Current["cli.error.missingInput"]);
        return 2;
    }

    if (!TryReadProblem(rest[0], out var file, out var readError)) return readError;

    var diagnostics = ProblemMapper.Validate(file!);

    if (diagnostics.Count == 0)
    {
        Console.WriteLine($"{rest[0]}: 结构校验通过");
        return 0;
    }

    ReportDiagnostics(diagnostics);
    return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 1 : 0;
}

/// <summary>把解出来的坐标打出来。验证题目对不对、以及给 agent 核对答案时用。</summary>
static int Inspect(string[] rest)
{
    if (rest.Length == 0)
    {
        Console.Error.WriteLine(Localizer.Current["cli.error.missingInput"]);
        return 2;
    }

    if (!TryReadProblem(rest[0], out var file, out var readError)) return readError;

    var result = ProblemMapper.Load(file!);
    ReportDiagnostics(result.Diagnostics);

    Console.WriteLine(Localizer.Current.Format("cli.inspect.title", file!.Title ?? file.Id ?? file.Objects.Count.ToString()));
    Console.WriteLine(Localizer.Current.Format("cli.inspect.objects", result.Scene.Objects.Count));

    foreach (var point in result.Scene.Objects.OfType<GeoPoint>())
    {
        var label = point.Label is { Length: > 0 } ? point.Label : point.Id;
        var kind = point.Kind switch
        {
            PointKind.Free => Localizer.Current["cli.inspect.point.free"],
            PointKind.OnPath => Localizer.Current["cli.inspect.point.path"],
            _ => Localizer.Current["cli.inspect.point.derived"],
        };
        Console.WriteLine($"  {label,-6} {kind}  {point.Position}");
    }

    if (file.Ask?.Target is { Length: > 0 } target)
        ReportAnswer(result.Scene, target);

    return result.Success ? 0 : 1;
}

/// <summary>把"所求"对象的度量打出来。截面会额外列出边数与各边长 —— 判断是不是正六边形就靠它。</summary>
static void ReportAnswer(Scene scene, string target)
{
    switch (scene.Find(target))
    {
        case GeoLine:
            Console.WriteLine(Localizer.Current.Format("cli.inspect.length", target, N(Measure.Length(scene, target))));
            break;

        case GeoSection section:
        {
            var polygon = CrossSection.Of(scene, section);
            if (polygon is null)
            {
                Console.WriteLine(Localizer.Current.Format("cli.inspect.sectionEmpty", target));
                break;
            }

            Console.WriteLine(Localizer.Current.Format("cli.inspect.section", target, polygon.Count));
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                Console.WriteLine(Localizer.Current.Format("cli.inspect.sectionSide", i, N(Vec3.Distance(polygon[i], polygon[next]))));
            }
            Console.WriteLine("  " + Localizer.Current.Format("cli.inspect.perimeter", target, N(Measure.Perimeter(scene, target))));
            Console.WriteLine("  " + Localizer.Current.Format("cli.inspect.area", target, N(Measure.Area(scene, target))));
            break;
        }

        case GeoPolygon:
            Console.WriteLine(Localizer.Current.Format("cli.inspect.area", target, N(Measure.Area(scene, target))));
            Console.WriteLine(Localizer.Current.Format("cli.inspect.perimeter", target, N(Measure.Perimeter(scene, target))));
            break;

        case GeoCircle:
            Console.WriteLine(Localizer.Current.Format("cli.inspect.area", target, N(Measure.Area(scene, target))));
            break;

        case GeoDihedral dihedral when scene.Find(dihedral.SolidId) is GeoPolyhedron solid
                                      && VertexPositions(scene, solid) is { } vertices:
        {
            var geometry = Dihedral.Of(vertices, solid.Faces, dihedral.FaceA, dihedral.FaceB);
            if (geometry is null)
            {
                Console.WriteLine(Localizer.Current.Format("cli.inspect.dihedralEmpty", target));
                break;
            }

            Console.WriteLine(Localizer.Current.Format("cli.inspect.dihedral", target, N(geometry.Degrees)));
            Console.WriteLine(Localizer.Current.Format("cli.inspect.dihedralVertex", geometry.Vertex));
            Console.WriteLine(Localizer.Current.Format("cli.inspect.dihedralEdge", N(geometry.EdgeLength)));
            break;
        }

        case GeoSphere sphere:
            if (!sphere.IsSolved)
            {
                Console.WriteLine(Localizer.Current.Format("cli.inspect.noSphere", target));
                break;
            }
            Console.WriteLine(Localizer.Current.Format("cli.inspect.radius", target, N(sphere.Radius)));
            Console.WriteLine(Localizer.Current.Format("cli.inspect.sphereCenter", sphere.Center));
            break;

        case GeoPoint point:
            Console.WriteLine(Localizer.Current.Format("cli.inspect.coordinate", target, point.Position));
            break;

        default:
            Console.WriteLine(Localizer.Current.Format("cli.inspect.unmeasurable", target));
            break;
    }
}

/// <summary>取出多面体各顶点的世界坐标。求不出来就返回 null，不要给一个半截数组。</summary>
static Vec3[]? VertexPositions(Scene scene, GeoPolyhedron solid)
{
    var positions = new Vec3[solid.VertexIds.Count];

    for (var i = 0; i < positions.Length; i++)
    {
        var point = scene.Point(solid.VertexIds[i]);
        if (point is null) return null;
        positions[i] = point.Position;
    }

    return positions;
}

// ————————————————————————————————————————————————————————————

static (string? Input, string? Output, bool Dark, double Width) ParseRenderArgs(string[] rest)
{
    string? input = null;
    string? output = null;
    var dark = false;
    var width = 960.0;

    for (var i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "-o" or "--out" when i + 1 < rest.Length:
                output = rest[++i];
                break;
            case "--dark":
                dark = true;
                break;
            case "--width" when i + 1 < rest.Length:
                if (double.TryParse(rest[++i], out var w) && w > 0) width = w;
                break;
            default:
                if (!rest[i].StartsWith('-')) input ??= rest[i];
                break;
        }
    }

    return (input, output, dark, width);
}

static bool TryReadProblem(string path, out ProblemFile? file, out int exitCode)
{
    file = null;
    exitCode = 0;

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"找不到文件：{path}");
        exitCode = 2;
        return false;
    }

    try
    {
        file = ProblemFile.FromJson(File.ReadAllText(path));
    }
    catch (Exception ex)
    {
        // JSON 语法错误要原样报出来 —— agent 写坏了文件时，行号是唯一有用的信息。
        Console.Error.WriteLine($"JSON 解析失败：{ex.Message}");
        exitCode = 1;
        return false;
    }

    if (file is null)
    {
        Console.Error.WriteLine("JSON 解析结果是空对象");
        exitCode = 1;
        return false;
    }

    return true;
}

/// <summary>数值输出统一用不变文化，并去掉多余的零 —— 20.78461 而不是 20.784610。</summary>
static string N(double? value)
    => value is null ? "-" : Math.Round(value.Value, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);

static void ReportDiagnostics(IReadOnlyList<SolveDiagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
    {
        var stream = diagnostic.Severity == DiagnosticSeverity.Error ? Console.Error : Console.Out;
        stream.WriteLine($"  {diagnostic}");
    }
}

static int Usage()
{
    var text = Localizer.Current;

    Console.WriteLine(text["cli.usage.title"]);
    Console.WriteLine();
    Console.WriteLine(text["cli.usage.body"]);
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine(Localizer.Current.Format("cli.unknownCommand", command));
    Usage();
    return 2;
}
