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
        this.FindControl<TextBox>("PowerOnBox")!.Text = s.PowerOnTimeoutSeconds.ToString();
        this.FindControl<TextBox>("ShutdownBox")!.Text = s.ShutdownTimeoutSeconds.ToString();
        this.FindControl<TextBox>("PollBox")!.Text = s.ToolsPollIntervalSeconds.ToString();
    }

    public BatchSettingsDialog() => AvaloniaXamlLoader.Load(this);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var s = _store.Load();
        s.AgentShare = (this.FindControl<TextBox>("ShareBox")!.Text ?? "").Trim();
        s.AgentUpdateScript = this.FindControl<TextBox>("ScriptBox")!.Text ?? "";
        s.PowerOnTimeoutSeconds = ParseInt(this.FindControl<TextBox>("PowerOnBox")!.Text, s.PowerOnTimeoutSeconds);
        s.ShutdownTimeoutSeconds = ParseInt(this.FindControl<TextBox>("ShutdownBox")!.Text, s.ShutdownTimeoutSeconds);
        s.ToolsPollIntervalSeconds = ParseInt(this.FindControl<TextBox>("PollBox")!.Text, s.ToolsPollIntervalSeconds);
        _store.Save(s);
        Close(true);
    }

    /// <summary>Parst eine Ganzzahl >= 0; leerer oder unlesbarer Text laesst den
    /// bisherigen Wert stehen — die Untergrenzen erzwingt am Ende der Runner.</summary>
    private static int ParseInt(string? text, int fallback)
        => int.TryParse((text ?? "").Trim(), out var v) && v >= 0 ? v : fallback;

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
