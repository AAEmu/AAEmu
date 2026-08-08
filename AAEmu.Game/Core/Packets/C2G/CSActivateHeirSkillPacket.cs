using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Features;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Requests activation or replacement of an unlocked Heir skill.</summary>
/// <remarks>
/// i32 heirSkillId, i32 successorSkillId, bool isChange.
/// </remarks>
public class CSActivateHeirSkillPacket() : GamePacket(CSOffsets.CSActivateHeirSkillPacket, 1)
{
    public int HeirSkillId { get; private set; }
    public int SuccessorSkillId { get; private set; }
    public bool IsChange { get; private set; }

    public override void Read(PacketStream stream)
    {
        HeirSkillId = stream.ReadInt32();
        SuccessorSkillId = stream.ReadInt32();
        IsChange = stream.ReadBoolean();

        if (!FeaturesManager.Fsets.Check(Feature.useHeirSkill) || HeirSkillId <= 0 || SuccessorSkillId <= 0)
            return;

        Connection.ActiveChar?.HeirSkills.TryActivate(
            checked((uint)HeirSkillId),
            checked((uint)SuccessorSkillId),
            IsChange);
    }
}
