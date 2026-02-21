using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Managers;

public interface ICashShopManager
{
    bool Enabled { get; }
    Dictionary<uint, IcsSku> SKUs { get; set; }
    Dictionary<uint, IcsItem> ShopItems { get; set; }
    List<IcsMenu> MenuItems { get; set; }
    void Load();
    void Initialize();
    void EnabledShop();
    void DisableShop();
    void CreditDisperseTick(TimeSpan delta);
    void SendICSPage(GameConnection connection, byte mainTabId, byte subTabId, ushort page);
}
