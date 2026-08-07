using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Managers;

public interface ICashShopManager : ILoadable, IInitializable
{
    bool Enabled { get; }
    Dictionary<uint, IcsSku> SKUs { get; set; }
    Dictionary<uint, IcsItem> ShopItems { get; set; }
    List<IcsMenu> MenuItems { get; set; }
    void EnabledShop();
    void DisableShop();
    void CreditDisperseTick(TimeSpan delta);
    void SendShopContents(GameConnection connection);
}
