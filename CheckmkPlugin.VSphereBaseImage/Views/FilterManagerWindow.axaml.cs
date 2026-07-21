using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.UI;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

public partial class FilterManagerWindow : ChromeWindow
{
    public FilterManagerWindow(VmFilterCollection filters)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new FilterManagerViewModel(filters);
    }

    // Parameterloser ctor nur fuer den XAML-Designer.
    public FilterManagerWindow() => AvaloniaXamlLoader.Load(this);

    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();
}
