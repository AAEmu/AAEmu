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
                Logger.Debug("DoodadFuncRatioChange: Weight {0}, Roll {1}, UpperBound {2}, OverridePhase {3}",
                    Ratio, owner.PhaseRatio, owner.CumulativePhaseRatio, NextPhase);
            else
                Logger.Trace("DoodadFuncRatioChange: Weight {0}, Roll {1}, UpperBound {2}, OverridePhase {3}",
                    Ratio, owner.PhaseRatio, owner.CumulativePhaseRatio, NextPhase);
            return true; // it is necessary to interrupt the phase functions and switch to NextPhase
        }
        if (caster is Character)
            Logger.Debug("DoodadFuncRatioChange: Weight {0}, Roll {1}, UpperBound {2}, NextPhase {3}",
                Ratio, owner.PhaseRatio, owner.CumulativePhaseRatio, NextPhase);
        else
            Logger.Trace("DoodadFuncRatioChange: Weight {0}, Roll {1}, UpperBound {2}, NextPhase {3}",
                Ratio, owner.PhaseRatio, owner.CumulativePhaseRatio, NextPhase);

        return false; // let's continue with the phase functions
    }
}
