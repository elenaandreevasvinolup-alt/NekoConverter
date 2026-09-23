using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace NekoConverter.App;

/// <summary>
/// Настольное окно приложения.
///
/// Само окно пустое: оно только показывает <see cref="MainView"/>. Такой разрыв
/// нужен ради мобильных сборок — там Window показать нельзя, а тот же MainView
/// подставляется прямо в единственное представление экрана.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
