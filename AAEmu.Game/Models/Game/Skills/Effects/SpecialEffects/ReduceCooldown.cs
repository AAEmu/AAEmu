using AAEmu.Game.Core.Managers;
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
        {
            var remaining = unit.Cooldowns.GetRemaining(affectedSkillId);
            var reduction = flatMilliseconds + remaining.TotalMilliseconds * percent / 100d;
            unit.Cooldowns.ReduceCooldown(affectedSkillId, TimeSpan.FromMilliseconds(reduction));
        }
    }
}
