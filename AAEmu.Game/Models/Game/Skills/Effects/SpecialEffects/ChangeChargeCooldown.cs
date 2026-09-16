using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 167 change_charge_cooldown: shifts a charge-bearing skill's recharge interval by a delta.
/// </summary>
/// <remarks>
/// The single shipped row (35203) is on 37272 버프 기능 테스트 and passes 38893 (빛의 사격, a 3-charge
/// skill) with -3000, i.e. three seconds off each charge's recharge. Same (skill id, delta) shape as
/// <see cref="ChangeChargeSkillCount"/>, in milliseconds against <c>skills.charge_cooldown_time</c> rather
/// than in charges.
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
        if (caster is not Unit unit || value1 <= 0)
            return;

        var template = SkillManager.Instance.GetSkillTemplate((uint)value1);
        var snapshot = unit.Charges.AddRecharge(
            (uint)value1,
            value2,
            template?.ChargeCount ?? 0,
            (uint)(template?.ChargeCooldownTime ?? 0),
            DateTime.UtcNow);

        Logger.Debug(
            "change_charge_cooldown: {0} skill {1} recharge {2} ms",
            unit.Name,
            value1,
            snapshot.RechargeMs);
    }
}
