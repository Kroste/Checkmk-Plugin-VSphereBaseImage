using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.Versioning;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.VSphereBaseImage.Models;
using CheckmkPlugin.VSphereBaseImage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class VSphereViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICredentialStore _credStore;
    private readonly IBatchSettingsStore _batchSettings;
    private readonly IAgentUpdater _updater;

    private List<VmInfo> _allVms = new();

    /// <summary>Live-State der Filter — analog Cockpit-HostFilterCollection.
    /// XAML bindet an <c>Filters.Filters</c> (ItemsSource) und <c>Filters.Active</c>
    /// (SelectedItem). Aenderungen im FilterManager greifen automatisch, weil die
    /// Collection Singleton ist.</summary>
    public VmFilterCollection Filters { get; }

    public ObservableCollection<VmInfo> VisibleVms { get; } = new();

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private VmInfo? _selectedVm;

    /// <summary>Freitext-Verfeinerung ueber den aktiven Filter (case-insensitive
    /// Contains ueber VM-Name und Power-State). Analog zum Freitext-Feld im
    /// Cockpit-Status-Tab.</summary>
    [ObservableProperty] private string _filterText = "";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public string BatchButtonLabel =>
        Filters.Active is null
            ? $"Batch starten ({VisibleVms.Count} VMs)"
            : $"Batch starten ({VisibleVms.Count} VMs im Filter „{Filters.Active.Name}\")";

    public VSphereViewModel(
        ICredentialStore credStore,
        VmFilterCollection filters,
        IBatchSettingsStore batchSettings,
        IAgentUpdater updater)
    {
        _credStore = credStore;
        Filters = filters;
        _batchSettings = batchSettings;
        _updater = updater;

        // Auf Filterwechsel und Filter-Bearbeitung reagieren.
        Filters.PropertyChanged += OnFiltersPropertyChanged;
        Filters.Filters.CollectionChanged += (_, _) => ApplyFilter();
    }

    private void OnFiltersPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VmFilterCollection.Active))
            ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var creds = _credStore.Load();
        var pw = _credStore.DecryptPassword(creds);
        if (string.IsNullOrWhiteSpace(creds.UserName) || string.IsNullOrEmpty(pw))
        {
            StatusMessage = "vCenter-Anmeldung fehlt — bitte in den Plugin-Einstellungen setzen.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Lade VMs von {creds.VCenterUrl}…";
        try
        {
            using var client = new VSphereClient(creds, pw);
            _allVms = (await client.ListVmsAsync()).OrderBy(v => v.Name).ToList();
            ApplyFilter();
            StatusMessage = $"{_allVms.Count} VMs geladen (davon {VisibleVms.Count} im Filter).";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "vCenter-Refresh fehlgeschlagen.");
            StatusMessage = $"Fehler beim Laden: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        VisibleVms.Clear();
        IEnumerable<VmInfo> q = _allVms;
        if (Filters.Active is { } f)
            q = q.Where(v => f.Matches(v.Name));
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var needle = FilterText.Trim();
            q = q.Where(v =>
                v.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || v.PowerState.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var v in q) VisibleVms.Add(v);
        OnPropertyChanged(nameof(BatchButtonLabel));
    }

    /// <summary>Fuehrt das Batch-Update auf allen sichtbaren VMs aus. Admin-
    /// Credentials (fuer Remote-PowerShell auf den Gast-VMs) werden als
    /// Parameter uebergeben — Aufrufer holt sie vorher im Credential-Dialog.</summary>
    public async Task RunBatchAsync(string adminUser, string adminPassword, CancellationToken ct = default)
    {
        var vms = VisibleVms.ToList();
        if (vms.Count == 0)
        {
            StatusMessage = "Keine VMs im aktiven Filter — Batch abgebrochen.";
            return;
        }

        var creds = _credStore.Load();
        var vcPw = _credStore.DecryptPassword(creds);
        if (string.IsNullOrEmpty(vcPw))
        {
            StatusMessage = "vCenter-Passwort nicht verfuegbar.";
            return;
        }

        var pluginCfg = _batchSettings.Load();

        IsBusy = true;
        LogText = "";
        StatusMessage = $"Batch startet fuer {vms.Count} VM(s).";
        try
        {
            using var client = new VSphereClient(creds, vcPw);
            var runner = new BatchRunner(client, _updater);

            var options = new BatchOptions(
                AdminUser: adminUser,
                AdminPassword: adminPassword,
                AgentShare: pluginCfg.AgentShare,
                ScriptTemplate: pluginCfg.AgentUpdateScript,
                PowerOnTimeout: TimeSpan.FromMinutes(10),
                ShutdownTimeout: TimeSpan.FromMinutes(5),
                ToolsPollInterval: TimeSpan.FromSeconds(5));

            var progress = new Progress<string>(line =>
            {
                LogText += line + Environment.NewLine;
                StatusMessage = line;
            });

            var results = await runner.RunAsync(vms, options, progress, ct);
            var ok = results.Count(r => r.Success);
            StatusMessage = $"Batch beendet: {ok}/{results.Count} erfolgreich.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Batch abgebrochen.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Batch-Runner fatal.");
            StatusMessage = $"Batch-Fehler: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
