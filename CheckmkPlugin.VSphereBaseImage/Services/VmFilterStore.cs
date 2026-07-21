using System.Text.Json;
using CheckmkPlugin.VSphereBaseImage.Models;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

public interface IVmFilterStore
{
    VmFilterState Load();
    void Save(VmFilterState state);
    string FilePath { get; }
}

/// <summary>VM-Filter user-lokal, im Plugin-Datenverzeichnis.</summary>
public sealed class VmFilterStore : IVmFilterStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public string FilePath => _path;

    public VmFilterStore(string pluginDataDirectory)
    {
        Directory.CreateDirectory(pluginDataDirectory);
        _path = Path.Combine(pluginDataDirectory, "filter.json");
    }

    public VmFilterState Load()
    {
        if (!File.Exists(_path)) return new VmFilterState();
        try
        {
            return JsonSerializer.Deserialize<VmFilterState>(File.ReadAllText(_path))
                   ?? new VmFilterState();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "VM-Filter konnten nicht geladen werden.");
            return new VmFilterState();
        }
    }

    public void Save(VmFilterState state)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "VM-Filter konnten nicht gespeichert werden: {Path}", _path);
        }
    }
}
