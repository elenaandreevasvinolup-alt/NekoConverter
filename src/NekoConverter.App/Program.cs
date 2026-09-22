using Avalonia;

namespace NekoConverter.App;

internal static class Program
{
#if !ANDROID && !IOS
    /// <summary>
    /// Путь для режима предпросмотра интерфейса.
    ///
    /// Зачем: проверить, что тема и вёрстка действительно применились, можно только
    /// по пикселям. Снимок экрана зависит от того, какое окно сейчас сверху,
    /// а этот режим рендерит окно в файл детерминированно — без оконного менеджера.
    ///
    /// Использование:
    ///   NekoConverter --preview out.png --theme dark
    /// </summary>
    public static string? PreviewPath { get; private set; }

    /// <summary>Тема для режима предпросмотра. null — взять из настроек.</summary>
    public static ThemeMode? PreviewTheme { get; private set; }

    /// <summary>
    /// Файлы, которые нужно «загрузить» в предпросмотре, через запятую.
    /// Несколько файлов нужны, чтобы снять состояние с несколькими группами.
    /// </summary>
    public static string[] PreviewFiles { get; private set; } = [];

    /// <summary>
    /// Пара «исходный, результат» для предпросмотра окна сравнения.
    /// Нужна, чтобы снять это окно без ручного нажатия кнопки.
    /// </summary>
    public static (string Original, string Result)? PreviewCompare { get; private set; }

    /// <summary>Раскрыть блок «高级设置» в предпросмотре.</summary>
    public static bool PreviewExpert { get; private set; }

    /// <summary>
    /// Запуск в режиме MCP-сервера: без окна, обмен JSON-RPC через стандартные потоки.
    ///
    /// Так приложению не нужен отдельный исполняемый файл для MCP: тот же самый
    /// бинарник умеет и интерфейс, и сервер. Иначе в поставку пришлось бы класть
    /// вторую копию всего кода, а это лишние десятки мегабайт.
    /// </summary>
    public static bool McpMode { get; private set; }

    /// <summary>Какую страницу показать в предпросмотре: convert, modules, settings, about.</summary>
    public static string? PreviewPage { get; private set; }

    /// <summary>
    /// Размер окна для предпросмотра в виде ШИРИНАxВЫСОТА.
    /// Нужен, чтобы проверять отзывчивую вёрстку: телефон, планшет, десктоп.
    /// </summary>
    public static (int Width, int Height)? PreviewSize { get; private set; }

    /// <summary>
    /// Точка входа настольных сборок.
    /// На Android и iOS точку входа даёт сама платформа (MainActivity и AppDelegate),
    /// поэтому здесь метод не компилируется — иначе было бы две точки входа.
    /// </summary>
#if !ANDROID && !IOS
    [STAThread]
    public static int Main(string[] args)
    {
        ParsePreviewArguments(args);

        if (McpMode)
        {
            // Окно не создаётся вовсе: сервер общается только через потоки.
            return NekoConverter.Core.Mcp.McpServer.RunAsync().GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
#endif

    private static void ParsePreviewArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preview-compare" when i + 1 < args.Length:
                    var pair = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (pair.Length == 2)
                    {
                        PreviewCompare = (pair[0], pair[1]);
                    }

                    i++;
                    break;

                case "--mcp":
                    McpMode = true;
                    break;

                case "--preview" when i + 1 < args.Length:
                    PreviewPath = args[i + 1];
                    i++;
                    break;

                case "--preview-file" when i + 1 < args.Length:
                    PreviewFiles = args[i + 1]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    i++;
                    break;

                case "--preview-expert":
                    PreviewExpert = true;
                    break;

                case "--preview-page" when i + 1 < args.Length:
                    PreviewPage = args[i + 1];
                    i++;
                    break;

                case "--size" when i + 1 < args.Length:
                    var parts = args[i + 1].Split('x', 'X');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out var width) &&
                        int.TryParse(parts[1], out var height))
                    {
                        PreviewSize = (width, height);
                    }

                    i++;
                    break;

                case "--theme" when i + 1 < args.Length:
                    if (Enum.TryParse<ThemeMode>(args[i + 1], ignoreCase: true, out var mode))
                    {
                        PreviewTheme = mode;
                    }

                    i++;
                    break;
            }
        }
    }

    // Этот метод также используется дизайнером XAML, поэтому имя и сигнатура стандартные.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
#endif
}
