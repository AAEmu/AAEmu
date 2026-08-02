using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Default/true when activity resumes and AutoGiveUp/true when the character becomes idle.
/// </remarks>
public class CSChangeDiceBidRulePacket() : GamePacket(CSOffsets.CSChangeDiceBidRulePacket, 1)
{
    public int TeamId { get; private set; }
    public ulong MemberId { get; private set; }
    public DiceBidRuleKind ChangeKind { get; private set; }
    public bool ByIdleState { get; private set; }

    public override void Read(PacketStream stream)
    {
        TeamId = stream.ReadInt32();
        MemberId = stream.ReadUInt64(); // Native wire name: type.
        ChangeKind = (DiceBidRuleKind)stream.ReadSByte();
        ByIdleState = stream.ReadBoolean();

        TeamManager.Instance.ChangeDiceBidRule(Connection.ActiveChar, TeamId, MemberId, ChangeKind, ByIdleState);
    }
}
