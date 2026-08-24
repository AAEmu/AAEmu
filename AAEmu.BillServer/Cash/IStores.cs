namespace AAEmu.BillServer.Cash;

public readonly record struct CashWallet(int Cash, int BonusCash);

public readonly record struct ProductDef(
    uint ShopId,
    uint Sku,
    uint ItemId,
    uint ItemCount,
    string Name,
    byte Available,
    uint Price,
    uint DiscountPrice,
    ushort PriceType,
    byte IcsCurrency,
    uint BuyLimit,
    byte LimitType,
    byte MainTab,
    byte SubTab,
    int TabPos);

public interface ICashStore
{
    CashWallet GetBalance(ulong accountId);
    /// <summary>Returns remaining wallet after credit, or null if op failed.</summary>
    CashWallet? Credit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source);
    /// <summary>Returns remaining wallet after debit, or null if insufficient / fail.</summary>
    CashWallet? Debit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source);
    int GetBuyCount(ulong accountId, int charId, int productId, int limitType);
    void RecordBuySlot(long requestId, ulong accountId, int charId, int buySource, int slot, int cashShopId, int priceType, int price, int limitType, int buyLimit, string source);
    void ConfirmBuy(long requestId, int charId, int productId);
}

public interface ICatalogStore
{
    IReadOnlyList<ProductDef> ListAll();
    IReadOnlyList<ProductDef> ListAvailable();
    ProductDef? Get(uint shopId);
    void Upsert(ProductDef product);
    /// <summary>Push available products into aaemu_game ics_* tables for World CashShopManager.</summary>
    int PublishToIcs(string? gameConnectionString, CompactItemNameCatalog? nameCatalog = null);
    /// <summary>Replace placeholder/empty names from compact.sqlite3 item localization.</summary>
    int FillMissingNames(CompactItemNameCatalog nameCatalog);
}
