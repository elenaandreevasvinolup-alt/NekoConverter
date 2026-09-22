using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace NekoConverter.App;

/// <summary>
/// Точка входа iOS.
///
/// Отдельного кода не требует: весь интерфейс общий с десктопом, а мобильная
/// вёрстка включается сама по ширине экрана.
/// </summary>
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder);
}
