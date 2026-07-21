using System.Text.RegularExpressions;

namespace CheckmkPlugin.VSphereBaseImage.Models;

/// <summary>Persistierbarer VM-Filter, analog zum Checkmk-Host-Filter:
/// Regex ODER Include-Liste. VMs sind keine Hosts — daher ein eigener Typ
/// statt die Cockpit-HostFilterCollection zu recyceln.</summary>
public sealed class VmFilter
{
    // Timeout gegen catastrophic backtracking bei bloed geschriebenen Regexes.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public string Name { get; set; } = "";
    public string? NameRegex { get; set; }
    public List<string> IncludeNames { get; set; } = new();

    public bool Matches(string vmName)
    {
        if (IncludeNames.Count > 0)
            return IncludeNames.Any(n => string.Equals(n, vmName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(NameRegex))
        {
            try
            {
                return Regex.IsMatch(vmName, NameRegex, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }
        return true;
    }

    public override string ToString() => Name;
}

public sealed class VmFilterState
{
    public List<VmFilter> Filters { get; set; } = new();
    public string? ActiveFilterName { get; set; }
}
