using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCTeamOwnerChangedPacket(int teamId, ulong ownerId) : GamePacket(SCOffsets.SCTeamOwnerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(ownerId);
        return stream;
    }
}
