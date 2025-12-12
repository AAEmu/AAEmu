using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSGoodsListPacket() : GamePacket(CSOffsets.CSICSGoodsListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var mainTabId = stream.ReadByte();
        var subTabId = stream.ReadByte();
        var page = stream.ReadUInt16();

        CashShopManager.Instance.SendICSPage(Connection, mainTabId, subTabId, page);
    }
}
