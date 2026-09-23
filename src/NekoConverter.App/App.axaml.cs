using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NekoConverter.App.Localization;

// Под Android есть собственный Android.App.Application: без псевдонима
// компилятор не понимает, от какого класса наследуется App.
using Application = Avalonia.Application;

namespace NekoConverter.App;

public partial class App : Application
{
    public override void Initialize()
    {
        // Имя показывается в системной строке macOS. Без него там красуется
        // «Avalonia App» — имя по умолчанию, которое ничего не говорит о программе.
        Name = "NekoConverter";

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Язык применяется здесь, а не в главном окне.
        //
        // Причина: окно сравнения можно открыть и без главного окна (в режиме
        // предпросмотра), и тогда строки оставались бы именами ключей вроде
        // «compare.title». Ресурсы приложения должны быть готовы до любого окна.
        var settings = AppSettings.Load();

        Localizer.UserLocaleDirectory = Path.Combine(AppSettings.DataDirectory, "Locale");
        Directory.CreateDirectory(Localizer.UserLocaleDirectory);
        Localizer.RefreshAvailableLanguages();
        Localizer.Apply(settings.Language);

        // Пока поддерживается только классический десктопный режим (macOS/Windows/Linux).
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if !ANDROID && !IOS
            if (Program.PreviewTheme is { } theme)
            {
                AppSettings.ForceTheme = theme;
            }

            // Окно сравнения — отдельное окно, поэтому в режиме предпросмотра
            // показываем именно его, а не главное.
            if (Program.PreviewCompare is { } compare)
            {
                var compareWindow = new CompareWindow(compare.Original, compare.Result);

                if (Program.PreviewSize is { } compareSize)
                {
                    compareWindow.Width = compareSize.Width;
                    compareWindow.Height = compareSize.Height;
                }

                desktop.MainWindow = compareWindow;

                if (Program.PreviewPath is { } comparePreviewPath)
                {
                    RenderPreview(desktop, compareWindow, comparePreviewPath);
                }

                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow();

            // Размер задаётся до показа окна, чтобы вёрстка сразу считалась нужной.
            if (Program.PreviewSize is { } size)
            {
                window.Width = size.Width;
                window.Height = size.Height;
            }

            desktop.MainWindow = window;

            if (Program.PreviewPath is { } previewPath)
            {
                RenderPreview(desktop, window, previewPath);
            }
#else
            // На телефоне аргументов командной строки нет, поэтому предпросмотр
            // и окно сравнения недоступны — просто показываем главное окно.
            desktop.MainWindow = new MainWindow();
#endif
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Мобильные платформы: окна как такового нет, есть единственное
            // представление на весь экран. Оно переиспользует ту же вёрстку,
            // что и настольное окно, — отдельного мобильного интерфейса нет.
            singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Рендерит окно в PNG и выходит. Нужен для проверки темы и вёрстки без оконного менеджера:
    /// снимок экрана зависит от того, какое окно оказалось сверху, а это — нет.
    /// </summary>
    private static void RenderPreview(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window window,
        string path)
    {
        window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
        {
            try
            {
                var size = new PixelSize(
                    Math.Max(1, (int)window.Bounds.Width),
                    Math.Max(1, (int)window.Bounds.Height));

                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(window);

                // В Avalonia 12 перегрузка Save(string, int?) устарела — нужен явный
                // выбор кодера. Для снимка интерфейса PNG подходит лучше всего: без потерь.
                bitmap.Save(path, PngBitmapEncoderOptions.Default);

                Console.WriteLine($"Предпросмотр сохранён: {path} ({size.Width}x{size.Height})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Не удалось сохранить предпросмотр: {ex.Message}");
            }

            desktop.Shutdown();
        }, TimeSpan.FromMilliseconds(900));
    }
}
