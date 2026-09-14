using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>Applies the explicit family level selected by the content row's first value.</summary>
public class FamilyLevelChange : SpecialEffectAction
{
    internal Func<Character, uint, bool> TryLevelUp { get; init; } =
        (character, level) => FamilyManager.Instance.TryLevelUp(character, level);

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
        if (caster is not Character character || value1 <= 0)
            return;

        if (value2 != 0 || value3 != 0 || value4 != 0)
        {
            Logger.Error(
                "FamilyLevelChange rejected unsupported operands value1 {0}, value2 {1}, value3 {2}, value4 {3}",
                value1,
                value2,
                value3,
                value4);
            return;
        }

        TryLevelUp(character, (uint)value1);
    }
}
