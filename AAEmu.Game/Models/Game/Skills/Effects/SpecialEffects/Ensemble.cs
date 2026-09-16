using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Starts the ensemble the caster is leading. Every member has to have sent their part first, which is
/// what the ensemble window waits for before it offers the play button.
/// </summary>
public class Ensemble : SpecialEffectAction
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
        if (caster is not Character maestro)
        {
            Logger.Debug("Special effects: Ensemble without a player casting it");
            return;
        }

        Logger.Trace("Special effects: Ensemble started by {0}", maestro.Name);
        if (!MusicManager.Instance.TryStartEnsemble(maestro))
            Logger.Warn("Ensemble: {0} tried to play an ensemble that is not ready", maestro.Name);
    }
}
