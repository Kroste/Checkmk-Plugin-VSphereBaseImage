using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CheckmkPlugin.VSphereBaseImage.UI;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

public partial class BatchReportDialog : ChromeWindow
{
    private readonly BatchReportViewModel? _vm;

    public BatchReportDialog(BatchReportViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        _vm = vm;
        DataContext = vm;
    }

    public BatchReportDialog() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyReportClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var clip = Clipboard;
        if (clip is null) return;
        await clip.SetTextAsync(_vm.BuildReport());
    }

    private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Batch-Log speichern",
            SuggestedFileName = $"vsphere-batch-{_vm.FinishedAt:yyyyMMdd-HHmm}.log",
            DefaultExtension = "log",
            FileTypeChoices = [new FilePickerFileType("Log") { Patterns = ["*.log", "*.txt"] }]
        });
        if (file is null) return;
        try
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, _vm.LogText ?? "");
        }
        catch
        {
            // Fehler landet nicht im Modal — Log-Panel-Kontextmenue ist der
            // primaere Speicher-Weg; hier nur Bequemlichkeit.
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
