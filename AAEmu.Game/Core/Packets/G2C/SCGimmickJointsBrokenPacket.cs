using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One broken joint reported for a gimmick by the authoritative Zone simulation.
/// </summary>
public readonly record struct GimmickJointBreak(uint GimmickId, int JointId, int Epicenter);

/// <summary>
/// u32 gimmickId, i32 jointId, and i32 epicenter for each entry.
/// </summary>
public class SCGimmickJointsBrokenPacket : GamePacket
{
    public const int MaxCountPerPacket = 200;

    private readonly GimmickJointBreak[] _joints;

    public SCGimmickJointsBrokenPacket(GimmickJointBreak[] joints)
        : base(SCOffsets.SCGimmickJointsBrokenPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(joints);
        if (joints.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(joints),
                joints.Length,
                $"The native client accepts at most {MaxCountPerPacket} broken joints per packet.");
        }

        _joints = joints;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_joints.Length);
        foreach (var joint in _joints)
        {
            stream.Write(joint.GimmickId);
            stream.Write(joint.JointId);
            stream.Write(joint.Epicenter);
        }

        return stream;
    }
}
