using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeaveSquadMemberPacket(ulong worldCharKey, byte mask, bool expelled)
    : GamePacket(SCOffsets.SCLeaveSquadMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(mask);
        stream.Write(expelled);
        NestedBlobWire.WriteEmpty(stream);
        return stream;
    }
}
