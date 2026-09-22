using Avalonia;

namespace MathGeo.App;

internal static class Program
{
    // Avalonia 需要 STA 线程来建窗口。用 classic desktop lifetime，
    // 启动、退出、窗口生命周期都交给框架，外壳不自己管消息循环。
    [STAThread]
    public static void Main(string[] args)
    {
        // 无头渲染模式：--render <题目.json> [输出.png] [宽] [高]
        // 放在建窗口之前，所以它不依赖窗口系统。
        if (args.Length >= 2 && args[0] == "--render")
        {
            var width = args.Length > 3 && int.TryParse(args[3], out var w) ? w : 960;
            var height = args.Length > 4 && int.TryParse(args[4], out var h) ? h : 720;

            Environment.ExitCode = RenderCommand.Run(args[1], args.Length > 2 ? args[2] : null, width, height);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
