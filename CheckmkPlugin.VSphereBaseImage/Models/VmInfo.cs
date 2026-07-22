using System.Text.Json.Serialization;

namespace CheckmkPlugin.VSphereBaseImage.Models;

/// <summary>Kompaktes VM-Info aus dem vCenter — reicht fuer VM-Grid + Filter.</summary>
public sealed record VmInfo
{
    [JsonPropertyName("vm")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("power_state")]
    public string PowerState { get; init; } = "";

    [JsonPropertyName("cpu_count")]
    public int CpuCount { get; init; }

    [JsonPropertyName("memory_size_MiB")]
    public int MemoryMiB { get; init; }

    [JsonIgnore]
    public bool IsPoweredOn => PowerState == "POWERED_ON";
}

/// <summary>Guest-Info (fuer den Hostname bei Ping/Agent-Update). Wird nur bei
/// laufender VM abgefragt und kann bei ausgeschalteter VM leer sein.</summary>
public sealed record VmGuestIdentity
{
    [JsonPropertyName("host_name")]
    public string? HostName { get; init; }

    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; init; }

    [JsonPropertyName("full_name")]
    public LocalizableMessage? FullName { get; init; }
}

public sealed record LocalizableMessage
{
    [JsonPropertyName("default_message")]
    public string? DefaultMessage { get; init; }
}

/// <summary>VMware-Tools-Status. "RUNNING" heisst der Guest ist bereit fuer
/// weitere Aktionen (WinRM-Verbindung etc.).</summary>
public sealed record VmToolsInfo
{
    [JsonPropertyName("run_state")]
    public string? RunState { get; init; }

    [JsonIgnore]
    public bool IsRunning => RunState == "RUNNING";
}

/// <summary>Snapshot-Info aus <c>GET /api/vcenter/vm/{vm}/snapshots</c>. Die
/// vCenter-API v8 liefert je Item mindestens <c>snapshot</c> (ID) und
/// <c>name</c>; <c>create_time</c> ist ein optionales ISO-8601-Feld, das wir
/// fuer die Retention-Sortierung nutzen (Fallback: Sortierung nach Name, weil
/// unsere Namenskonvention <c>checkmk-update-YYYYMMDD-HHmmss</c> chronologisch
/// ist).</summary>
public sealed record VmSnapshotInfo
{
    [JsonPropertyName("snapshot")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset? CreateTime { get; init; }
}
