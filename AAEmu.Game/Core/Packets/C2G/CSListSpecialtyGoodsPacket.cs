using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListSpecialtyGoodsPacket() : GamePacket(CSOffsets.CSListSpecialtyGoodsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        _ = stream.ReadBc(); // Opaque client context, also present on the specialty sale request.

        SpecialtyManager.Instance.SendBuyList(Connection.ActiveChar, npcObjId);
    }
}
