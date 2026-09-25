using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCTeamJointPacket(
    uint targetTeamId,
    uint leaderTeamId,
    long type,
    byte packetMode,
    int jointOrder) : GamePacket(SCOffsets.SCTeamJointPacket, 1)
{
    /// <summary>
    /// The value of <c>packetMode</c> is not established: the client switches on it, but the mode
    /// table for this packet was never recovered, and it is NOT the same numbering as
    /// <c>SCTeamJointInfoPacket.mode</c> (see <see cref="Models.Game.Team.TeamJointModes"/>). Zero is
    /// written as the neutral value; it is the only byte in this slice that is not evidence-backed.
    /// </summary>
    public const byte PacketModeUnresolved = 0;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(targetTeamId);
        stream.Write(leaderTeamId);
        stream.Write(type);
        stream.Write(packetMode);
        stream.Write(jointOrder);
        return stream;
    }
}
