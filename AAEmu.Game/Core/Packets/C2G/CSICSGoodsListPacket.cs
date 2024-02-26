using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSGoodsListPacket : GamePacket
{
    public CSICSGoodsListPacket() : base(CSOffsets.CSICSGoodsListPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mainTabId = stream.ReadByte();
        var subTabId = stream.ReadByte();
        var page = stream.ReadUInt16();

        CashShopManager.Instance.SendICSPage(Connection, mainTabId, subTabId, page);
    }
}
