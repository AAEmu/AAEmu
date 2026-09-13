using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRatioChange : DoodadPhaseFuncTemplate
{
    // doodad_phase_funcs
    public int Ratio { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        var selected = owner.TrySelectPhaseRatio(Ratio);
        if (selected)
        {
            owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded
            if (caster is Character)
                Logger.Debug("DoodadFuncRatioChange: Chance {0}, Roll {1}, OverridePhase {2}",
                    Ratio, owner.PhaseRatio, NextPhase);
            else
                Logger.Trace("DoodadFuncRatioChange: Chance {0}, Roll {1}, OverridePhase {2}",
                    Ratio, owner.PhaseRatio, NextPhase);
            return true; // it is necessary to interrupt the phase functions and switch to NextPhase
        }
        if (caster is Character)
            Logger.Debug("DoodadFuncRatioChange: Chance {0}, Roll {1}, NextPhase {2}",
                Ratio, owner.PhaseRatio, NextPhase);
        else
            Logger.Trace("DoodadFuncRatioChange: Chance {0}, Roll {1}, NextPhase {2}",
                Ratio, owner.PhaseRatio, NextPhase);

        return false; // let's continue with the phase functions
    }
}
