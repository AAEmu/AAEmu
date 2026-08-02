using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Detach : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character chara)
            return;

        Logger.Debug("Special effects: Detach value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        // The dismount skills name no target of their own, so the rider comes off whatever it is
        // currently riding. ForceDismount covers both mates and slaves and clears the transform parent,
        // sending SCUnitDetached and the matching zone relay for each.
        if (!chara.ForceDismount(AttachUnitReason.SlaveBinding))
            Logger.Debug("Detach: {0} was not riding anything", chara.Name);
    }
}

internal enum DetachUnitReason : byte
{
    None = 0x0,
    Death = 0x1,
    KnockBack = 0x2,
    RagDoll = 0x3,
    UseMountSkill = 0x4,
    ForciblyUnbindSlave = 0x5,
    UnboardTransfer = 0x6,
    ForciblyByServer = 0x7,
    BeginCutscene = 0x8,
    UnmountMate = 0x9,
    DismissPet = 0xA,
    KickByMaster = 0xB,
    KickByDespawn = 0xC,
    ScheduleToLogout = 0xD,
    ReAttach = 0xE
}
