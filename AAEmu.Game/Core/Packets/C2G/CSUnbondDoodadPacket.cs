using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnbondDoodadPacket() : GamePacket(CSOffsets.CSUnbondDoodadPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var characterObjId = stream.ReadBc();
        var doodadObjId = stream.ReadBc();

        if (Connection.ActiveChar.ObjId != characterObjId)
            return;

        BondDoodad.TryRelease(Connection.ActiveChar, doodadObjId);
    }
}
