using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The acceptance half of the two-step joint handshake; the pending request is resolved by the
/// world joint manager.
/// </summary>
/// <remarks>
/// Body: u64 type, bool myTeamLeader, bool accept, bool timeout.
/// </remarks>
public class CSTeamJointPacket() : GamePacket(CSOffsets.CSTeamJointPacket, 1)
{
    public ulong TypeValue { get; private set; }
    public bool MyTeamLeader { get; private set; }
    public bool Accept { get; private set; }
    public bool Timeout { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        MyTeamLeader = stream.ReadBoolean();
        Accept = stream.ReadBoolean();
        Timeout = stream.ReadBoolean();

        if (Connection?.ActiveChar is { } character)
            TeamJointManager.Instance.RespondToJoint(character.Id, TypeValue, MyTeamLeader, Accept, Timeout);
    }
}
