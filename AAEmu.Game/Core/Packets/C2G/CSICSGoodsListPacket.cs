using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSGoodsListPacket : GamePacket
{
    public CSICSGoodsListPacket() : base(CSOffsets.CSICSGoodsListRequestPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mainTabId = stream.ReadByte();
        var subTabId = stream.ReadByte();
        var page = stream.ReadByte(); // page short in 1.2, byte in 3+

        CashShopManager.Instance.SendICSPage(Connection, mainTabId, subTabId, page);
    }
}
