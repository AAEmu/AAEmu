using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListSpecialtyGoodsPacket : GamePacket
{
    public CSListSpecialtyGoodsPacket() : base(CSOffsets.CSListSpecialtyGoodsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        Logger.Warn("ListSpecialtyGoods, ObjId: {0}", objId);
    }
}
