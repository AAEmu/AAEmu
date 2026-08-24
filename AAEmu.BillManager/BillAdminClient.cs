using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AAEmu.BillManager;

public sealed class BillAdminClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpClient _http;

    public BillAdminClient(TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(120)
        };
    }

    public void Dispose() => _http.Dispose();

    public async Task<BillStatus?> GetStatusAsync(string adminBase, CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await _http.GetAsync(Normalize(adminBase) + "/status", cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                return null;

            return new BillStatus(
                root.TryGetProperty("productCount", out var pc) ? pc.GetInt32() : 0,
                root.TryGetProperty("availableCount", out var ac) ? ac.GetInt32() : 0,
                root.TryGetProperty("busy", out var busy) && busy.GetBoolean(),
                root.TryGetProperty("shuttingDown", out var sd) && sd.GetBoolean());
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Ok, string Body, int StatusCode)> PutProductAsync(
        string adminBase,
        uint shopId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PutAsync($"{Normalize(adminBase)}/catalog/{shopId}", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    public async Task<(bool Ok, string Body, int StatusCode)> BulkSaveAsync(
        string adminBase,
        IReadOnlyList<object> products,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(products, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{Normalize(adminBase)}/catalog/bulk", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    public async Task<(bool Ok, string Body, int StatusCode)> PublishAsync(
        string adminBase,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{Normalize(adminBase)}/catalog/publish", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    public async Task<(bool Ok, string Body, int StatusCode)> FillNamesAsync(
        string adminBase,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{Normalize(adminBase)}/catalog/fill-names", content, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    public async Task<string> GetCatalogJsonAsync(string adminBase, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.GetAsync(Normalize(adminBase) + "/catalog", cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<bool> RequestShutdownAsync(string adminBase, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{Normalize(adminBase)}/admin/shutdown", content, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task WaitForOfflineAsync(string adminBase, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GetStatusAsync(adminBase, cancellationToken) is null)
                return;
            await Task.Delay(250, cancellationToken);
        }
    }

    private static string Normalize(string adminBase) => adminBase.Trim().TrimEnd('/');
}

public readonly record struct BillStatus(int ProductCount, int AvailableCount, bool Busy, bool ShuttingDown);
