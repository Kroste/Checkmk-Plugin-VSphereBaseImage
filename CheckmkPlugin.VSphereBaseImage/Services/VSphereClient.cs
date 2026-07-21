using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CheckmkPlugin.VSphereBaseImage.Models;
using NLog;

namespace CheckmkPlugin.VSphereBaseImage.Services;

/// <summary>
/// vCenter-REST-Client (API v8, kompatibel zu 7.0u2+).
/// Auth: POST /api/session mit Basic-Auth liefert einen Session-Token; alle
/// weiteren Requests schicken den Token als <c>vmware-api-session-id</c>-Header.
/// Bei 401 wird die Session einmal frisch geholt und der Request wiederholt.
///
/// Sicher gegen self-signed Zertifikate ueber
/// <see cref="VSphereCredentials.IgnoreCertificateErrors"/> (nur setzen, wenn's
/// wirklich sein muss — im Fachbereich ist das vCenter-Cert normalerweise vom
/// internen AD-CA und wird von Windows validiert).
/// </summary>
public sealed class VSphereClient : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly VSphereCredentials _creds;
    private readonly string _password;
    private readonly HttpClient _http;
    private string? _sessionToken;

    public VSphereClient(VSphereCredentials creds, string plainPassword)
    {
        _creds = creds;
        _password = plainPassword;
        var handler = new HttpClientHandler();
        if (creds.IgnoreCertificateErrors)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(NormalizeBaseUrl(creds.VCenterUrl)),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Trailing-Slash sicherstellen, damit relative Requests sauber laufen.</summary>
    private static string NormalizeBaseUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        return trimmed + "/";
    }

    /// <summary>Session holen (Basic-Auth → Token) und im Client-Header ablegen.
    /// Wird lazy beim ersten Request aufgerufen; bei 401 auch wiederholt.</summary>
    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_sessionToken)) return;

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/session");
        var basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_creds.UserName}:{_password}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new VSphereApiException(
                $"vSphere-Session konnte nicht erstellt werden ({(int)resp.StatusCode}): {body}",
                resp.StatusCode);

        // API v8 liefert den Token als reinen JSON-String zurueck ("abc123..."),
        // v7-Legacy verpackt ihn in { "value": "..." }.
        var token = body.Trim().Trim('"');
        if (token.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("value", out var v))
                    token = v.GetString() ?? "";
            }
            catch { /* bleib bei getrimmtem body */ }
        }
        _sessionToken = token;
        _http.DefaultRequestHeaders.Remove("vmware-api-session-id");
        _http.DefaultRequestHeaders.Add("vmware-api-session-id", token);
        Log.Debug("vSphere-Session etabliert bei {Url}", _http.BaseAddress);
    }

    private async Task<T> GetWithAuthAsync<T>(string path, CancellationToken ct)
    {
        await EnsureSessionAsync(ct);
        var resp = await _http.GetAsync(path, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _sessionToken = null;
            await EnsureSessionAsync(ct);
            resp = await _http.GetAsync(path, ct);
        }
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new VSphereApiException($"GET {path} -> {(int)resp.StatusCode}: {body}", resp.StatusCode);
        return JsonSerializer.Deserialize<T>(body, JsonOpts)
               ?? throw new VSphereApiException($"GET {path}: leere Antwort.", resp.StatusCode);
    }

    private async Task PostWithAuthAsync(string path, CancellationToken ct)
    {
        await EnsureSessionAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _sessionToken = null;
            await EnsureSessionAsync(ct);
            using var retry = new HttpRequestMessage(HttpMethod.Post, path);
            resp = await _http.SendAsync(retry, ct);
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new VSphereApiException($"POST {path} -> {(int)resp.StatusCode}: {body}", resp.StatusCode);
        }
    }

    public Task<List<VmInfo>> ListVmsAsync(CancellationToken ct = default)
        => GetWithAuthAsync<List<VmInfo>>("api/vcenter/vm", ct);

    public Task<VmGuestIdentity?> GetGuestIdentityAsync(string vmId, CancellationToken ct = default)
    {
        // Guest-Identity ist nur bei laufender VM verfuegbar; kein Fehler wenn 503.
        return TryGetAsync<VmGuestIdentity>($"api/vcenter/vm/{vmId}/guest/identity", ct);
    }

    public Task<VmToolsInfo?> GetToolsInfoAsync(string vmId, CancellationToken ct = default)
        => TryGetAsync<VmToolsInfo>($"api/vcenter/vm/{vmId}/tools", ct);

    public Task<VmInfo> GetVmAsync(string vmId, CancellationToken ct = default)
        => GetWithAuthAsync<VmInfo>($"api/vcenter/vm/{vmId}", ct);

    public Task PowerOnAsync(string vmId, CancellationToken ct = default)
        => PostWithAuthAsync($"api/vcenter/vm/{vmId}/power?action=start", ct);

    /// <summary>Sauberes Herunterfahren ueber den Guest (VMware-Tools). Bei
    /// ausgeschaltetem Guest oder fehlenden Tools schlaegt der Call fehl —
    /// dann muss der Caller entscheiden, ob er hart per <c>power?action=stop</c>
    /// abschaltet.</summary>
    public Task GuestShutdownAsync(string vmId, CancellationToken ct = default)
        => PostWithAuthAsync($"api/vcenter/vm/{vmId}/guest/power?action=shutdown", ct);

    private async Task<T?> TryGetAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            await EnsureSessionAsync(ct);
            var resp = await _http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(body, JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Optionaler GET {Path} fehlgeschlagen.", path);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class VSphereApiException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public VSphereApiException(string message, System.Net.HttpStatusCode code)
        : base(message) => StatusCode = code;
}
