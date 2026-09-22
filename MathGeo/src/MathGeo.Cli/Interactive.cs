using System.Reflection;
using System.Text;
using MathGeo.Core;

namespace MathGeo.Cli;

/// <summary>
/// 交互式预览：把一个文件夹的题目生成一份**纯触摸**的单文件 HTML。
///
/// 为什么要预先烘帧而不是在浏览器里求解：内核是 C#，浏览器里没有求解器。
/// 但交互需要的是"动起来"，而不是"任意拖动"—— 动点沿路径走一遍、
/// 立体绕一圈，这两件事都可以在生成时烘成几十帧，前端只负责擦洗。
/// 于是得到一份：不依赖 .NET、不依赖网络、双击就能用、在希沃触摸屏和 iPad 上
/// 用手指左右拖就能看动画的文件。
///
/// 帧里只带浅色配色，深色主题由前端查映射表换色 —— 否则文件大小翻倍。
/// </summary>
internal static class Interactive
{
    /// <summary>帧数。36 帧足够顺，再密只是白涨体积。</summary>
    private const int FrameCount = 36;

    /// <summary>预览界面需要的那几个键。只取这些，不把 90 个键全塞进 HTML。</summary>
    private static readonly string[] UiKeys =
    [
        "app.name", "app.tagline",
        "ui.theme", "ui.theme.light", "ui.theme.dark", "ui.language",
        "ui.play", "ui.pause", "ui.reset",
        "ui.drag.hint", "ui.touch.hint",
        "ui.answer", "ui.steps", "ui.frame", "ui.frames",
        "ui.kind.animation", "ui.kind.orbit", "ui.kind.static",
        "ui.generated",
    ];

