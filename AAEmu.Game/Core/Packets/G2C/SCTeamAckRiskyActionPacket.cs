using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamAckRiskyActionPacket(
    int teamId,
    ulong targetId,
    RiskyAction riskyAction,
    TeamRiskyWarningFlags warningFlags,
    ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCTeamAckRiskyActionPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 teamId, u64 type, u8 ra, i32 w, i16 ErrorMessage.
        stream.Write(teamId);
        stream.Write(targetId);
        stream.Write((byte)riskyAction);
        stream.Write((int)warningFlags);
        stream.Write((short)errorMessage);
        return stream;
    }
}
