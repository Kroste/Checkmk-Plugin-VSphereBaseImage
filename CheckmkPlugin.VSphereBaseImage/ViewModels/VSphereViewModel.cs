using System.Collections.ObjectModel;
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
    private readonly IVmFilterStore _filterStore;
    private readonly IBatchSettingsStore _batchSettings;
    private readonly IAgentUpdater _updater;

    private List<VmInfo> _allVms = new();

    public ObservableCollection<VmInfo> VisibleVms { get; } = new();
    public ObservableCollection<VmFilter> Filters { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchButtonLabel))]
    private VmFilter? _activeFilter;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private VmInfo? _selectedVm;

    public string BatchButtonLabel =>
        ActiveFilter is null
            ? $"Batch starten ({VisibleVms.Count} VMs)"
            : $"Batch starten ({VisibleVms.Count} VMs im Filter „{ActiveFilter.Name}\")";

    partial void OnActiveFilterChanged(VmFilter? value)
    {
        ApplyFilter();
        var state = _filterStore.Load();
        state.ActiveFilterName = value?.Name;
        _filterStore.Save(state);
    }

    public VSphereViewModel(
        ICredentialStore credStore,
        IVmFilterStore filterStore,
        IBatchSettingsStore pluginSettings,
        IAgentUpdater updater)
    {
        _credStore = credStore;
        _filterStore = filterStore;
        _batchSettings = pluginSettings;
        _updater = updater;

        var s = _filterStore.Load();
        foreach (var f in s.Filters) Filters.Add(f);
        _activeFilter = string.IsNullOrEmpty(s.ActiveFilterName)
            ? null
            : Filters.FirstOrDefault(f => f.Name == s.ActiveFilterName);
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
        if (ActiveFilter is { } f)
            q = q.Where(v => f.Matches(v.Name));
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
