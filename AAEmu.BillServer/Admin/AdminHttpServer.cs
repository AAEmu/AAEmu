using System.Net;
using System.Text;
using System.Text.Json;
using AAEmu.BillServer.Cash;
using NLog;

namespace AAEmu.BillServer.Admin;

/// <summary>
/// Local admin HTTP for BillManager and ops.
/// GET  /status
/// GET  /catalog
/// PUT  /catalog/{shopId}  JSON product
/// POST /catalog/bulk      JSON array of products
/// POST /cash/add          { accountId, amount, priceType }
/// POST /catalog/publish   push available rows → aaemu_game ics_*
/// POST /catalog/fill-names  resolve empty/Premium names from client compact.sqlite3
/// POST /admin/shutdown    graceful stop (no new catalog ops; drain listeners)
/// </summary>
public sealed class AdminHttpServer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly HttpListener _listener = new();
    private readonly ICashStore _cash;
    private readonly ICatalogStore _catalog;
    private readonly string? _icsSyncCs;
    private readonly BillCatalogOptions _catalogOptions;
    private readonly Func<string> _statusExtra;
    private readonly CatalogMutationGate _catalogGate;
    private readonly Action? _requestShutdown;
    private CancellationTokenSource? _cts;

    public AdminHttpServer(
        string prefix,
        ICashStore cash,
        ICatalogStore catalog,
        string? icsSyncCs,
        BillCatalogOptions catalogOptions,
        Func<string> statusExtra,
        CatalogMutationGate catalogGate,
        Action? requestShutdown = null)
    {
        _listener.Prefixes.Add(prefix.EndsWith('/') ? prefix : prefix + "/");
        _cash = cash;
        _catalog = catalog;
        _icsSyncCs = icsSyncCs;
        _catalogOptions = catalogOptions;
        _statusExtra = statusExtra;
        _catalogGate = catalogGate;
        _requestShutdown = requestShutdown;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Log.Info("admin HTTP listening {0}", string.Join(", ", _listener.Prefixes));
        _ = Loop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "admin accept");
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Handle(ctx);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "admin task fault");
                }
            }, ct);
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            var method = req.HttpMethod.ToUpperInvariant();

            if (method == "GET" && path is "/status" or "")
            {
                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    service = "AAEmu.BillServer",
                    detail = _statusExtra(),
                    productCount = _catalog.ListAll().Count,
                    availableCount = _catalog.ListAvailable().Count,
                    busy = _catalogGate.IsBusy,
                    shuttingDown = _catalogGate.IsShuttingDown
                });
                return;
            }

            if (method == "GET" && path == "/catalog")
            {
                await WriteJson(ctx, 200, _catalog.ListAll());
                return;
            }

            if (method == "POST" && path == "/admin/shutdown")
            {
                _requestShutdown?.Invoke();
                await WriteJson(ctx, 200, new { ok = true, message = "shutdown requested" });
                return;
            }

            if (method == "PUT" && path.StartsWith("/catalog/", StringComparison.Ordinal))
            {
                var lease = await _catalogGate.TryEnterAsync();
                if (lease is null)
                {
                    await WriteJson(ctx, _catalogGate.IsShuttingDown ? 503 : 409, new
                    {
                        error = _catalogGate.IsShuttingDown ? "server shutting down" : "catalog busy"
                    });
                    return;
                }

                using (lease)
                {
                var idPart = path["/catalog/".Length..];
                if (!uint.TryParse(idPart, out var shopId))
                {
                    await WriteJson(ctx, 400, new { error = "bad shopId" });
                    return;
                }

                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var json = await reader.ReadToEndAsync();
                var dto = JsonSerializer.Deserialize<ProductDto>(json, JsonOpts);
                if (dto is null)
                {
                    await WriteJson(ctx, 400, new { error = "bad json" });
                    return;
                }

                var product = MergeProduct(shopId, dto);
                _catalog.Upsert(product);
                await WriteJson(ctx, 200, product);
                }

                return;
            }

            if (method == "POST" && path == "/catalog/bulk")
            {
                var lease = await _catalogGate.TryEnterAsync();
                if (lease is null)
                {
                    await WriteJson(ctx, _catalogGate.IsShuttingDown ? 503 : 409, new
                    {
                        error = _catalogGate.IsShuttingDown ? "server shutting down" : "catalog busy"
                    });
                    return;
                }

                using (lease)
                {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var json = await reader.ReadToEndAsync();
                var rows = JsonSerializer.Deserialize<List<ProductDto>>(json, JsonOpts);
                if (rows is null || rows.Count == 0)
                {
                    await WriteJson(ctx, 400, new { error = "expected non-empty JSON array" });
                    return;
                }

                var saved = 0;
                var errors = new List<string>();
                foreach (var dto in rows)
                {
                    if (dto.ShopId is not { } shopId || shopId == 0)
                    {
                        errors.Add("row missing shopId");
                        continue;
                    }

                    try
                    {
                        _catalog.Upsert(MergeProduct(shopId, dto));
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"shopId={shopId}: {ex.Message}");
                    }
                }

                await WriteJson(ctx, errors.Count == 0 ? 200 : 207, new { saved, failed = errors.Count, errors });
                }

                return;
            }

            if (method == "POST" && path == "/cash/add")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var json = await reader.ReadToEndAsync();
                var dto = JsonSerializer.Deserialize<CashAddDto>(json, JsonOpts);
                if (dto is null)
                {
                    await WriteJson(ctx, 400, new { error = "bad json" });
                    return;
                }

                var opId = $"GMADD-{dto.AccountId}-0-{DateTime.UtcNow.Ticks}";
                var after = _cash.Credit(opId, dto.AccountId, dto.CharId, 0, dto.Amount, dto.PriceType, "gm_command");
                if (after is null)
                    await WriteJson(ctx, 500, new { error = "credit failed" });
                else
                    await WriteJson(ctx, 200, after);
                return;
            }

            if (method == "POST" && path == "/catalog/publish")
            {
                var lease = await _catalogGate.TryEnterAsync();
                if (lease is null)
                {
                    await WriteJson(ctx, _catalogGate.IsShuttingDown ? 503 : 409, new
                    {
                        error = _catalogGate.IsShuttingDown ? "server shutting down" : "catalog publish already running"
                    });
                    return;
                }

                using (lease)
                {
                using var names = _catalogOptions.OpenNameCatalog();
                var n = _catalog.PublishToIcs(_icsSyncCs, names.IsAvailable ? names : null);
                await WriteJson(ctx, 200, new { published = n, namesFromCompact = names.IsAvailable ? names.CompactPath : null });
                }

                return;
            }

            if (method == "POST" && path == "/catalog/fill-names")
            {
                var lease = await _catalogGate.TryEnterAsync();
                if (lease is null)
                {
                    await WriteJson(ctx, _catalogGate.IsShuttingDown ? 503 : 409, new
                    {
                        error = _catalogGate.IsShuttingDown ? "server shutting down" : "catalog busy"
                    });
                    return;
                }

                using (lease)
                {
                using var names = _catalogOptions.OpenNameCatalog();
                if (!names.IsAvailable)
                {
                    await WriteJson(ctx, 503, new
                    {
                        error = "client compact.sqlite3 not found",
                        hint = "Set ClientCompactPath in Config.json or AAEMU_CLIENT_COMPACT env"
                    });
                    return;
                }

                var updated = _catalog.FillMissingNames(names);
                await WriteJson(ctx, 200, new { updated, compact = names.CompactPath });
                }

                return;
            }

            if (method == "POST" && path is "/billing/give_item")
            {
                await WriteRaw(ctx, 200, """{"status":"ok","message":"give_item received"}""");
                return;
            }

            if (method == "POST" && path is "/billing/charge")
            {
                Log.Info("Charge notification");
                await WriteRaw(ctx, 200, """{"status":"ok"}""");
                return;
            }

            await WriteJson(ctx, 404, new { error = "not found", path });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "admin handle");
            try { await WriteJson(ctx, 500, new { error = ex.Message }); } catch { /* ignore */ }
        }
    }

    private ProductDef MergeProduct(uint shopId, ProductDto dto)
    {
        var existing = _catalog.Get(shopId);
        return new ProductDef(
            shopId,
            dto.Sku ?? existing?.Sku ?? shopId + 1000000u,
            dto.ItemId ?? existing?.ItemId ?? 0,
            dto.ItemCount ?? existing?.ItemCount ?? 1,
            dto.Name ?? existing?.Name ?? "",
            dto.Available ?? existing?.Available ?? 0,
            dto.Price ?? existing?.Price ?? 0,
            dto.DiscountPrice ?? existing?.DiscountPrice ?? 0,
            dto.PriceType ?? existing?.PriceType ?? 0,
            dto.IcsCurrency ?? existing?.IcsCurrency ?? 0,
            dto.BuyLimit ?? existing?.BuyLimit ?? 0,
            dto.LimitType ?? existing?.LimitType ?? 0,
            dto.MainTab ?? existing?.MainTab ?? 1,
            dto.SubTab ?? existing?.SubTab ?? 1,
            dto.TabPos ?? existing?.TabPos ?? 0);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static async Task WriteJson(HttpListenerContext ctx, int code, object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        await WriteRaw(ctx, code, json);
    }

    private static async Task WriteRaw(HttpListenerContext ctx, int code, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private sealed class ProductDto
    {
        public uint? ShopId { get; set; }
        public uint? Sku { get; set; }
        public uint? ItemId { get; set; }
        public uint? ItemCount { get; set; }
        public string? Name { get; set; }
        public byte? Available { get; set; }
        public uint? Price { get; set; }
        public uint? DiscountPrice { get; set; }
        public ushort? PriceType { get; set; }
        public byte? IcsCurrency { get; set; }
        public uint? BuyLimit { get; set; }
        public byte? LimitType { get; set; }
        public byte? MainTab { get; set; }
        public byte? SubTab { get; set; }
        public int? TabPos { get; set; }
    }

    private sealed class CashAddDto
    {
        public ulong AccountId { get; set; }
        public int CharId { get; set; }
        public int Amount { get; set; }
        public int PriceType { get; set; }
    }
}
