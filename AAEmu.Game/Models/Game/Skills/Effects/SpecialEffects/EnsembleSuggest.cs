using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Asks a player to join the caster's ensemble. The skill runs once per player it reached, so each call
/// asks the one it was given: the session is opened by the first and kept for the rest.
/// </summary>
public class EnsembleSuggest : SpecialEffectAction
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
        if (caster is not Character maestro || target is not Character invited)
        {
            Logger.Debug("Special effects: EnsembleSuggest without a player on either side");
            return;
        }

        Logger.Trace("Special effects: EnsembleSuggest {0} -> {1}", maestro.Name, invited.Name);
        MusicManager.Instance.SuggestEnsembleTo(maestro, invited);
    }
}
