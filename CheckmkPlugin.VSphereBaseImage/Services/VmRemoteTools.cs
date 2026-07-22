using System.Diagnostics;
using System.Runtime.Versioning;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>
/// Startet Windows-Standard-Remote-Tools (mstsc, ping) fuer eine vSphere-VM.
/// Analog zum Cockpit-<c>RemoteTools</c>, aber ohne HostContext/FQDN-Aufloesung —
/// vSphere-Baseimages werden im Fachbereich per VM-Name (der der Windows-
/// Hostname ist) angesprochen. Braucht spaeter ein Domain-Suffix, kommt das
/// als Plugin-Setting.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VmRemoteTools
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void StartRdp(string vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:{vmName}",
                UseShellExecute = true
            });
            Log.Info("RDP-Verbindung geoeffnet: {Vm}", vmName);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "mstsc konnte nicht gestartet werden (VM={Vm}).", vmName);
        }
    }

    public void StartPing(string vmName)
    {
        if (string.IsNullOrWhiteSpace(vmName)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k ping -t {vmName}",
                UseShellExecute = true
            });
            Log.Info("Ping-Fenster geoeffnet: {Vm}", vmName);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "ping konnte nicht gestartet werden (VM={Vm}).", vmName);
        }
    }
}