    public static int Run(string[] args)
    {
        string? folder = null;
        string? output = null;
        string? lang = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--lang" when i + 1 < args.Length:
                    lang = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith('-')) folder ??= args[i];
                    break;
            }
        }

        if (folder is null)
        {
            Console.Error.WriteLine(Localizer.Current["cli.error.missingFolder"]);
            return 2;
        }

        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine(Localizer.Current.Format("cli.error.folderNotFound", folder));
            return 2;
        }

        var files = Directory.EnumerateFiles(folder, "*.problem.json").Order().ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine(Localizer.Current.Format("cli.error.noSamples", folder));
            return 2;
        }

        // 诊断用命令行指定的语言渲染。界面文案则全部 16 种都带上，由前端切换 ——
        // 界面文案很小，多带 16 份比"切一次语言重新生成一次"实用得多。
        if (lang is not null) Localizer.Use(lang);
        var text = Localizer.Current;

        var problems = new List<string>(files.Count);
        var failed = 0;

        foreach (var file in files)
        {
            try
            {
                var payload = BuildProblem(file, text);
                if (payload is null) { failed++; continue; }
                problems.Add(payload);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  {Path.GetFileName(file)}: {ex.Message}");
                failed++;
            }
        }

        var html = InteractiveTemplate.Html
            .Replace("@@LANG@@", text.Lang)
            .Replace("@@DIR@@", text.Rtl ? "rtl" : "ltr")
            .Replace("@@LANG_JSON@@", Json(text.Lang))
            .Replace("@@APP_NAME@@", Html(text["app.name"]))
            .Replace("@@TAGLINE@@", Html(text["app.tagline"]))
            .Replace("@@SUMMARY@@", Html(text.Format("ui.frames", problems.Count)))
            .Replace("@@FOOTER@@", Html(text["ui.generated"]))
            .Replace("@@CURSOR_DEFAULT@@", DefaultCursor())
            .Replace("@@THEME_LIGHT@@", ThemeVariables(Theme.Textbook))
            .Replace("@@THEME_DARK@@", ThemeVariables(Theme.Dark))
            .Replace("@@LOCALES_JSON@@", BuildLocaleTable())
            .Replace("@@PALETTE_JSON@@", BuildPalette())
            .Replace("@@CURSORS_JSON@@", BuildCursors())
            .Replace("@@PROBLEMS_JSON@@", $"[{string.Join(",", problems)}]");

        var destination = output ?? Path.Combine(folder, "interactive.html");
        File.WriteAllText(destination, html);

        Console.WriteLine(text.Format("cli.gallery.done", destination));
        Console.WriteLine(text.Format("cli.gallery.summary", problems.Count, failed));

        return failed == 0 ? 0 : 1;
    }

    // ————————————————————————— 一道题 —————————————————————————

    private static string? BuildProblem(string path, Localizer text)
    {
        var problem = ProblemFile.FromJson(File.ReadAllText(path));
        if (problem is null) return null;

        var result = ProblemMapper.Load(problem);
        var scene = result.Scene;

        var kind = DetermineKind(scene);
        var frames = BuildFrames(scene, kind);
        var view = UnionBounds(frames);

        var builder = new StringBuilder(64 * 1024);
        builder.Append('{');
        builder.Append("\"file\":").Append(Json(Path.GetFileName(path)));
        builder.Append(",\"title\":").Append(Json(problem.Title ?? string.Empty));
        builder.Append(",\"statement\":").Append(Json(problem.Statement ?? string.Empty));
        builder.Append(",\"kind\":").Append(Json(kind));
        builder.Append(",\"background\":").Append(Json(Theme.Textbook.Background));

        builder.Append(",\"view\":[");
        for (var i = 0; i < view.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(MathUtil.Num(view[i]));
        }
        builder.Append(']');

        builder.Append(",\"frames\":[");
        for (var i = 0; i < frames.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(DisplayListJson.Write(frames[i]));
        }
        builder.Append(']');

        if (problem.Answer is { } answer && (!string.IsNullOrWhiteSpace(answer.Value) || answer.Steps.Count > 0))
        {
            builder.Append(",\"answer\":{\"value\":").Append(Json(answer.Value ?? string.Empty));
            builder.Append(",\"steps\":[").Append(string.Join(',', answer.Steps.Select(Json))).Append("]}");
        }

        // 诊断用命令行语言渲染一次即可：它是开发者向的细节，不跟着界面语言实时切。
        if (result.Diagnostics.Count > 0)
        {
            builder.Append(",\"diagnostics\":[");
            builder.Append(string.Join(',', result.Diagnostics.Select(d =>
                $"{{\"severity\":\"{(d.Severity == DiagnosticSeverity.Error ? "error" : "warning")}\"," +
                $"\"text\":{Json($"{d.Code} · {d.ObjectId ?? "-"} · {d.Localize(text)}")}}}")));
            builder.Append(']');
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>决定这道题该怎么"动"。动点优先于旋转 —— 动点才是几何课的主线。</summary>
    private static string DetermineKind(Scene scene)
    {
        if (scene.Objects.OfType<GeoPoint>().Any(p => p.Driver is not null)) return "animation";
        if (scene.Objects.OfType<GeoPolyhedron>().Any()) return "orbit";
        return "static";
    }

    private static List<IReadOnlyList<Shape>> BuildFrames(Scene scene, string kind)
    {
        var frames = new List<IReadOnlyList<Shape>>(FrameCount);

        if (kind == "static")
        {
            scene.Solve();
            frames.Add(scene.BuildDisplayList());
            return frames;
        }

        if (kind == "animation")
        {
            var driven = scene.Objects
                .OfType<GeoPoint>()
                .Where(p => p.Driver is not null)
                .Select(p => p.Id)
                .ToList();

            for (var i = 0; i < FrameCount; i++)
            {
                var t = (double)i / FrameCount;

                foreach (var id in driven)
                {
                    var driver = scene.Point(id)!.Driver!;

                    // 往返播放：讲动点问题时几乎总是要的，单程播完会"啪"地跳回起点。
                    var value = driver.PingPong
                        ? t < 0.5
                            ? driver.Min + (driver.Max - driver.Min) * (t * 2)
                            : driver.Max - (driver.Max - driver.Min) * ((t - 0.5) * 2)
                        : driver.Min + (driver.Max - driver.Min) * t;

                    scene.Drive(id, value);
                }

                frames.Add(scene.BuildDisplayList());
            }

            return frames;
        }

        // orbit：绕一圈。相机绕的是图形重心，所以看起来是"体在原地转"。
        var baseCamera = scene.Camera ?? Camera.Textbook;

        for (var i = 0; i < FrameCount; i++)
        {
            scene.Camera = baseCamera.Orbit((double)i / FrameCount * 360, 0);
            scene.Solve();
            frames.Add(scene.BuildDisplayList());
        }

        return frames;
    }

    /// <summary>
    /// 取所有帧的并集包围盒。
    /// 必须用并集而不是逐帧自适应 —— 否则动点一动，整张图就跟着缩放抖动，
    /// 看起来像软件在抽搐。
    /// </summary>
    private static double[] UnionBounds(List<IReadOnlyList<Shape>> frames)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var frame in frames)
        {
            var bounds = Bounds2.Of(frame);
            if (!double.IsFinite(bounds.MinX)) continue;

            minX = Math.Min(minX, bounds.MinX);
            minY = Math.Min(minY, bounds.MinY);
            maxX = Math.Max(maxX, bounds.MaxX);
            maxY = Math.Max(maxY, bounds.MaxY);
        }

        if (!double.IsFinite(minX)) return [0, 0, 1, 1];

        // 给顶点标签留余量：标签是按像素偏移画的，不在世界坐标里，
        // 所以这里按内容尺寸的百分比放一圈，保证最外圈的标签不会被裁掉。
        var margin = Math.Max(maxX - minX, maxY - minY) * 0.08 + 1e-6;

        return [minX - margin, minY - margin, (maxX - minX) + margin * 2, (maxY - minY) + margin * 2];
    }

    // ————————————————————————— 界面数据 —————————————————————————

    private static string BuildLocaleTable()
    {
        var builder = new StringBuilder(16 * 1024);
        builder.Append('{');

        var first = true;
        foreach (var info in Localizer.Installed)
        {
            var localizer = Localizer.Load(info.Lang);

            if (!first) builder.Append(',');
            first = false;

            builder.Append(Json(info.Lang)).Append(":{");
            builder.Append("\"_name\":").Append(Json(info.Name));
            if (info.Rtl) builder.Append(",\"_rtl\":true");

            // 未校对的语言要能一眼看出来，不能把英文冒充成翻译。
            if (!info.Reviewed) builder.Append(",\"_reviewed\":false");

            foreach (var key in UiKeys)
                builder.Append(',').Append(Json(key)).Append(':').Append(Json(localizer[key]));

            builder.Append('}');
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// 把主题摊成 CSS 变量。
    ///
    /// 从 Theme 生成而不是在模板里写死颜色：颜色只有一处定义，
    /// 改主题时不会出现"内核改了、预览还是旧色"的漂移。
    /// </summary>
    private static string ThemeVariables(Theme theme) => $"""
          --bg: {theme.PageBackground}; --card: {theme.Background}; --border: {theme.SurfaceBorder};
          --figure: {theme.Background};
          --text: {theme.LabelFill}; --muted: {theme.HelperStroke}; --faint: {theme.HelperStroke};
          --accent: {theme.HighlightStroke}; --accent-soft: {theme.AccentSoft};
          --bad: {theme.AnswerStroke}; --warn: {theme.AngleStroke};
        """;

    private static string BuildPalette()
    {
        var builder = new StringBuilder(1024);
        builder.Append('{');

        var first = true;
        foreach (var (light, dark) in ThemePalette.DarkMap)
        {
            if (!first) builder.Append(',');
            first = false;
            builder.Append(Json(light.ToUpperInvariant())).Append(':').Append(Json(dark));
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// 把光标 SVG 转成 CSS 的 cursor 值。
    /// 用 data URI 内联而不是外链文件：预览必须是一个文件，双击就能开，不能有同级依赖。
    /// </summary>
    private static string BuildCursors()
    {
        var assembly = typeof(Interactive).Assembly;
        var builder = new StringBuilder(16 * 1024);
        builder.Append('{');

        var first = true;
        foreach (var (id, hotspot) in CursorManifest())
        {
            using var stream = assembly.GetManifestResourceStream($"MathGeo.Cursor.{id}.svg");
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            var svg = reader.ReadToEnd();

            if (!first) builder.Append(',');
            first = false;

            var uri = "data:image/svg+xml," + Uri.EscapeDataString(svg);
            var fallback = id == "point" ? "crosshair" : id == "pan" ? "grab" : "default";

            builder.Append(Json(id)).Append(':')
                   .Append(Json($"url(\"{uri}\") {hotspot[0]} {hotspot[1]}, {fallback}"));
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string DefaultCursor()
    {
        var assembly = typeof(Interactive).Assembly;
        using var stream = assembly.GetManifestResourceStream("MathGeo.Cursor.point.svg");
        if (stream is null) return "crosshair";

        using var reader = new StreamReader(stream);
        return $"url(\"data:image/svg+xml,{Uri.EscapeDataString(reader.ReadToEnd())}\") 12 12, crosshair";
    }

    /// <summary>
    /// 光标清单。热点写在代码里而不是运行时读 manifest.json ——
    /// 少一次资源解析，而且这里本来就只有 8 条，改的时候一眼能看全。
    /// </summary>
    private static IEnumerable<(string Id, int[] Hotspot)> CursorManifest()
    {
        yield return ("select", [4, 3]);
        yield return ("point", [12, 12]);
        yield return ("segment", [4, 4]);
        yield return ("circle", [12, 3]);
        yield return ("angle", [4, 20]);
        yield return ("label", [12, 12]);
        yield return ("orbit", [12, 12]);
        yield return ("pan", [12, 12]);
    }

    // ————————————————————————— 转义 —————————————————————————

    private static string Json(string value)
        => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")}\"";

    private static string Html(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
