using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ChangeSkillActiveType : SpecialEffectAction
{
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
        var character = target as Character ?? caster as Character;
        if (character == null || value1 <= 0 || value3 < 0 ||
            !Enum.IsDefined(typeof(SkillActiveType), value2))
            return;

        if (!character.SkillActiveTypes.TrySet(
                checked((uint)value3),
                checked((uint)value1),
                (SkillActiveType)value2))
        {
            Logger.Warn(
                "ChangeSkillActiveType rejected character={0}, heir={1}, skill={2}, active={3}",
                character.Id, value3, value1, value2);
        }
    }
}
