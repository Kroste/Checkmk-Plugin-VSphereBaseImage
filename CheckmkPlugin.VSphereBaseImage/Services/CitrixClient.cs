using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CheckmkPlugin.VSphereBaseImage.Models;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>
/// REST-Client fuer die Citrix-CVAD-On-Prem-Orchestration-API. Auth-Weg:
/// <c>POST /tokens</c> mit Basic-Auth (Domaenen-User) → Bearer-Token,
/// alle weiteren Requests mit dem Token im <c>Authorization: Bearer …</c>-Header.
/// Bei 401 wird das Token einmal frisch geholt und der Request wiederholt.
/// </summary>
public sealed class CitrixClient : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly DdcCredentials _creds;
    private readonly string _password;
    private readonly HttpClient _http;
    private string? _bearer;
    private string? _cachedSiteId;

    public CitrixClient(DdcCredentials creds, string plainPassword)
    {
        _creds = creds;
        _password = plainPassword;
        var handler = new HttpClientHandler();
        if (creds.IgnoreCertificateErrors)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        var baseUrl = creds.DdcUrl.TrimEnd('/') + "/citrix/orchestration/api/";
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_bearer)) return;

        using var req = new HttpRequestMessage(HttpMethod.Post, "tokens");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_creds.UserName}:{_password}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new CitrixApiException(
                $"Citrix-Token konnte nicht geholt werden ({(int)resp.StatusCode}): {body}",
                resp.StatusCode);

        // Response ist meist { "Token": "..." } oder direkt der Token-String.
        var token = body.Trim().Trim('"');
        if (token.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("Token", out var t))
                    token = t.GetString() ?? "";
            }
            catch { /* bleib beim getrimmten body */ }
        }
        _bearer = token;
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        Log.Debug("Citrix-Bearer-Token etabliert bei {Url}", _http.BaseAddress);
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        var resp = await _http.GetAsync(path, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _bearer = null;
            await EnsureTokenAsync(ct);
            resp = await _http.GetAsync(path, ct);
        }
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new CitrixApiException($"GET {path} → {(int)resp.StatusCode}: {body}", resp.StatusCode);
        return JsonSerializer.Deserialize<T>(body, JsonOpts)
               ?? throw new CitrixApiException($"GET {path}: leere Antwort.", resp.StatusCode);
    }

    /// <summary>Site-ID holen; erste verfuegbare Site gewinnt. Cacht das Ergebnis.</summary>
    public async Task<string> GetSiteIdAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_cachedSiteId)) return _cachedSiteId;
        var sites = await GetJsonAsync<CitrixItems<CitrixSite>>("Sites", ct);
        if (sites.Items.Count == 0)
            throw new CitrixApiException("DDC lieferte keine Sites — Site-Konfiguration pruefen.",
                System.Net.HttpStatusCode.OK);
        _cachedSiteId = sites.Items[0].Id;
        Log.Debug("Citrix-Site-ID: {Sid} ({Name})", _cachedSiteId, sites.Items[0].Name);
        return _cachedSiteId;
    }

    public async Task<IReadOnlyList<CitrixMachineCatalog>> ListMachineCatalogsAsync(
        CancellationToken ct = default)
    {
        var sid = await GetSiteIdAsync(ct);
        var res = await GetJsonAsync<CitrixItems<CitrixMachineCatalog>>(
            $"Sites/{sid}/MachineCatalogs", ct);
        return res.Items.OrderBy(c => c.Name).ToList();
    }

    /// <summary>Startet das Master-Image-Update fuer einen Machine Catalog.
    /// Der neue Pfad wird aus dem aktuellen MasterImagePath des Katalogs
    /// abgeleitet: die HostingUnit bleibt, VM- und Snapshot-Anteile werden
    /// ersetzt. Damit muessen wir die Hosting-Unit nicht separat konfigurieren.</summary>
    public async Task PublishMasterImageAsync(
        CitrixMachineCatalog catalog, string newVmName, string newSnapshotName,
        CancellationToken ct = default)
    {
        var sid = await GetSiteIdAsync(ct);

        var newPath = BuildNewMasterImagePath(catalog.MasterImagePath, newVmName, newSnapshotName);
        var payload = JsonSerializer.Serialize(new { MasterImage = newPath }, JsonOpts);

        async Task<HttpResponseMessage> Send()
        {
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"Sites/{sid}/MachineCatalogs/{catalog.Id}/$UpdateProvisioningScheme")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return await _http.SendAsync(req, ct);
        }

        await EnsureTokenAsync(ct);
        var resp = await Send();
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _bearer = null;
            await EnsureTokenAsync(ct);
            resp = await Send();
        }
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new CitrixApiException(
                $"POST $UpdateProvisioningScheme → {(int)resp.StatusCode}: {body}",
                resp.StatusCode);
        Log.Info("Citrix-Katalog {Name} auf Master-Image {Path} umgestellt.",
            catalog.Name, newPath);
    }

    /// <summary>Aus <c>XdHyp:\HostingUnits\{unit}\{oldVm}.vm\{oldSnap}.snapshot</c>
    /// wird <c>XdHyp:\HostingUnits\{unit}\{newVm}.vm\{newSnap}.snapshot</c>.
    /// HostingUnit bleibt automatisch korrekt.</summary>
    internal static string BuildNewMasterImagePath(
        string? currentPath, string newVmName, string newSnapshotName)
    {
        // HostingUnit aus dem aktuellen Pfad ziehen. Groups: 1 = unit.
        var m = Regex.Match(currentPath ?? "",
            @"XdHyp:\\HostingUnits\\([^\\]+)\\", RegexOptions.IgnoreCase);
        if (!m.Success)
            throw new CitrixApiException(
                $"Aktueller MasterImage-Pfad '{currentPath}' ist keine erwartete XdHyp-Struktur — " +
                "manuell im DDC pruefen.",
                System.Net.HttpStatusCode.OK);
        var unit = m.Groups[1].Value;
        return $"XdHyp:\\HostingUnits\\{unit}\\{newVmName}.vm\\{newSnapshotName}.snapshot";
    }

    public void Dispose() => _http.Dispose();
}

public sealed class CitrixApiException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public CitrixApiException(string message, System.Net.HttpStatusCode code) : base(message)
        => StatusCode = code;
}
