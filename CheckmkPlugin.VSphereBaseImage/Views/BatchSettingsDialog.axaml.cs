using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.UI;

namespace CheckmkPlugin.VSphereBaseImage.Views;

public partial class BatchSettingsDialog : ChromeWindow
{
    private readonly IBatchSettingsStore _store = null!;

    public BatchSettingsDialog(IBatchSettingsStore store)
    {
        AvaloniaXamlLoader.Load(this);
        _store = store;
        var s = store.Load();
        this.FindControl<TextBox>("ShareBox")!.Text = s.AgentShare;
        this.FindControl<TextBox>("ScriptBox")!.Text = s.AgentUpdateScript;
    }

    public BatchSettingsDialog() => AvaloniaXamlLoader.Load(this);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var s = _store.Load();
        s.AgentShare = (this.FindControl<TextBox>("ShareBox")!.Text ?? "").Trim();
        s.AgentUpdateScript = this.FindControl<TextBox>("ScriptBox")!.Text ?? "";
        _store.Save(s);
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
