using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOverHeadMarkerSetPacket(uint teamId, OverHeadMark index, bool isObjId, uint id)
    : GamePacket(SCOffsets.SCOverHeadMarkerSetPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write((int)index);

        stream.Write((byte)(isObjId ? 2 : 1));
        if (isObjId)
            stream.WriteBc(id);
        else
            stream.Write(id);
        return stream;
    }
}
