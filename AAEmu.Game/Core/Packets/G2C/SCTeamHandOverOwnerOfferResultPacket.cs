using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCTeamHandOverOwnerOfferResultPacket(int teamId, ulong candidateId, bool accept) : GamePacket(SCOffsets.SCTeamHandOverOwnerOfferResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(candidateId);
        stream.Write(accept);
        return stream;
    }
}
