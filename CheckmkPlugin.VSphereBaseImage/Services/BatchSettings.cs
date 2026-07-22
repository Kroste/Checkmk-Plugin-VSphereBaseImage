using System.Text.Json;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>Batch-Konfiguration: Agent-Share + Skript-Vorlage, die beim Agent-
/// Update auf jeder VM verwendet werden. Bewusst dupliziert vom AgentUpdater-
/// Plugin — Plugin 2 soll unabhaengig von den internen Types von Plugin 1
/// bleiben (Plugin-Isolation).</summary>
public sealed class BatchSettings
{
    public string AgentShare { get; set; } = @"\\samba01\542$\5424_IT-Basis-Dienste\CheckMK";
    public string AgentUpdateScript { get; set; } = DefaultAgentUpdateScript;

    /// <summary>Zeit, in der die VM nach PowerOn/Restart per VMware-Tools und
    /// per Ping erreichbar sein muss. Default 600 s (10 min) — reicht selbst
    /// fuer Windows-Baseimages mit Domain-Join und Pending-Updates beim Boot.</summary>
    public int PowerOnTimeoutSeconds { get; set; } = 600;

    /// <summary>Zeit fuer den Guest-Shutdown, bevor der Snapshot angelegt
    /// wird. Default 300 s (5 min) — Windows braucht mit Pending-Login-
    /// Skripten manchmal drei bis vier Minuten.</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 300;

    /// <summary>Poll-Intervall fuer VMware-Tools- und Power-Off-Warteschritte.
    /// Default 5 s — Kompromiss aus Reaktionszeit und vCenter-Last.</summary>
    public int ToolsPollIntervalSeconds { get; set; } = 5;

    public const string DefaultAgentUpdateScript =
        "# Laeuft auf dem Zielhost. {host}=Hostname, {installer}=lokaler MSI-Pfad.\n" +
        "$ErrorActionPreference = 'Stop'\n" +
        "\n" +
        "$keys = 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'," +
        "'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'\n" +
        "Get-ItemProperty $keys -ErrorAction SilentlyContinue |\n" +
        "  Where-Object { $_.DisplayName -like 'Checkmk Agent*' } |\n" +
        "  ForEach-Object {\n" +
        "    Write-Output \"Deinstalliere $($_.DisplayName)\"\n" +
        "    $p = Start-Process msiexec.exe -ArgumentList \"/x $($_.PSChildName) /qn /norestart\" -Wait -PassThru\n" +
        "    if ($p.ExitCode -ne 0) { throw \"msiexec /x fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "  }\n" +
        "\n" +
        "Write-Output 'Installiere neuen Agent'\n" +
        "$p = Start-Process msiexec.exe -ArgumentList \"/i `\"{installer}`\" /qn /norestart\" -Wait -PassThru\n" +
        "if ($p.ExitCode -ne 0) { throw \"msiexec /i fehlgeschlagen (ExitCode $($p.ExitCode))\" }\n" +
        "\n" +
        "Write-Output 'Registriere Agent-Controller'\n" +
        "& \"C:\\Program Files (x86)\\checkmk\\service\\cmk-agent-ctl.exe\" register --trust-cert " +
        "-H {host} -s cmk.lhp.intern -i LHP -U Agent_cmk -P ************\n" +
        "Write-Output 'Fertig.'\n";
}

public interface IBatchSettingsStore
{
    BatchSettings Load();
    void Save(BatchSettings settings);
    string FilePath { get; }
}

public sealed class BatchSettingsStore : IBatchSettingsStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public string FilePath => _path;

    public BatchSettingsStore(string pluginDataDirectory)
    {
        Directory.CreateDirectory(pluginDataDirectory);
        _path = Path.Combine(pluginDataDirectory, "batch-settings.json");
    }

    public BatchSettings Load()
    {
        if (!File.Exists(_path)) return new BatchSettings();
        try
        {
            return JsonSerializer.Deserialize<BatchSettings>(File.ReadAllText(_path))
                   ?? new BatchSettings();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Batch-Settings konnten nicht geladen werden.");
            return new BatchSettings();
        }
    }

    public void Save(BatchSettings settings)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Batch-Settings konnten nicht gespeichert werden: {Path}", _path);
        }
    }
}
