using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ExpeditionLevelChange : SpecialEffectAction
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
        if (caster is not Character character || value1 <= 0)
            return;

        // The skill pipeline owns the configured source-item consumption after this effect succeeds.
        if (!ExpeditionManager.Instance.TryApplyLevelChange(character, (uint)value1))
            Logger.Warn("Unable to change expedition level to {0} for character {1}", value1, character.Id);
    }
}
