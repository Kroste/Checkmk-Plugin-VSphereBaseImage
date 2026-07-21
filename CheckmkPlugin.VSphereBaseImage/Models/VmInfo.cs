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
