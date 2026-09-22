using System.Text;
using MathGeo.Core;

namespace MathGeo.Cli;

/// <summary>
/// 把一个文件夹里的题目渲成单文件 HTML 预览（静态版）。
///
/// 与 interactive 的分工：这一份是"一页看全"的样张 —— 适合打印、发给同事、
/// 贴进备课文档；那一份是能用手拖的交互演示。两者共用同一份显示列表和同一套配色。
///
/// 配色直接取内核那套设计令牌，所以预览的观感和软件本体一致；
/// 深浅主题由 --dark 切换，界面文案走语言表 —— 不在这里写死任何自然语言。
/// </summary>
internal static class Gallery
{
    private static Localizer _text = Localizer.Current;

    public static int Run(string[] args)
    {
        string? folder = null;
        string? output = null;
        var dark = false;
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
                case "--dark":
                    dark = true;
                    break;
                default:
                    if (!args[i].StartsWith('-')) folder ??= args[i];
                    break;
            }
        }

        if (lang is not null) Localizer.Use(lang);
        _text = Localizer.Current;

        if (folder is null)
        {
            Console.Error.WriteLine(_text["cli.error.missingFolder"]);
            return 2;
        }

        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine(_text.Format("cli.error.folderNotFound", folder));
            return 2;
        }

        var files = Directory.EnumerateFiles(folder, "*.problem.json").Order().ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine(_text.Format("cli.error.noSamples", folder));
            return 2;
        }

        var theme = dark ? Theme.Dark : Theme.Textbook;

        var html = new StringBuilder(64 * 1024);
        var rendered = 0;
        var failed = 0;

        WriteHead(html, files.Count, theme, dark);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);

            try
            {
                var problem = ProblemFile.FromJson(File.ReadAllText(file));
                if (problem is null)
                {
                    WriteErrorCard(html, name, "JSON parse returned null");
                    failed++;
                    continue;
                }

                var result = ProblemMapper.Load(problem);
                var svg = result.Scene.ToSvg(theme, new SvgOptions { TargetWidth = 720 });

                WriteCard(html, name, problem, svg, result.Diagnostics);
                rendered++;
            }
            catch (Exception ex)
            {
                // 一张卡片渲不出来不该让整份预览报废。
                WriteErrorCard(html, name, ex.Message);
                failed++;
            }
        }

        html.Append("</div>\n</body>\n</html>\n");

        var destination = output ?? Path.Combine(folder, "index.html");
        File.WriteAllText(destination, html.ToString());

        Console.WriteLine(_text.Format("cli.gallery.done", destination));
        Console.WriteLine(_text.Format("cli.gallery.summary", rendered, failed));

        return failed == 0 ? 0 : 1;
    }

    private static void WriteHead(StringBuilder html, int count, Theme theme, bool dark)
    {
        html.Append("<!DOCTYPE html>\n<html lang=\"").Append(_text.Lang)
            .Append("\" dir=\"").Append(_text.Rtl ? "rtl" : "ltr").Append("\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(Html(_text["gallery.title"])).Append("</title>\n<style>\n");

        html.Append($$"""
            :root {
              --bg: {{theme.PageBackground}}; --card: {{theme.Background}}; --border: {{theme.SurfaceBorder}};
              --text: {{theme.LabelFill}}; --muted: {{theme.HelperStroke}};
              --accent: {{theme.HighlightStroke}}; --accent-soft: {{theme.PolygonFill}};
            }
            * { box-sizing: border-box; }
            body {
              margin: 0; padding: 32px 24px 64px;
              background: var(--bg); color: var(--text);
              font: 15px/1.6 -apple-system, "Segoe UI", "Noto Sans SC", "Microsoft YaHei", sans-serif;
            }
            header { max-width: 1180px; margin: 0 auto 28px; }
            h1 { margin: 0 0 6px; font-size: 22px; font-weight: 600; }
            .subtitle { color: var(--muted); font-size: 13px; }
            .grid {
              max-width: 1180px; margin: 0 auto;
              display: grid; gap: 22px;
              grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
            }
            .card {
              background: var(--card); border: 1px solid var(--border);
              border-radius: 10px; padding: 16px; display: flex; flex-direction: column;
            }
            .card h2 { margin: 0 0 4px; font-size: 15px; font-weight: 600; }
            .file { color: var(--muted); font-size: 11px; font-family: Menlo, Consolas, monospace; }
            .statement { margin: 10px 0 12px; font-size: 14px; }
            .figure {
              background: var(--card); border: 1px solid var(--border); border-radius: 8px;
              overflow: hidden; display: flex; justify-content: center;
            }
            .figure svg { display: block; width: 100%; height: auto; }
            .answer {
              margin-top: 12px; padding: 10px 12px; border-radius: 8px;
              background: var(--accent-soft); font-size: 13px;
            }
            .answer .value { font-weight: 600; color: var(--accent); }
            .answer ol { margin: 6px 0 0; padding-inline-start: 20px; color: var(--muted); }
            .meta { margin-top: 10px; display: flex; flex-wrap: wrap; gap: 6px; }
            .tag {
              font-size: 11px; padding: 2px 8px; border-radius: 999px;
              background: var(--bg); color: var(--muted); border: 1px solid var(--border);
            }
            .diag {
              margin-top: 10px; padding: 8px 10px; border-radius: 8px; font-size: 12px;
              font-family: Menlo, Consolas, monospace; white-space: pre-wrap;
            }
            .diag.error { color: {{theme.AnswerStroke}}; }
            .diag.warning { color: {{theme.AngleStroke}}; }
            .bad-card { border-color: {{theme.AnswerStroke}}; }
            """);

        html.Append("</style>\n</head>\n<body>\n")
            .Append("<header><h1>").Append(Html(_text["gallery.title"])).Append("</h1>")
            .Append("<div class=\"subtitle\">")
            .Append(Html(_text.Format("gallery.subtitle", count)))
            .Append("</div></header>\n<div class=\"grid\">\n");
    }

    private static void WriteCard(
        StringBuilder html, string fileName, ProblemFile problem,
        string svg, IReadOnlyList<SolveDiagnostic> diagnostics)
    {
        html.Append("  <div class=\"card\">\n");
        html.Append("    <h2>").Append(Html(problem.Title ?? fileName)).Append("</h2>\n");
        html.Append("    <div class=\"file\">").Append(Html(fileName)).Append("</div>\n");

        if (!string.IsNullOrWhiteSpace(problem.Statement))
            html.Append("    <div class=\"statement\">")
                .Append(Html(problem.Statement)).Append("</div>\n");

        html.Append("    <div class=\"figure\">").Append(svg).Append("</div>\n");

        if (problem.Answer is { } answer
            && (!string.IsNullOrWhiteSpace(answer.Value) || answer.Steps.Count > 0))
        {
            html.Append("    <div class=\"answer\">");
            if (!string.IsNullOrWhiteSpace(answer.Value))
                html.Append("<span class=\"value\">").Append(Html(answer.Value)).Append("</span>");

            if (answer.Steps.Count > 0)
            {
                html.Append("<ol>");
                foreach (var step in answer.Steps)
                    html.Append("<li>").Append(Html(step)).Append("</li>");
                html.Append("</ol>");
            }
            html.Append("</div>\n");
        }

        html.Append("    <div class=\"meta\">");
        html.Append(Tag(problem.Kind));
        if (problem.Ask?.Target is { Length: > 0 } target)
            html.Append(Tag(_text.Format("gallery.ask", target)));
        if (problem.Ask?.Measure is { Length: > 0 } measure)
            html.Append(Tag(measure));
        if (problem.Source?.Textbook is { Length: > 0 } textbook)
            html.Append(Tag(_text.Format("gallery.source", textbook, problem.Source.Page ?? "")));
        html.Append(Tag(_text.Format("gallery.objects", problem.Objects.Count)));
        html.Append("</div>\n");

        foreach (var group in diagnostics.GroupBy(d => d.Severity))
        {
            var css = group.Key == DiagnosticSeverity.Error ? "error" : "warning";
            html.Append("    <div class=\"diag ").Append(css).Append("\">");
            foreach (var diagnostic in group)
                html.Append(Html(diagnostic.ToString())).Append('\n');
            html.Append("</div>\n");
        }

        html.Append("  </div>\n");
    }

    private static void WriteErrorCard(StringBuilder html, string fileName, string message)
    {
        html.Append("  <div class=\"card bad-card\">\n");
        html.Append("    <h2>").Append(Html(_text["gallery.failed"])).Append("</h2>\n");
        html.Append("    <div class=\"file\">").Append(Html(fileName)).Append("</div>\n");
        html.Append("    <div class=\"diag error\">").Append(Html(message)).Append("</div>\n");
        html.Append("  </div>\n");
    }

    private static string Tag(string text) => $"<span class=\"tag\">{Html(text)}</span>";

    private static string Html(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
