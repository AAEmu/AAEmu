using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 158 charge_cooldown: sets the recharge interval of the skill carrying the effect.
/// </summary>
/// <remarks>
/// <c>value1</c> is the interval in milliseconds - 20000, 16000, 22000, 12000, 15000, 9000, and 86400000
/// for the four day-long ones (51572-51574 and 51835). The effect is named after the column it overrides,
/// <c>skills.charge_cooldown_time</c>, and carries no skill id, so the only pool it can name is the casting
/// skill's own. Milliseconds fits: the 26 charge skills author 8000-22000 or a whole day or hour in that
/// column (38893 빛의 사격: 3 charges at 16000 ms against a 9000 ms cooldown), and the six short values here
/// sit in the same band.
/// <para>
/// All thirteen rows are dormant: <c>special_effects</c> is only reachable through an <c>effects</c> row
/// that names it as its SpecialEffect, and none of the thirteen has one, so no skills row can be cast into
/// this action today. It is implemented so that re-enabling the content does not fall back to the
/// "Unknown special effect" warning, and the reading above is the effect's own name rather than anything
/// the shipped skills exercise.
/// </para>
/// </remarks>
public class ChargeCooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChargeCooldown;

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
        if (caster is not Unit unit || skill == null || value1 < 0)
            return;

        var skillId = skill.Template.Id;
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        var snapshot = unit.Charges.SetRecharge(
            skillId,
            (uint)value1,
            template?.ChargeCount ?? 0,
            (uint)(template?.ChargeCooldownTime ?? 0),
            DateTime.UtcNow);

        Logger.Debug(
            "charge_cooldown: {0} skill {1} recharge {2} ms",
            unit.Name,
            skillId,
            snapshot.RechargeMs);
    }
}
