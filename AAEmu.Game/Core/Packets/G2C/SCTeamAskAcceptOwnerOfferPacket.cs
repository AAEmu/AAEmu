using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCTeamAskAcceptOwnerOfferPacket(
    int teamId,
    ulong candidateId,
    TeamOwnerHandoverDetails details) : GamePacket(SCOffsets.SCTeamAskAcceptOwnerOfferPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(candidateId);
        details.Write(stream);
        return stream;
    }
}
