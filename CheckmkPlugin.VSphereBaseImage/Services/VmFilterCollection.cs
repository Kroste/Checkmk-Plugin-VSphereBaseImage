using System.Collections.ObjectModel;
using CheckmkPlugin.VSphereBaseImage.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>
/// Live-State fuer VM-Filter — Singleton, den der VSphereViewModel und der
/// FilterManagerWindow gemeinsam beobachten. Analog zum
/// <c>HostFilterCollection</c> aus dem Cockpit; Aenderungen an <see cref="Active"/>,
/// <see cref="Add"/>, <see cref="Remove"/>, <see cref="Update"/> persistieren
/// automatisch in den <see cref="IVmFilterStore"/>.
/// </summary>
public sealed class VmFilterCollection : ObservableObject
{
    private readonly IVmFilterStore _store;
    private bool _suppressPersist;

    public ObservableCollection<VmFilter> Filters { get; } = new();

    private VmFilter? _active;
    public VmFilter? Active
    {
        get => _active;
        set
        {
            // Beim Laden setzt die two-way-gebundene ComboBox waehrend Filters.Clear()
            // Active=null zurueck. Ohne diesen Guard wuerde der Setter dann Persist()
            // mit LEERER Filterliste ausloesen und die Datei ueberschreiben.
            if (SetProperty(ref _active, value) && !_suppressPersist)
                Persist();
        }
    }

    public VmFilterCollection(IVmFilterStore store)
    {
        _store = store;
        _suppressPersist = true;
        try
        {
            var s = _store.Load();
            foreach (var f in s.Filters)
                if (f is not null) Filters.Add(f);
            _active = string.IsNullOrEmpty(s.ActiveFilterName)
                ? null
                : Filters.FirstOrDefault(f => f.Name == s.ActiveFilterName);
        }
        finally { _suppressPersist = false; }
    }

    public void Add(VmFilter f)
    {
        Filters.Add(f);
        Persist();
    }

    public void Remove(VmFilter f)
    {
        Filters.Remove(f);
        if (ReferenceEquals(_active, f))
            Active = null;
        else
            Persist();
    }

    /// <summary>Nach externer Bearbeitung eines Filters aufrufen, um den Store
    /// zu aktualisieren.</summary>
    public void Update() => Persist();

    private void Persist()
        => _store.Save(new VmFilterState
        {
            Filters = Filters.ToList(),
            ActiveFilterName = _active?.Name
        });
}
