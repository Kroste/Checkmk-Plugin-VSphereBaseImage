using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CheckmkPlugin.VSphereBaseImage.Models;
using CheckmkPlugin.VSphereBaseImage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.ViewModels;

/// <summary>
/// ViewModel des VM-Detail-Fensters (analog Cockpit-<c>HostDetailViewModel</c>).
/// Zeigt Guest-Identity, Netzwerk-IP und die Snapshot-Liste; erlaubt manuelles
/// Loeschen einzelner Snapshots. Das Detail-Fenster oeffnet on-demand einen
/// eigenen <see cref="VSphereClient"/>, damit es unabhaengig vom Batch-Client
/// im ViewModel lebt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class VmDetailViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly ICredentialStore _credStore;

    public VmInfo Vm { get; }

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _guestHostName;
    [ObservableProperty] private string? _guestIpAddress;
    [ObservableProperty] private string? _guestFullName;
    [ObservableProperty] private string? _toolsRunState;
    [ObservableProperty] private VmSnapshotInfo? _selectedSnapshot;

    public ObservableCollection<VmSnapshotInfo> Snapshots { get; } = new();

    public VmDetailViewModel(VmInfo vm, ICredentialStore credStore)
    {
        Vm = vm;
        _credStore = credStore;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var creds = _credStore.Load();
        var pw = _credStore.DecryptPassword(creds);
        if (string.IsNullOrWhiteSpace(creds.UserName) || string.IsNullOrEmpty(pw))
        {
            StatusMessage = "vCenter-Anmeldung fehlt.";
            return;
        }

        IsBusy = true;
        try
        {
            using var client = new VSphereClient(creds, pw);

            // Guest-Info + Tools nur wenn VM laeuft — sonst 400/leer.
            if (Vm.IsPoweredOn)
            {
                var guest = await client.GetGuestIdentityAsync(Vm.Id);
                GuestHostName = guest?.HostName;
                GuestIpAddress = guest?.IpAddress;
                GuestFullName = guest?.FullName?.DefaultMessage;
                var tools = await client.GetToolsInfoAsync(Vm.Id);
                ToolsRunState = tools?.RunState;
            }
            else
            {
                GuestHostName = null;
                GuestIpAddress = null;
                GuestFullName = null;
                ToolsRunState = "(VM aus)";
            }

            Snapshots.Clear();
            var list = await client.ListSnapshotsAsync(Vm.Id);
            foreach (var s in list.OrderByDescending(s => s.CreateTime ?? DateTimeOffset.MinValue)
                                  .ThenByDescending(s => s.Name, StringComparer.Ordinal))
                Snapshots.Add(s);

            StatusMessage = $"{Snapshots.Count} Snapshot(s) — Stand {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "VM-Detail-Refresh {Vm} fehlgeschlagen.", Vm.Name);
            StatusMessage = $"Fehler beim Laden: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync(VmSnapshotInfo? snap)
    {
        snap ??= SelectedSnapshot;
        if (snap is null) return;

        var creds = _credStore.Load();
        var pw = _credStore.DecryptPassword(creds);
        if (string.IsNullOrEmpty(pw)) { StatusMessage = "vCenter-Passwort nicht verfuegbar."; return; }

        IsBusy = true;
        try
        {
            using var client = new VSphereClient(creds, pw);
            await client.DeleteSnapshotAsync(Vm.Id, snap.Id);
            Snapshots.Remove(snap);
            StatusMessage = $"Snapshot '{snap.Name}' geloescht.";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Snapshot-Delete {Vm}/{Snap} fehlgeschlagen.", Vm.Name, snap.Name);
            StatusMessage = $"Loeschen fehlgeschlagen: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
