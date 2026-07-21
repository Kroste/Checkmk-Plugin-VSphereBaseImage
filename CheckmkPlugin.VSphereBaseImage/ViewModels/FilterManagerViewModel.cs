using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CheckmkPlugin.VSphereBaseImage.Models;
using CheckmkPlugin.VSphereBaseImage.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckmkPlugin.VSphereBaseImage.ViewModels;

/// <summary>Dialog-VM zum Verwalten der VM-Filter (Anlegen/Bearbeiten/Loeschen).
/// Direkt vom Cockpit-<c>FilterManagerViewModel</c>-Muster uebernommen — nur
/// die Feldnamen sind auf VM-Terminologie umgestellt.</summary>
public sealed partial class FilterManagerViewModel : ObservableObject
{
    private readonly VmFilterCollection _collection;

    public ObservableCollection<VmFilter> Filters => _collection.Filters;

    [ObservableProperty]
    private VmFilter? _selected;

    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editRegex = "";
    [ObservableProperty] private string _editIncludeNames = "";

    /// <summary>Fehlermeldung fuer den Editor (v. a. Regex-Validierung).</summary>
    [ObservableProperty] private string _validationMessage = "";

    public FilterManagerViewModel(VmFilterCollection collection)
    {
        _collection = collection;
        _selected = _collection.Active ?? _collection.Filters.FirstOrDefault();
        LoadFromSelected();
    }

    partial void OnSelectedChanged(VmFilter? value) => LoadFromSelected();

    [RelayCommand]
    private void New()
    {
        var f = new VmFilter { Name = NextName() };
        _collection.Add(f);
        Selected = f;
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        var toRemove = Selected;
        var idx = Filters.IndexOf(toRemove);
        _collection.Remove(toRemove);
        Selected = Filters.Count == 0
            ? null
            : Filters[Math.Min(idx, Filters.Count - 1)];
    }

    [RelayCommand]
    private void Apply()
    {
        if (Selected is null) return;

        var regex = string.IsNullOrWhiteSpace(EditRegex) ? null : EditRegex.Trim();

        // Regex VOR dem Speichern validieren — ein kaputter Ausdruck wuerde sonst
        // bei jedem Refresh oder Batch-Start eine Exception werfen (persistent
        // in filter.json).
        if (regex is not null)
        {
            try
            {
                _ = new Regex(regex, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                ValidationMessage = $"Regex ungültig: {ex.Message}";
                return;
            }
        }
        ValidationMessage = "";

        // Referenz sichern (siehe Cockpit-Kommentar): das RemoveAt/Insert unten
        // leert die two-way-gebundene ListBox-Auswahl und schreibt Selected=null
        // zurueck — ohne diese Kopie wuerde ein null in die Filter-Liste eingefuegt.
        var item = Selected;

        item.Name = string.IsNullOrWhiteSpace(EditName) ? "unbenannt" : EditName.Trim();
        item.NameRegex = regex;
        item.IncludeNames = EditIncludeNames
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _collection.Update();

        // ObservableCollection benachrichtigt bei Property-Aenderungen auf Items
        // nicht — Re-Insert erzwingt Neu-Rendern des Listbox-Eintrags.
        var idx = Filters.IndexOf(item);
        if (idx >= 0)
        {
            Filters.RemoveAt(idx);
            Filters.Insert(idx, item);
            Selected = item;
        }
    }

    [RelayCommand]
    private void ActivateSelected() => _collection.Active = Selected;

    [RelayCommand]
    private void ClearActive() => _collection.Active = null;

    private void LoadFromSelected()
    {
        ValidationMessage = "";
        if (Selected is null)
        {
            EditName = "";
            EditRegex = "";
            EditIncludeNames = "";
            return;
        }
        EditName = Selected.Name;
        EditRegex = Selected.NameRegex ?? "";
        EditIncludeNames = string.Join(Environment.NewLine, Selected.IncludeNames);
    }

    private string NextName()
    {
        var i = 1;
        while (Filters.Any(f => f.Name == $"Filter {i}")) i++;
        return $"Filter {i}";
    }
}
