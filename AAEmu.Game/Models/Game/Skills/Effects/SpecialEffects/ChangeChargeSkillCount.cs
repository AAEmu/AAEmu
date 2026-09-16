using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Special effect 166 <c>change_charge_skill_count</c>: <c>value1</c> is the skill whose charges change,
/// <c>value2</c> the signed number of charges to add.
/// </summary>
/// <remarks>
/// Five descriptors, and the two shapes in the data both read as "give charges back": 35202 adds one
/// charge to 38893 빛의 사격 (3 charges on a 16 s recharge), and 56119/56121/56130/56132 add three —
/// a full refill — to the four once-a-day 3-charge skills 44677 영구동토, 44727 용수바람,
/// 44713 시간의 고치 and 44702 피의 복수 (86,400,000 ms recharge). The two that ship enabled are
/// 35202 on skill 37430 기술 기능 테스트 and the refills on the 86,400,000 ms family.
/// </remarks>
public class ChangeChargeSkillCount : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChangeChargeSkillCount;

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
        if (caster is not Unit unit || value1 <= 0 || value2 == 0)
            return;

        var skillId = (uint)value1;
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
        {
            Logger.Warn("Special effects: ChangeChargeSkillCount for unknown skill {0}", value1);
            return;
        }

        // The name says "count", and 56119 adds three to a 3-charge skill, so the value is a delta on
        // the pool rather than a new ceiling.
        unit.Cooldowns.ChangeChargeCount(skillId, template.ChargeCount, value2);
        Logger.Debug("Special effects: ChangeChargeSkillCount skill {0} delta {1}", value1, value2);
    }
}
