using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using MathGeo.App.Controls;
using MathGeo.Core;

namespace MathGeo.App;

/// <summary>
/// 无头渲染：把一道题画成 PNG，不开窗口。
///
/// 存在的理由有三个，都指向同一件事 —— 渲染结果要能被机器检查：
///   · CI 里验证"画布真的画得出东西"，不用人盯着屏幕；
///   · agent 批量出图，不需要窗口；
///   · 我改完渲染代码之后，能自己看一眼图对不对，而不是只能说"应该没问题"。
///
/// 它走的是和窗口完全相同的 SceneView，所以这里渲出来的图就是屏幕上会看到的图。
/// </summary>
internal static class RenderCommand
{
    public static int Run(string problemPath, string? outputPath, int width, int height)
    {
        if (!File.Exists(problemPath))
        {
            Console.Error.WriteLine($"找不到文件：{problemPath}");
            return 2;
        }

        ProblemFile? problem;
        try
        {
            problem = ProblemFile.FromJson(File.ReadAllText(problemPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"JSON 解析失败：{ex.Message}");
            return 1;
        }

        if (problem is null)
        {
            Console.Error.WriteLine("JSON 解析结果是空对象");
            return 1;
        }

        var result = ProblemMapper.Load(problem);

        // 用无头平台 + 真实 Skia 绘制：不建窗口，但走的是真正的渲染管线。
        // UseHeadlessDrawing = false 是关键 —— 默认的无头绘制是个空实现，
        // 渲出来会是一张白图，那样这个功能就白做了。
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            })
            .UseSkia()
            .SetupWithoutStarting();

        var scene = result.Scene;
        scene.View = scene.View with { ShowGrid = true };

        // 用和窗口一样的组合：画布 + 浮在右上角的视角立方体。
        // 这样渲出来的 PNG 就是窗口里会看到的东西 —— 包括视角立方体本身，
        // 否则它就成了"只在有窗口时才能验证"的盲区。
        var root = new Grid { Width = width, Height = height };

        var canvas = new SceneView { Width = width, Height = height };
        canvas.SetShapes(scene.BuildDisplayList(Theme.Textbook));
        root.Children.Add(canvas);

        if (scene.Objects.OfType<GeoPolyhedron>().Any())
        {
            var cube = new ViewCube
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                Camera = scene.Camera ?? Camera.Textbook,
            };
            root.Children.Add(cube);
        }

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(root);

        var destination = outputPath ?? Path.ChangeExtension(problemPath, ".png");

        using (var stream = File.Create(destination))
            bitmap.Save(stream);

        Console.WriteLine($"已输出 {destination}（{scene.BuildDisplayList(Theme.Textbook).Count} 个图元，{width}×{height}）");

        return result.Success ? 0 : 1;
    }
}
