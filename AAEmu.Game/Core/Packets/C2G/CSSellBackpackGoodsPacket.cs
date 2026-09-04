using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellBackpackGoodsPacket() : GamePacket(CSOffsets.CSSellBackpackGoodsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        var auxiliary = stream.ReadBc();

        Logger.Debug("SellBackpackGoods, NpcObjId: {0}, Auxiliary: {1}", npcObjId, auxiliary);
        SpecialtyManager.Instance.SellSpecialty(Connection.ActiveChar, npcObjId);
    }
}
