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
    /// The <c>packetMode</c> byte of this packet is a different table from
    /// <see cref="SCTeamJointInfoPacket"/>'s (see <see cref="Models.Game.Team.TeamJointModes"/>).
    /// Mode 1 is the only value that makes a member's client *store* the joint: any other mode
    /// takes the relay branch instead, which echoes a joint-info packet back to the server and
    /// leaves the request refused, so a joint announced under it exists only on the server.
    /// </summary>
    public const byte PacketModeSet = 1;

    /// <summary>
    /// The mode paired with <see cref="PacketModeSet"/> that ends a joint: the same storing mode
    /// with <c>targetTeamId</c> zero, which is how the client is told there is no joint to store.
    /// This is what both refuses a joint that was asked for and releases one that was granted, and
    /// it is the only value a client drops a stored joint for — a break notification on its own
    /// leaves the member still holding the joint.
    /// </summary>
    public const byte PacketModeSetRefused = 1;

    /// <summary>The other team this member's client is being told about, or 0 to clear the joint.</summary>
    public uint TargetTeamId { get; } = targetTeamId;

    /// <summary>The joint this member's client is being told to store.</summary>
    public uint LeaderTeamId { get; } = leaderTeamId;

    /// <summary>How the client should treat the packet. See <see cref="PacketModeSet"/>.</summary>
    public byte PacketMode { get; } = packetMode;

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
