using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Trading;

public sealed record SpecialtyPurchaseWrite(
    uint CharacterId,
    uint AccountId,
    long ExpectedMoney,
    long NewMoney,
    int ExpectedLabor,
    int NewLabor,
    int ExpectedLocalLabor,
    int NewLocalLabor,
    Item CargoItem,
    Item PreviousBackpack,
    ulong BagContainerId,
    int BagSlot,
    SpecialtyMarketWrite Market,
    long BankMoney,
    InventoryPersistenceSnapshot Inventory);
