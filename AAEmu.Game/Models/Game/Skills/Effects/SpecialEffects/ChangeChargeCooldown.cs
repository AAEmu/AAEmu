using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Special effect 167 <c>change_charge_cooldown</c>: <c>value1</c> is the skill whose recharge timer
/// moves, <c>value2</c> the signed offset in milliseconds.
/// </summary>
/// <remarks>
/// One descriptor, 35203: <c>{ value1 = 38893 (빛의 사격), value2 = -3000 }</c>, reached from skill 37272
/// 버프 기능 테스트. Negative shortens the timer, which is the only direction the shipped row uses;
/// a positive value extends it, and an offset that lands in the past leaves the charge due immediately.
/// </remarks>
public class ChangeChargeCooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChangeChargeCooldown;

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
            Logger.Warn("Special effects: ChangeChargeCooldown for unknown skill {0}", value1);
            return;
        }

        unit.Cooldowns.ChangeChargeRechargeTime(skillId, template.ChargeCount, value2);
        Logger.Debug("Special effects: ChangeChargeCooldown skill {0} offset {1} ms", value1, value2);
    }
}
