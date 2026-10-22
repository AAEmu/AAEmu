using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Content special 194: add permanent bonus skill points (Stone of Gods and Heroes / test item).
/// <c>value1</c> is the amount; the two shipped rows both use 1.
/// </summary>
public class AdditionalSkillPoint : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.AdditionalSkillPoint;

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

        if (value2 != 0 || value3 != 0 || value4 != 0)
        {
            Logger.Error(
                "AdditionalSkillPoint rejected unsupported trailing operands value2 {0}, value3 {1}, value4 {2}",
                value2, value3, value4);
            return;
        }

        if (!character.TryAddAdditionalSkillPoints(value1))
            Logger.Error("AdditionalSkillPoint rejected amount {0} for {1}", value1, character.Name);
    }
}
