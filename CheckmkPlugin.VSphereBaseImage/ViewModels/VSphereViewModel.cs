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
    private readonly IDdcCredentialStore _ddcStore;
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

    /// <summary>CTS des laufenden Batches — wird beim Cancel-Button ausgeloest,
    /// beendet aber nur die Warteschritte (Tools/Ping/Off) sauber. Die
    /// aktuelle VM wird zu Ende gefuehrt, weitere VMs werden uebersprungen.</summary>
    private CancellationTokenSource? _batchCts;

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
        IDdcCredentialStore ddcStore,
        VmFilterCollection filters,
        IBatchSettingsStore batchSettings,
        IAgentUpdater updater)
    {
        _credStore = credStore;
        _ddcStore = ddcStore;
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

    /// <summary>Holt vor dem Batch die Machine-Catalog-Liste vom DDC. Rueckgabe
    /// leere Liste wenn DDC-Auth fehlt oder API nicht erreichbar — dann laeuft
    /// der Batch ohne Katalog-Publish weiter (nur Agent-Update + Snapshot).</summary>
    public async Task<IReadOnlyList<CitrixMachineCatalog>> LoadCatalogsAsync(CancellationToken ct = default)
    {
        var ddc = _ddcStore.Load();
        var pw = _ddcStore.DecryptPassword(ddc);
        if (string.IsNullOrWhiteSpace(ddc.UserName) || string.IsNullOrEmpty(pw))
        {
            Log.Debug("DDC-Anmeldung nicht konfiguriert — Katalog-Publish wird uebersprungen.");
            return Array.Empty<CitrixMachineCatalog>();
        }
        try
        {
            using var citrix = new CitrixClient(ddc, pw);
            return await citrix.ListMachineCatalogsAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Machine-Catalog-Liste konnte nicht geholt werden.");
            return Array.Empty<CitrixMachineCatalog>();
        }
    }

    /// <summary>Fuehrt das Batch-Update auf den zugeordneten VMs aus. Admin-
    /// Credentials (fuer Remote-PowerShell) und die Katalog-Zuordnung werden
    /// vom Aufrufer via <see cref="Views.CredentialDialog"/> und
    /// <see cref="Views.CatalogPickerDialog"/> geholt.</summary>
    public async Task RunBatchAsync(
        IReadOnlyList<VmCatalogAssignment> assignments,
        string adminUser, string adminPassword,
        CancellationToken ct = default)
    {
        if (assignments.Count == 0)
        {
            StatusMessage = "Keine VMs zugeordnet — Batch abgebrochen.";
            return;
        }

        // Interne CTS mit dem externen Token koppeln, damit sowohl der
        // Cancel-Button (CancelBatchCommand) als auch ein aeusserer Abbruch
        // greifen. Alte CTS ggf. entsorgen.
        _batchCts?.Dispose();
        _batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var batchCt = _batchCts.Token;

        var creds = _credStore.Load();
        var vcPw = _credStore.DecryptPassword(creds);
        if (string.IsNullOrEmpty(vcPw))
        {
            StatusMessage = "vCenter-Passwort nicht verfuegbar.";
            return;
        }

        var pluginCfg = _batchSettings.Load();
        var ddc = _ddcStore.Load();
        var ddcPw = _ddcStore.DecryptPassword(ddc);

        IsBusy = true;
        LogText = "";
        StatusMessage = $"Batch startet fuer {assignments.Count} VM(s).";
        try
        {
            using var vsphere = new VSphereClient(creds, vcPw);

            // CitrixClient nur wenn Anmeldung konfiguriert UND mindestens eine
            // VM einen Katalog zugeordnet hat. Sonst spart das die unnoetige
            // Auth-Runde.
            CitrixClient? citrix = null;
            if (!string.IsNullOrWhiteSpace(ddc.UserName) && !string.IsNullOrEmpty(ddcPw)
                && assignments.Any(a => a.Catalog is not null))
            {
                citrix = new CitrixClient(ddc, ddcPw);
            }

            try
            {
                var runner = new BatchRunner(vsphere, _updater, citrix);
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

                var results = await runner.RunAsync(assignments, options, progress, batchCt);
                var ok = results.Count(r => r.Success);
                var published = results.Count(r => r.CatalogPublished is not null);
                StatusMessage = $"Batch beendet: {ok}/{results.Count} erfolgreich, {published} Kataloge publiziert.";
            }
            finally { citrix?.Dispose(); }
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
        finally
        {
            IsBusy = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    [RelayCommand]
    private void CancelBatch()
    {
        if (_batchCts is null || _batchCts.IsCancellationRequested) return;
        StatusMessage = "Batch wird abgebrochen — bitte kurz warten…";
        _batchCts.Cancel();
    }
}
