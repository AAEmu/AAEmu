using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRefuseSquadInvitationPacket(ulong worldCharKey, sbyte refuseType)
    : GamePacket(SCOffsets.SCRefuseSquadInvitationPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(refuseType);
        return stream;
    }
}
