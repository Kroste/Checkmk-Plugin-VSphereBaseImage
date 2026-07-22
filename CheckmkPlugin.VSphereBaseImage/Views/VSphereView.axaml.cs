using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

[SupportedOSPlatform("windows")]
public partial class VSphereView : UserControl
{
    private readonly ICredentialStore? _credStore;
    private readonly IDdcCredentialStore? _ddcStore;
    private readonly IBatchSettingsStore? _batchSettings;
    private readonly VmFilterCollection? _filters;

    public VSphereView(
        VSphereViewModel vm,
        ICredentialStore credStore,
        IDdcCredentialStore ddcStore,
        IBatchSettingsStore batchSettings,
        VmFilterCollection filters)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
        _credStore = credStore;
        _ddcStore = ddcStore;
        _batchSettings = batchSettings;
        _filters = filters;

        // Ctrl+F von ueberall im Tab fokussiert das Freitext-Feld — analog Cockpit.
        AddHandler(KeyDownEvent, OnTabKeyDown, RoutingStrategies.Tunnel);
    }

    public VSphereView() => AvaloniaXamlLoader.Load(this);

    private void OnTabKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var box = this.FindControl<TextBox>("FilterTextBox");
            if (box is not null)
            {
                box.Focus();
                box.SelectAll();
                e.Handled = true;
            }
        }
    }

    private void OnFilterTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is TextBox tb)
        {
            tb.Text = "";
            e.Handled = true;
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_credStore is null || _ddcStore is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new SettingsDialog(_credStore, _ddcStore);
        await dlg.ShowDialog(owner);
    }

    private async void OnBatchSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_batchSettings is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new BatchSettingsDialog(_batchSettings);
        await dlg.ShowDialog(owner);
    }

    private async void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (_filters is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await new FilterManagerWindow(_filters).ShowDialog(owner);
    }

    private async void OnBatchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VSphereViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var creds = await new CredentialDialog(
            "Admin-Anmeldung fuer Remote-PowerShell auf die Baseimage-VMs")
            .ShowDialog<CredentialResult?>(owner);
        if (creds is null) return;

        await vm.RunBatchAsync(creds.User, creds.Password);
    }
}
