using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSMenuListPacket() : GamePacket(CSOffsets.CSICSMenuListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // This request has no body.
        Logger.Info("ICSMenuList open={0} enabled={1} bill={2} menus={3} shops={4}",
            CashShopManager.Instance.IsOpenForPlayers,
            CashShopManager.Instance.Enabled,
            BillClientManager.Instance.IsConnected,
            CashShopManager.Instance.MenuItems.Count,
            CashShopManager.Instance.ShopItems.Count);

        // Send menu, goods, details, and exchange ratio in the order required to complete refresh.
        Connection.SendPacket(new SCICSMenuListPacket(CashShopManager.Instance.IsOpenForPlayers));
        if (CashShopManager.Instance.IsOpenForPlayers)
        {
            CashShopManager.Instance.SendAllIcsTabsFirstPage(Connection);
            // ratio>0 enables AA-point charge UI; 100 is a safe demo default (not load-bearing for list).
            Connection.SendPacket(new SCICSExchangeRatioPacket(100));
            // Client often follows ratio with CSICSBuyCountRequest; push eagerly too.
            if (Connection.ActiveChar != null)
            {
                CashShopManager.Instance.SendBuyCounts(
                    Connection,
                    Connection.ActiveChar.AccountId,
                    Connection.ActiveChar.Id);
            }
        }
    }
}
