using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Charges the caster mana for a skill that carries its cost in the effect instead of in
/// <c>skills.mana_cost</c>. The formula is unconfirmed — see <see cref="ManaCostRules"/> — and no shipped row
/// reaches this class.
/// </summary>
public class ManaCost : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ManaCost;

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
        if (caster is not Character character)
            return;

        var manaCost = character.SkillModifiersCache.ApplyModifiers(
            skill, SkillAttribute.ManaCost, ManaCostRules.Compute(value1, value2));
        character.ReduceCurrentMp(null, (int)manaCost);

        character.LastCast = DateTime.UtcNow;
        character.IsInPostCast = true;
    }
}
