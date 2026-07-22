using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>vCenter-Anmeldedaten. Passwort ist DPAPI-verschluesselt (CurrentUser).</summary>
public sealed class VSphereCredentials
{
    public string VCenterUrl { get; set; } = "https://vc.lhp.intern";
    public string UserName { get; set; } = "";
    public string? ProtectedPassword { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
}

/// <summary>DDC-Anmeldedaten (Citrix Delivery Controller, On-Prem CVAD).
/// Passwort ist DPAPI-verschluesselt (CurrentUser).</summary>
public sealed class DdcCredentials
{
    public string DdcUrl { get; set; } = "https://ddc.lhp.intern";
    public string UserName { get; set; } = "";
    public string? ProtectedPassword { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
}

public interface ICredentialStore
{
    VSphereCredentials Load();
    void Save(VSphereCredentials creds, string? plainPassword);
    string? DecryptPassword(VSphereCredentials creds);
    string FilePath { get; }
}

public interface IDdcCredentialStore
{
    DdcCredentials Load();
    void Save(DdcCredentials creds, string? plainPassword);
    string? DecryptPassword(DdcCredentials creds);
    string FilePath { get; }
}

/// <summary>
/// Speichert vCenter-Credentials user-lokal (DPAPI, CurrentUser-Scope) im
/// Plugin-Datenverzeichnis. Wichtig: <b>Nichts vorbelegen mit</b>
/// <c>Environment.UserName</c> — im Fachbereich weicht der vCenter-User vom
/// Windows-Login ab (siehe Projekt-Memo).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public string FilePath => _path;

    public DpapiCredentialStore(string pluginDataDirectory)
    {
        Directory.CreateDirectory(pluginDataDirectory);
        _path = Path.Combine(pluginDataDirectory, "credentials.json");
    }

    public VSphereCredentials Load()
    {
        if (!File.Exists(_path)) return new VSphereCredentials();
        try
        {
            return JsonSerializer.Deserialize<VSphereCredentials>(File.ReadAllText(_path))
                   ?? new VSphereCredentials();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "vSphere-Credentials konnten nicht geladen werden — Defaults.");
            return new VSphereCredentials();
        }
    }

    public void Save(VSphereCredentials creds, string? plainPassword)
    {
        if (!string.IsNullOrEmpty(plainPassword))
        {
            var blob = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainPassword), null, DataProtectionScope.CurrentUser);
            creds.ProtectedPassword = Convert.ToBase64String(blob);
        }
        else if (plainPassword is null)
        {
            // null = "nicht anfassen" (bestehendes Passwort behalten). Nur bei
            // explizit leerem String wird geloescht.
        }
        else
        {
            creds.ProtectedPassword = null;
        }

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(creds, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "vSphere-Credentials konnten nicht gespeichert werden.");
        }
    }

    public string? DecryptPassword(VSphereCredentials creds)
    {
        if (string.IsNullOrEmpty(creds.ProtectedPassword)) return null;
        try
        {
            var blob = Convert.FromBase64String(creds.ProtectedPassword);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "vSphere-Passwort konnte nicht entschluesselt werden (falscher User/Rechner?).");
            return null;
        }
    }
}

/// <summary>
/// Speichert DDC-Credentials user-lokal (DPAPI-CurrentUser). Getrennte Datei
/// vom vSphere-Store, damit die zwei Systeme klar getrennt sind — und der
/// AD-Account fuers DDC weicht typischerweise vom vCenter-User ab (weder Windows-
/// Login noch vCenter-User vorbelegen).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDdcCredentialStore : IDdcCredentialStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public string FilePath => _path;

    public DpapiDdcCredentialStore(string pluginDataDirectory)
    {
        Directory.CreateDirectory(pluginDataDirectory);
        _path = Path.Combine(pluginDataDirectory, "ddc-credentials.json");
    }

    public DdcCredentials Load()
    {
        if (!File.Exists(_path)) return new DdcCredentials();
        try
        {
            return JsonSerializer.Deserialize<DdcCredentials>(File.ReadAllText(_path))
                   ?? new DdcCredentials();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "DDC-Credentials konnten nicht geladen werden — Defaults.");
            return new DdcCredentials();
        }
    }

    public void Save(DdcCredentials creds, string? plainPassword)
    {
        if (!string.IsNullOrEmpty(plainPassword))
        {
            var blob = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainPassword), null, DataProtectionScope.CurrentUser);
            creds.ProtectedPassword = Convert.ToBase64String(blob);
        }
        else if (plainPassword is null)
        {
            // null = "nicht anfassen" (bestehendes Passwort behalten).
        }
        else
        {
            creds.ProtectedPassword = null;
        }

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(creds, JsonOpts));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "DDC-Credentials konnten nicht gespeichert werden.");
        }
    }

    public string? DecryptPassword(DdcCredentials creds)
    {
        if (string.IsNullOrEmpty(creds.ProtectedPassword)) return null;
        try
        {
            var blob = Convert.FromBase64String(creds.ProtectedPassword);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "DDC-Passwort konnte nicht entschluesselt werden (falscher User/Rechner?).");
            return null;
        }
    }
}
