using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// i32 leadershipPeriodPoint, i32 leadershipPoint, i8 level and u32 gearScore.
/// </remarks>
public class SCTeamAskHandOverOwnerPacket(
    int teamId,
    ulong candidateId,
    TeamOwnerHandoverDetails details) : GamePacket(SCOffsets.SCTeamAskHandOverOwnerPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(candidateId);
        details.Write(stream);
        return stream;
    }
}
