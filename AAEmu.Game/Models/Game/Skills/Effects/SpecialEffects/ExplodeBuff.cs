using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Burns the target's beneficial effects away and deals <c>value1</c> damage for each one, up to
/// <c>value2</c>/<c>value3</c> of them. Carried by 내부 충격 16410 / 20650 and by the Timeout trigger of
/// buff 449 내부 충격.
/// </summary>
public class ExplodeBuff : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ExplodeBuff;

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
        if (target is not Unit unit || value1 <= 0)
            return;

        var maxBuffs = ExplodeBuffRules.MaxBuffsToBurn(value2, value3);
        if (maxBuffs < 1)
            return;

        // The decision runs on a snapshot: burning an effect mutates the list the owner holds, and ending one
        // buff can also end a second that was only kept alive by the first. GetAllBuffs is the Buffs API that
        // classifies by kind, so the rule can tell 이로운 효과 (beneficial) from a debuff.
        var held = new List<Buff>();
        unit.Buffs.GetAllBuffs(held, [], [], includeAllPassives: false);

        var chosen = ExplodeBuffRules.SelectBuffs(
            held
                .Where(buff => buff.Template != null)
                .Select(buff => new ExplodeBuffRules.BurnCandidate(
                    (int)buff.Index, buff.Template.Kind, buff.Passive, buff.Template.System)),
            maxBuffs);
        if (chosen.Count == 0)
            return;

        var damage = ExplodeBuffRules.DamageFor(chosen.Count, value1);
        if (damage <= 0)
            return;

        foreach (var candidate in chosen)
        {
            var buff = unit.Buffs.GetEffectByIndex((uint)candidate.Index);
            if (buff?.Template == null)
                continue;

            // Dispel is the single exit every end path goes through, and it is what Buffs.RemoveBuff calls,
            // so the victim and everyone watching are told the effect is gone.
            buff.Template.Dispel(buff.Caster, buff.Owner, buff);
            unit.Buffs.RemoveEffect(buff);
        }

        unit.ReduceCurrentHp(caster, damage);
    }
}
