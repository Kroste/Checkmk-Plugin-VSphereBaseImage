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
    string? SnapshotId = null);

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

    private readonly VSphereClient _vsphere;
    private readonly IAgentUpdater _updater;

    public BatchRunner(VSphereClient vsphere, IAgentUpdater updater)
    {
        _vsphere = vsphere;
        _updater = updater;
    }

    public async Task<IReadOnlyList<BatchStepResult>> RunAsync(
        IReadOnlyList<VmInfo> vms,
        BatchOptions options,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var results = new List<BatchStepResult>();
        for (var i = 0; i < vms.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var vm = vms[i];
            progress.Report($"[{i + 1}/{vms.Count}] {vm.Name} — starte Ablauf");

            try
            {
                var (snapshotName, snapshotId) = await UpdateOneAsync(vm, options, progress, ct);
                results.Add(new BatchStepResult(vm.Name, true, "OK", snapshotName, snapshotId));
                progress.Report($"[{i + 1}/{vms.Count}] {vm.Name} — abgeschlossen ✔");
            }
            catch (OperationCanceledException)
            {
                progress.Report("Batch abgebrochen.");
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Batch-Update {Vm} fehlgeschlagen.", vm.Name);
                results.Add(new BatchStepResult(vm.Name, false, ex.Message));
                progress.Report($"[{i + 1}/{vms.Count}] {vm.Name} — FEHLER: {ex.Message}");
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
        var snapshotName = $"checkmk-update-{DateTime.Now:yyyyMMdd-HHmmss}";
        progress.Report($"  → Snapshot anlegen: {snapshotName}");
        string? snapshotId = null;
        try
        {
            snapshotId = await _vsphere.CreateSnapshotAsync(
                vm.Id, snapshotName,
                "Angelegt vom Checkmk Cockpit (vSphere-Baseimage-Plugin) nach Agent-Update.",
                ct);
            progress.Report($"  → Snapshot-ID: {snapshotId ?? "(unbekannt)"}");
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
