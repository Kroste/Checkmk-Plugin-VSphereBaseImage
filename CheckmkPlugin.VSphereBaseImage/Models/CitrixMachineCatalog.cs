using System.Text.Json.Serialization;

namespace CheckmkPlugin.VSphereBaseImage.Models;

/// <summary>Kompaktes Machine-Catalog-Info aus dem DDC — reicht fuer die
/// Katalog-Auswahl im Pre-Batch-Dialog.</summary>
public sealed record CitrixMachineCatalog
{
    [JsonPropertyName("Id")] public string Id { get; init; } = "";
    [JsonPropertyName("Name")] public string Name { get; init; } = "";

    /// <summary>Aktueller Master-Image-Pfad im XenDesktop-Format:
    /// <c>XdHyp:\HostingUnits\{unit}\{vmName}.vm\{snapshot}.snapshot</c>.
    /// Aus diesem Pfad extrahieren wir die HostingUnit — der Neu-Pfad wird
    /// nur an den VM- und Snapshot-Teilen ersetzt.</summary>
    [JsonPropertyName("ProvisioningSchemeMasterImagePath")]
    public string? MasterImagePath { get; init; }
}

/// <summary>Wrapper fuer die odata-artige Response {"Items":[...]}.</summary>
public sealed record CitrixItems<T>
{
    [JsonPropertyName("Items")] public List<T> Items { get; init; } = new();
}

public sealed record CitrixSite
{
    [JsonPropertyName("Id")] public string Id { get; init; } = "";
    [JsonPropertyName("Name")] public string Name { get; init; } = "";
}

/// <summary>Ergebnis der Katalog-Zuordnung, wie sie im CatalogPickerDialog
/// vom User bestaetigt wurde. Wird an den BatchRunner uebergeben.</summary>
public sealed record VmCatalogAssignment(
    VmInfo Vm,
    CitrixMachineCatalog? Catalog);
