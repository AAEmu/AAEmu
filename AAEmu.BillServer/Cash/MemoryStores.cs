using System.Collections.Concurrent;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.BillServer.Cash;

public sealed class MemoryCashStore : ICashStore
{
    private readonly ConcurrentDictionary<ulong, CashWallet> _wallets = new();
    private readonly ConcurrentDictionary<string, CashWallet> _ops = new();
    private readonly ConcurrentDictionary<(ulong Acc, int Product), int> _buyCounts = new();
    private readonly object _gate = new();

    public CashWallet GetBalance(ulong accountId) =>
        _wallets.GetOrAdd(accountId, _ => new CashWallet(0, 0));

    public CashWallet? Credit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source)
    {
        lock (_gate)
        {
            if (_ops.TryGetValue(opId, out var prior))
                return prior;
            var w = GetBalance(accountId);
            w = priceType == 5
                ? w with { BonusCash = w.BonusCash + amount }
                : w with { Cash = w.Cash + amount };
            _wallets[accountId] = w;
            _ops[opId] = w;
            return w;
        }
    }

    public CashWallet? Debit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source)
    {
        lock (_gate)
        {
            if (_ops.TryGetValue(opId, out var prior))
                return prior;
            var w = GetBalance(accountId);
            if (priceType == 5)
            {
                if (w.BonusCash < amount)
                    return null;
                w = w with { BonusCash = w.BonusCash - amount };
            }
            else
            {
                if (w.Cash < amount)
                    return null;
                w = w with { Cash = w.Cash - amount };
            }

            _wallets[accountId] = w;
            _ops[opId] = w;
            return w;
        }
    }

    public int GetBuyCount(ulong accountId, int charId, int productId, int limitType) =>
        _buyCounts.GetValueOrDefault((accountId, productId));

    public void RecordBuySlot(long requestId, ulong accountId, int charId, int buySource, int slot, int cashShopId, int priceType, int price, int limitType, int buyLimit, string source)
    {
        lock (_gate)
        {
            var key = (accountId, cashShopId);
            _buyCounts[key] = _buyCounts.GetValueOrDefault(key) + 1;
        }
    }

    public void ConfirmBuy(long requestId, int charId, int productId)
    {
        // memory path: counted at RecordBuySlot
    }
}

public sealed class MemoryCatalogStore : ICatalogStore
{
    private readonly ConcurrentDictionary<uint, ProductDef> _products = new();
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public MemoryCatalogStore()
    {
        // Default demo SKUs (same as SQL seed)
        Upsert(new ProductDef(2000000, 1000000, 29176, 1, "Starter Pack Credit Test", 1, 100, 0, 0, 0, 0, 0, 1, 1, 0));
        Upsert(new ProductDef(2000001, 1000001, 29177, 1, "Limited Mount Coupon", 1, 500, 400, 0, 0, 3, 1, 1, 1, 1));
        Upsert(new ProductDef(2000002, 1000002, 29178, 5, "Hidden Glider (off)", 0, 250, 0, 0, 0, 0, 0, 1, 2, 0));
    }

    public IReadOnlyList<ProductDef> ListAll() => _products.Values.OrderBy(p => p.ShopId).ToList();
    public IReadOnlyList<ProductDef> ListAvailable() => _products.Values.Where(p => p.Available != 0).OrderBy(p => p.ShopId).ToList();
    public ProductDef? Get(uint shopId) => _products.TryGetValue(shopId, out var p) ? p : null;

    public void Upsert(ProductDef product) => _products[product.ShopId] = product;

    public int PublishToIcs(string? gameConnectionString, CompactItemNameCatalog? nameCatalog = null)
    {
        if (string.IsNullOrWhiteSpace(gameConnectionString))
        {
            Log.Warn("PublishToIcs: no game connection string");
            return 0;
        }

        return IcsCatalogPublisher.Publish(gameConnectionString, ListAvailable(), nameCatalog);
    }

    public int FillMissingNames(CompactItemNameCatalog nameCatalog)
    {
        if (!nameCatalog.IsAvailable)
            return 0;

        var updated = 0;
        foreach (var p in ListAll())
        {
            if (!CompactItemNameCatalog.NeedsResolvedName(p.Name))
                continue;

            var resolved = nameCatalog.ResolveDisplayName(p.Name, p.ItemId);
            if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, p.Name, StringComparison.Ordinal))
                continue;

            Upsert(p with { Name = resolved });
            updated++;
        }

        Log.Info("Filled {0} product names from {1}", updated, nameCatalog.CompactPath);
        return updated;
    }
}
