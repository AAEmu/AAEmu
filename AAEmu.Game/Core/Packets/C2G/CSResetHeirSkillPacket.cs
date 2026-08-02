using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

using AAEmu.Game.Models.Game.Heirs;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Requests removal of an active Heir-skill successor.</summary>
/// <remarks>
/// u32 resetKind, i8 ability, i32 successorSkillId.
/// </remarks>
public class CSResetHeirSkillPacket() : GamePacket(CSOffsets.CSResetHeirSkillPacket, 1)
{
    public uint ResetKind { get; private set; }
    public sbyte Ability { get; private set; }
    public int SuccessorSkillId { get; private set; }

    public override void Read(PacketStream stream)
    {
        ResetKind = stream.ReadUInt32();
        Ability = stream.ReadSByte();
        SuccessorSkillId = stream.ReadInt32();

        if (!Enum.IsDefined(typeof(HeirSkillResetKind), ResetKind))
            return;

        Connection.ActiveChar?.HeirSkills.TryReset(
            (HeirSkillResetKind)ResetKind,
            Ability,
            SuccessorSkillId);
    }
}
