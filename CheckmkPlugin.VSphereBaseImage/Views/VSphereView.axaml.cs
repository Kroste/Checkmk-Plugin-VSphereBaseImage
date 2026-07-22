using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CheckmkPlugin.VSphereBaseImage.Models;
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
    private readonly VmRemoteTools? _remote;

    public VSphereView(
        VSphereViewModel vm,
        ICredentialStore credStore,
        IDdcCredentialStore ddcStore,
        IBatchSettingsStore batchSettings,
        VmFilterCollection filters,
        VmRemoteTools remote)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
        _credStore = credStore;
        _ddcStore = ddcStore;
        _batchSettings = batchSettings;
        _filters = filters;
        _remote = remote;

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

        var visible = vm.VisibleVms.ToList();
        if (visible.Count == 0) return;

        // 1) Kataloge vom DDC holen (leere Liste falls DDC-Auth fehlt / nicht
        //    erreichbar). Der User kann trotzdem batchen — dann eben ohne
        //    Katalog-Publish. Das entkoppelt vSphere-Update von Citrix-Erreichbarkeit.
        var catalogs = await vm.LoadCatalogsAsync();

        // 2) Katalog-Picker: pro VM Vorbelegung via Praefix-Aehnlichkeit,
        //    User kann manuell umstellen oder leer lassen.
        var pickerVm = new CatalogPickerViewModel(visible, catalogs);
        var assignments = await new CatalogPickerDialog(pickerVm)
            .ShowDialog<IReadOnlyList<VmCatalogAssignment>?>(owner);
        if (assignments is null) return;

        // 3) Admin-Credentials fuer Remote-PowerShell (nicht persistiert,
        //    nur im Prozess-Memory bis Batch fertig).
        var creds = await new CredentialDialog(
            "Admin-Anmeldung fuer Remote-PowerShell auf die Baseimage-VMs")
            .ShowDialog<CredentialResult?>(owner);
        if (creds is null) return;

        await vm.RunBatchAsync(assignments, creds.User, creds.Password);
    }

    // --- Kontextmenue am VM-Grid ------------------------------------------

    private VmInfo? SelectedVm()
        => DataContext is VSphereViewModel vm ? vm.SelectedVm : null;

    private void OnVmDoubleTapped(object? sender, TappedEventArgs e) => OpenVmDetails();

    private void OnOpenVmDetailsClick(object? sender, RoutedEventArgs e) => OpenVmDetails();

    private void OpenVmDetails()
    {
        if (SelectedVm() is not { } vm) return;
        if (_credStore is null || _remote is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var detailVm = new VmDetailViewModel(vm, _credStore);
        new VmDetailWindow(detailVm, _remote).Show(owner);
    }

    private void OnRdpClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedVm() is { Name: { Length: > 0 } n }) _remote?.StartRdp(n);
    }

    private void OnPingClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedVm() is { Name: { Length: > 0 } n }) _remote?.StartPing(n);
    }

    private async void OnCopyVmNameClick(object? sender, RoutedEventArgs e)
        => await CopyAsync(SelectedVm()?.Name);

    private async void OnCopyVmIdClick(object? sender, RoutedEventArgs e)
        => await CopyAsync(SelectedVm()?.Id);

    private async Task CopyAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null) return;
        await clip.SetTextAsync(text);
    }
}
