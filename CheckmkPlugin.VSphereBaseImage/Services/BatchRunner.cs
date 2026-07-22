using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.VSphereBaseImage.Models;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

public sealed record BatchStepResult(
    string VmName,
    bool Success,
    string Message,
    string? SnapshotName = null,
    string? SnapshotId = null,
    string? CatalogPublished = null);

public sealed record BatchOptions(
    string AdminUser,
    string AdminPassword,
    string AgentShare,
    string ScriptTemplate,
    TimeSpan PowerOnTimeout,
    TimeSpan ShutdownTimeout,
    TimeSpan ToolsPollInterval);

/// <summary>
/// Orchestriert den Baseimage-Update-Workflow ueber eine Liste von VMs:
///  Power-State merken -> (falls aus) PowerOn -> warten bis VMware-Tools laufen
///  -> warten bis der Guest per Ping erreichbar ist -> Agent-Update via
///  <see cref="IAgentUpdater"/> aus Plugin 1 -> Guest-Shutdown -> warten bis
///  Power-Off -> Snapshot anlegen (Aus-Zustand ist der saubere Snapshot-Punkt)
///  -> falls die VM vor dem Update AN war: wieder PowerOn (Power-State-Restore).
///
/// Fehler bei einer VM brechen die Batch NICHT ab; sie werden gesammelt und am
/// Ende summiert. Snapshot-Fehler sind non-fatal — das Update ist ja schon
/// drin, nur die Snapshot-Referenz fuer den spaeteren Citrix-Katalog-Publish
/// fehlt fuer diese VM.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BatchRunner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Snapshot-Namenspraefix — nur Snapshots mit diesem Praefix werden
    /// von der Retention angefasst; manuell angelegte Snapshots bleiben unberuehrt.</summary>
    public const string SnapshotPrefix = "checkmk-update-";

    /// <summary>Anzahl der zu behaltenden <c>checkmk-update-*</c>-Snapshots je VM.
    /// 3 ist ein pragmatischer Kompromiss: aktueller Snapshot + zwei Rueckfall-
    /// punkte, ohne den vSphere-Datastore unnoetig zu belegen.</summary>
    public const int SnapshotRetentionCount = 3;

    private readonly VSphereClient _vsphere;
    private readonly IAgentUpdater _updater;
    private readonly CitrixClient? _citrix;

    public BatchRunner(VSphereClient vsphere, IAgentUpdater updater, CitrixClient? citrix = null)
    {
        _vsphere = vsphere;
        _updater = updater;
        _citrix = citrix;
    }

    public async Task<IReadOnlyList<BatchStepResult>> RunAsync(
        IReadOnlyList<VmCatalogAssignment> assignments,
        BatchOptions options,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var results = new List<BatchStepResult>();
        for (var i = 0; i < assignments.Count; i++)
        {
            // Cancel-Check zwischen den VMs: statt Exception zu werfen brechen
            // wir die Schleife ab und geben die bisher gesammelten Results
            // zurueck. Damit kann der Aufrufer auch nach Cancel den Bericht
            // fuer die schon erledigten VMs zeigen.
            if (ct.IsCancellationRequested)
            {
                progress.Report("Batch abgebrochen — verbleibende VMs uebersprungen.");
                break;
            }
            var assign = assignments[i];
            var vm = assign.Vm;
            progress.Report($"[{i + 1}/{assignments.Count}] {vm.Name} — starte Ablauf");

            try
            {
                var (snapshotName, snapshotId) = await UpdateOneAsync(vm, options, progress, ct);

                // Citrix-Katalog-Publish (Roadmap 1c): nur wenn eine Zuordnung gesetzt
                // ist UND wir vorher einen Snapshot bekommen haben UND der CitrixClient
                // verfuegbar ist. Publish-Fehler sind NON-FATAL — Update + Snapshot sind
                // schon durch, der User kann im DDC manuell nachziehen.
                string? publishedCatalog = null;
                if (assign.Catalog is { } cat && snapshotName is not null && _citrix is not null)
                {
                    try
                    {
                        progress.Report($"  → Citrix-Katalog-Publish: {cat.Name}");
                        await _citrix.PublishMasterImageAsync(cat, vm.Name, snapshotName, ct);
                        publishedCatalog = cat.Name;
                        progress.Report($"  → Publish OK: {cat.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Citrix-Publish fuer Katalog {Cat} (VM {Vm}) fehlgeschlagen.",
                            cat.Name, vm.Name);
                        progress.Report($"  → WARN: Publish {cat.Name} fehlgeschlagen: {ex.Message}");
                    }
                }

                results.Add(new BatchStepResult(vm.Name, true, "OK", snapshotName, snapshotId, publishedCatalog));
                progress.Report($"[{i + 1}/{assignments.Count}] {vm.Name} — abgeschlossen ✔");
            }
            catch (OperationCanceledException)
            {
                // Cancel innerhalb einer VM: die aktuelle VM als "abgebrochen"
                // markieren, verbleibende VMs skipt die naechste Iteration.
                results.Add(new BatchStepResult(vm.Name, false, "Abgebrochen"));
                progress.Report($"[{i + 1}/{assignments.Count}] {vm.Name} — abgebrochen.");
                break;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Batch-Update {Vm} fehlgeschlagen.", vm.Name);
                results.Add(new BatchStepResult(vm.Name, false, ex.Message));
                progress.Report($"[{i + 1}/{assignments.Count}] {vm.Name} — FEHLER: {ex.Message}");
            }
        }
        return results;
    }

    private async Task<(string? snapshotName, string? snapshotId)> UpdateOneAsync(
        VmInfo vm, BatchOptions options, IProgress<string> progress, CancellationToken ct)
    {
        // 0) Power-State-vor-Update erfassen, damit wir ihn am Ende wiederherstellen.
        //    Fuer CTX???00-Baseimages ist der Normalfall "vorher aus, hinterher aus".
        var current = await _vsphere.GetVmAsync(vm.Id, ct);
        var wasOn = current.IsPoweredOn;
        progress.Report($"  → Ausgangs-Power-State: {(wasOn ? "POWERED_ON" : "POWERED_OFF")}");

        // 1) Power on (falls noch aus).
        if (!wasOn)
        {
            progress.Report("  → PowerOn");
            await _vsphere.PowerOnAsync(vm.Id, ct);
        }
        else
        {
            progress.Report("  → bereits an — kein extra PowerOn");
        }

        // 2) Warten bis VMware-Tools RUNNING (bis PowerOnTimeout).
        progress.Report("  → warte auf VMware-Tools");
        var toolsUp = await WaitAsync(
            async () =>
            {
                var t = await _vsphere.GetToolsInfoAsync(vm.Id, ct);
                return t?.IsRunning == true;
            },
            options.ToolsPollInterval, options.PowerOnTimeout, ct);
        if (!toolsUp) throw new InvalidOperationException("VMware-Tools starteten nicht innerhalb Timeout.");

        // 3) Hostname vom Guest holen (fuer Ping + Agent-Update).
        var guest = await _vsphere.GetGuestIdentityAsync(vm.Id, ct);
        var hostName = guest?.HostName;
        if (string.IsNullOrWhiteSpace(hostName))
        {
            // Fallback: VM-Name als Hostname verwenden (haeufig identisch im Fachbereich).
            hostName = vm.Name;
            progress.Report($"  → Guest-Hostname nicht verfuegbar, nutze VM-Name '{hostName}'");
        }
        else
        {
            progress.Report($"  → Guest-Hostname: {hostName}");
        }

        // 4) Warten bis per Ping erreichbar.
        progress.Report("  → warte auf Ping-Reachability");
        var reachable = await WaitAsync(
            () => Task.FromResult(TryPing(hostName!)),
            TimeSpan.FromSeconds(3), options.PowerOnTimeout, ct);
        if (!reachable) throw new InvalidOperationException($"Host {hostName} nicht per Ping erreichbar.");

        // 5) Agent-Update ueber Plugin 1 (IAgentUpdater).
        progress.Report("  → Agent-Update (via IAgentUpdater)");
        var inner = new Progress<string>(line => progress.Report("     " + line));
        var updateResult = await _updater.UpdateAsync(
            hostName!, options.AdminUser, options.AdminPassword,
            options.AgentShare, options.ScriptTemplate, inner, ct);
        if (!updateResult.Success)
            throw new InvalidOperationException("Agent-Update fehlgeschlagen — siehe Log.");

        // 6) Guest-Shutdown fuer den Snapshot-Zeitpunkt (auch wenn die VM
        //    vorher AN war — Snapshot einer ausgeschalteten VM ist sauber und
        //    schnell, kein Memory-State-Konsistenz-Risiko).
        progress.Report("  → Guest-Shutdown (Snapshot-Vorbereitung)");
        await _vsphere.GuestShutdownAsync(vm.Id, ct);

        // 7) Warten bis POWERED_OFF.
        progress.Report("  → warte auf Power-Off");
        var offReached = await WaitAsync(
            async () =>
            {
                var s = await _vsphere.GetVmAsync(vm.Id, ct);
                return !s.IsPoweredOn;
            },
            TimeSpan.FromSeconds(5), options.ShutdownTimeout, ct);
        if (!offReached) throw new InvalidOperationException("VM ging nicht innerhalb Timeout in Power-Off.");

        // 8) Snapshot anlegen — dient spaeter als Master-Image fuer den
        //    Citrix-Katalog-Publish. Fehler NICHT fatal: der Agent ist ja
        //    schon drin. Ohne Snapshot fehlt nur die Referenz fuer den
        //    naechsten Roadmap-1c-Schritt.
        var snapshotName = $"{SnapshotPrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
        progress.Report($"  → Snapshot anlegen: {snapshotName}");
        string? snapshotId = null;
        try
        {
            snapshotId = await _vsphere.CreateSnapshotAsync(
                vm.Id, snapshotName,
                "Angelegt vom Checkmk Cockpit (vSphere-Baseimage-Plugin) nach Agent-Update.",
                ct);
            progress.Report($"  → Snapshot-ID: {snapshotId ?? "(unbekannt)"}");

            // 8b) Retention: alte checkmk-update-*-Snapshots derselben VM auf
            //     SnapshotRetentionCount zurueckschneiden. Fehler NICHT fatal
            //     — nur Aufraeum-Kosmetik.
            await ApplySnapshotRetentionAsync(vm, progress, ct);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Snapshot-Anlage fuer {Vm} fehlgeschlagen.", vm.Name);
            progress.Report($"  → WARN: Snapshot fehlgeschlagen: {ex.Message}");
            snapshotName = null;
        }

        // 9) Power-State-Restore: wenn die VM VOR dem Update AN war, wieder anschalten.
        //    Sonst bleibt sie aus (CTX???00-Baseimage-Normalfall).
        if (wasOn)
        {
            progress.Report("  → Restore: PowerOn (VM war vor dem Update AN)");
            await _vsphere.PowerOnAsync(vm.Id, ct);
        }
        else
        {
            progress.Report("  → bleibt aus (VM war vor dem Update AUS)");
        }

        return (snapshotName, snapshotId);
    }

    /// <summary>Loescht alte <c>checkmk-update-*</c>-Snapshots der VM, so dass nur
    /// noch <see cref="SnapshotRetentionCount"/> die neuesten uebrig bleiben.
    /// Sortierung: <c>create_time</c> falls vorhanden, sonst Name (unsere
    /// Konvention ist chronologisch). Manuelle Snapshots ohne unser Praefix
    /// werden nicht angefasst.</summary>
    private async Task ApplySnapshotRetentionAsync(
        VmInfo vm, IProgress<string> progress, CancellationToken ct)
    {
        List<VmSnapshotInfo> ours;
        try
        {
            var all = await _vsphere.ListSnapshotsAsync(vm.Id, ct);
            ours = all.Where(s => s.Name.StartsWith(SnapshotPrefix, StringComparison.Ordinal))
                      .OrderByDescending(s => s.CreateTime ?? DateTimeOffset.MinValue)
                      .ThenByDescending(s => s.Name, StringComparer.Ordinal)
                      .ToList();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Snapshot-Liste fuer Retention nicht abrufbar (VM {Vm}).", vm.Name);
            return;
        }

        if (ours.Count <= SnapshotRetentionCount) return;

        var toDelete = ours.Skip(SnapshotRetentionCount).ToList();
        progress.Report($"  → Retention: {toDelete.Count} alte Snapshot(s) loeschen (behalte {SnapshotRetentionCount}).");
        foreach (var snap in toDelete)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _vsphere.DeleteSnapshotAsync(vm.Id, snap.Id, ct);
                progress.Report($"     - geloescht: {snap.Name}");
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Snapshot-Delete fuer {Vm}/{Snap} fehlgeschlagen.", vm.Name, snap.Name);
                progress.Report($"     - WARN: {snap.Name} nicht geloescht ({ex.Message})");
            }
        }
    }

    private static async Task<bool> WaitAsync(
        Func<Task<bool>> probe, TimeSpan interval, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await probe()) return true; }
            catch { /* transient — weiter warten */ }
            await Task.Delay(interval, ct);
        }
        return false;
    }

    private static bool TryPing(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, 1500);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }
}
