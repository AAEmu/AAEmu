using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 166 change_charge_skill_count: moves a charge-bearing skill's pool ceiling.
/// </summary>
/// <remarks>
/// <c>value1</c> is the skill id and <c>value2</c> the delta. Four of the five authored rows name a skill
/// that carries <c>charge_count = 3</c> (56119 영구동토 44677, 56132 피의 복수 44702, 56130 시간의 고치
/// 44713, 56121 용수바람 44727) and ask for +3, i.e. "this effect hands the skill a second full pool"; the
/// fifth (35202, the only one a skill actually uses, on 37430 기술 기능 테스트) asks for +1 on 38893 빛의
/// 사격, itself a 3-charge skill.
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
        if (caster is not Unit unit || value1 <= 0)
            return;

        var template = SkillManager.Instance.GetSkillTemplate((uint)value1);
        var snapshot = unit.Charges.AddMax(
            (uint)value1,
            value2,
            template?.ChargeCount ?? 0,
            (uint)(template?.ChargeCooldownTime ?? 0),
            DateTime.UtcNow);

        Logger.Debug(
            "change_charge_skill_count: {0} skill {1} -> {2}/{3} charges",
            unit.Name,
            value1,
            snapshot.Available,
            snapshot.Max);
    }
}
