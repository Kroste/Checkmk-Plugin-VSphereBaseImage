using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Services;
using CheckmkPlugin.VSphereBaseImage.ViewModels;

namespace CheckmkPlugin.VSphereBaseImage.Views;

[SupportedOSPlatform("windows")]
public partial class VSphereView : UserControl
{
    private readonly ICredentialStore? _credStore;
    private readonly IBatchSettingsStore? _batchSettings;
    private readonly IVmFilterStore? _filterStore;

    public VSphereView(
        VSphereViewModel vm,
        ICredentialStore credStore,
        IBatchSettingsStore batchSettings,
        IVmFilterStore filterStore)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
        _credStore = credStore;
        _batchSettings = batchSettings;
        _filterStore = filterStore;
    }

    public VSphereView() => AvaloniaXamlLoader.Load(this);

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_credStore is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new SettingsDialog(_credStore);
        await dlg.ShowDialog(owner);
    }

    private async void OnBatchSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_batchSettings is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new BatchSettingsDialog(_batchSettings);
        await dlg.ShowDialog(owner);
    }

    private void OnManageFiltersClick(object? sender, RoutedEventArgs e)
    {
        // MVP: filter.json im OS-Editor oeffnen. Ein richtiger Filter-Manager
        // wie im Cockpit kommt in einer spaeteren Version.
        if (_filterStore is null) return;
        try
        {
            var path = _filterStore.FilePath;
            if (!File.Exists(path))
                _filterStore.Save(new Models.VmFilterState());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* User kann's manuell im Explorer aufmachen */ }
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
