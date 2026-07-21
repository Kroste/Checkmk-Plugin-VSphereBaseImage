using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Checkmk.PluginContracts.Services;
using CheckmkPlugin.VSphereBaseImage.Models;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

public sealed record BatchStepResult(string VmName, bool Success, string Message);

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
///  PowerOn -> warten bis VMware-Tools laufen -> warten bis der Guest per Ping
///  erreichbar ist -> Agent-Update via <see cref="IAgentUpdater"/> aus Plugin 1
///  -> Guest-Shutdown -> warten bis Power-Off -> naechste VM.
///
/// Fehler bei einer VM brechen die Batch NICHT ab; sie werden gesammelt und am
/// Ende summiert.
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
                await UpdateOneAsync(vm, options, progress, ct);
                results.Add(new BatchStepResult(vm.Name, true, "OK"));
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

    private async Task UpdateOneAsync(
        VmInfo vm, BatchOptions options, IProgress<string> progress, CancellationToken ct)
    {
        // 1) Power on (falls noch aus).
        var current = await _vsphere.GetVmAsync(vm.Id, ct);
        if (!current.IsPoweredOn)
        {
            progress.Report($"  → PowerOn");
            await _vsphere.PowerOnAsync(vm.Id, ct);
        }
        else
        {
            progress.Report($"  → bereits an");
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

        // 6) Guest-Shutdown.
        progress.Report("  → Guest-Shutdown");
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
