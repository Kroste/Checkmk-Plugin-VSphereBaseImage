using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Models;
using CheckmkPlugin.VSphereBaseImage.UI;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

public partial class CatalogPickerDialog : ChromeWindow
{
    private readonly CatalogPickerViewModel? _vm;

    public CatalogPickerDialog(CatalogPickerViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;
        DataContext = vm;
    }

    public CatalogPickerDialog() => AvaloniaXamlLoader.Load(this);

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close((IReadOnlyList<VmCatalogAssignment>?)null);

    private void OnStartClick(object? sender, RoutedEventArgs e)
        => Close(_vm?.BuildAssignments());
}
