using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public sealed class ReduceCooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ReduceCooldown;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3,
        int value4)
    {
        Execute(caster, casterObj, target, targetObj, castObj, skill, skillObject, time, value1, value2, value3,
            value4, 0, 0, 0);
    }

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int skillId, int skillTagId,
        int value3, int value4, int value5, int flatMilliseconds, int percent)
    {
        if (caster is not Unit unit)
            return;

        IEnumerable<uint> affected = skillId > 0
            ? [(uint)skillId]
            : skillTagId > 0
                ? SkillManager.Instance.GetSkillsByTag((uint)skillTagId)
                : [];

        foreach (var affectedSkillId in affected.Distinct())
            unit.Cooldowns.ApplyCooldownReduction(affectedSkillId, flatMilliseconds, percent);

        // Server-side reduction alone leaves the client's grey bar unchanged until login
        // re-sends SCCooldowns. Mirror ResetCooldown and push the reduce packet.
        if (unit is Character character && (skillId > 0 || skillTagId > 0))
        {
            character.SendPacket(new SCSkillCooldownReducePacket(
                character.ObjId,
                (uint)Math.Max(skillId, 0),
                (uint)Math.Max(skillTagId, 0),
                percent,
                0,
                flatMilliseconds,
                rstc: false,
                rtsc: false,
                rtstc: false));
        }
    }
}
