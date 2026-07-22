using System.Collections.ObjectModel;
using CheckmkPlugin.VSphereBaseImage.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckmkPlugin.VSphereBaseImage.ViewModels;

/// <summary>Zeile im CatalogPickerDialog: eine VM + ausgewaehlter Katalog.
/// SelectedCatalog auf null = kein Publish fuer diese VM (nur Update+Snapshot).</summary>
public sealed partial class VmCatalogRow : ObservableObject
{
    public VmInfo Vm { get; }
    public IReadOnlyList<CitrixMachineCatalog> AllCatalogs { get; }

    [ObservableProperty] private CitrixMachineCatalog? _selectedCatalog;

    public VmCatalogRow(VmInfo vm, IReadOnlyList<CitrixMachineCatalog> catalogs,
        CitrixMachineCatalog? suggestion)
    {
        Vm = vm;
        AllCatalogs = catalogs;
        _selectedCatalog = suggestion;
    }
}

public sealed partial class CatalogPickerViewModel : ObservableObject
{
    public ObservableCollection<VmCatalogRow> Rows { get; } = new();

    [ObservableProperty] private string _statusMessage = "";

    public CatalogPickerViewModel(
        IReadOnlyList<VmInfo> vms, IReadOnlyList<CitrixMachineCatalog> catalogs)
    {
        foreach (var vm in vms)
        {
            var suggestion = SuggestCatalog(vm, catalogs);
            Rows.Add(new VmCatalogRow(vm, catalogs, suggestion));
        }
        var withSuggestion = Rows.Count(r => r.SelectedCatalog is not null);
        StatusMessage = $"{Rows.Count} VM(s) — {withSuggestion} Vorschlaege automatisch gesetzt.";
    }

    public IReadOnlyList<VmCatalogAssignment> BuildAssignments()
        => Rows.Select(r => new VmCatalogAssignment(r.Vm, r.SelectedCatalog)).ToList();

    /// <summary>
    /// Vorschlag: der Katalog, dessen Name der laengste gemeinsame Praefix zum
    /// VM-Namen ist (case-insensitive). Beispiel: VM "CTX01100" → Katalog
    /// "CTX01" (5 Zeichen Praefix-Match) schlaegt "CTX" (3 Zeichen) und
    /// "OTHER" (0) klar. Kein Match → kein Vorschlag (SelectedCatalog=null).
    /// </summary>
    private static CitrixMachineCatalog? SuggestCatalog(
        VmInfo vm, IReadOnlyList<CitrixMachineCatalog> catalogs)
    {
        var vmName = vm.Name;
        CitrixMachineCatalog? best = null;
        var bestLen = 0;
        foreach (var c in catalogs)
        {
            var name = c.Name;
            var len = CommonPrefixLength(vmName, name);
            if (len > bestLen)
            {
                bestLen = len;
                best = c;
            }
        }
        // Mindestens 3 Zeichen Praefix-Match, damit "CTX" nicht zufaellig
        // "C" mit "Common-Catalog" verbindet.
        return bestLen >= 3 ? best : null;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[i]))
            i++;
        return i;
    }
}
