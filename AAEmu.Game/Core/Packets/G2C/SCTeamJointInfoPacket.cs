using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed record TeamJointInfo(
    long Type,
    string TargetCharName,
    uint TargetTeamId,
    int MemberCount,
    uint JointTeamId,
    bool Leader);

/// <summary>
/// The joint-info body is a mode byte followed by the shared joint-info structure. The mode values
/// are the client's TEAM_JOINT_REQUEST / TEAM_JOINT_RESPONSE UI event ids, not gameplay ids.
/// </summary>
public sealed class SCTeamJointInfoPacket(sbyte mode, TeamJointInfo info) : GamePacket(SCOffsets.SCTeamJointInfoPacket, 1)
{
    /// <summary>The mode this reply answers. A menu query is answered with the mode it asked in.</summary>
    public sbyte Mode { get; } = mode;

    /// <summary>The body carried alongside the mode.</summary>
    public TeamJointInfo Info { get; } = info;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unchecked((byte)mode));
        stream.Write(info.Type);
        stream.Write(info.TargetCharName);
        stream.Write(info.TargetTeamId);
        stream.Write(info.MemberCount);
        stream.Write(info.JointTeamId);
        stream.Write(info.Leader);
        return stream;
    }
}
