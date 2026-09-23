using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace NekoConverter.App;

/// <summary>
/// Класс приложения Android: здесь приложение связывается с нашим App.
///
/// В Avalonia 12 связь переехала сюда из активити — раньше было
/// AvaloniaMainActivity&lt;App&gt;, теперь активити необобщённая, а тип
/// приложения задаётся этим классом. Атрибут [Application] регистрирует
/// класс в манифесте, поэтому отдельная запись android:name не нужна.
/// </summary>
[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(nint handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder);
}
