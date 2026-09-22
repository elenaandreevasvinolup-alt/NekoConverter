using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace NekoConverter.App;

/// <summary>
/// Точка входа Android.
///
/// ConfigurationChanges перечисляет то, что система не должна пересоздавать активити:
/// поворот экрана, изменение размера и смена темы. Без этого при повороте телефона
/// приложение перезапускалось бы и пользователь терял выбранный файл.
/// </summary>
[Activity(
    Label = "NekoConverter",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder);
}
