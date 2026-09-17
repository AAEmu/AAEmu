using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Special effect 158 <c>charge_cooldown</c>: (re)starts the recharge timer of the skill that carries
/// the effect, with <c>value1</c> as the interval in milliseconds.
/// </summary>
/// <remarks>
/// 13 descriptors exist and every one of them stores an interval that some charge skill also stores in
/// <c>skills.charge_cooldown_time</c> — 36724 20000 (39289 당기기: 돌풍), 41872 16000 (38893 빛의 사격),
/// 51572-51574 and 51835 86400000 (the four once-a-day 3-charge skills 44677/44702/44713/44727),
/// 55123 22000 (13281 다발 사격), 62408/64685/64715 12000 (48002, 48592), 62804/62806 15000
/// (41252/41281/47950/48016), 62805 9000 (47986 휘몰아치기).
///
/// No descriptor names a target skill and none is reachable from an enabled <c>skill_effects</c> row —
/// the one apparent hit (effect 99743 on skill 50268) is a <c>BuffEffect</c> whose id happens to collide
/// with a <c>special_effects</c> id, which is the false positive the AAEmu schema invites. So the only
/// owner the effect can act on is the skill that applied it, and that is what this does. If a live cast
/// is ever found routing it elsewhere, the interval comparison above is where to start.
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
        if (caster is not Unit unit || skill?.Template == null || value1 <= 0)
            return;

        var template = skill.Template;
        unit.Cooldowns.RestartChargeRecharge(template.Id, template.ChargeCount, value1);
        Logger.Debug("Special effects: ChargeCooldown skill {0} recharge {1} ms", template.Id, value1);
    }
}
