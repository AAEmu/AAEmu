using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRatioChange : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public int Ratio { get; set; }
        public int NextPhase { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            if (owner.PhaseRatio + owner.CumulativePhaseRatio <= Ratio)
            {
                owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded
                if (caster is Character)
                    _log.Debug($"DoodadFuncRatioChange : Ratio {Ratio}, PhaseRatio {owner.PhaseRatio + owner.CumulativePhaseRatio}, OverridePhase {NextPhase}", Ratio, NextPhase);
                else
                    _log.Trace($"DoodadFuncRatioChange : Ratio {Ratio}, PhaseRatio {owner.PhaseRatio + owner.CumulativePhaseRatio}, OverridePhase {NextPhase}", Ratio, NextPhase);
                return true; // it is necessary to interrupt the phase functions and switch to NextPhase
            }
            if (caster is Character)
                _log.Debug($"DoodadFuncRatioChange : Ratio {Ratio}, PhaseRatio {owner.PhaseRatio + owner.CumulativePhaseRatio}, NextPhase {NextPhase}", Ratio, owner.FuncGroupId);
            else
                _log.Trace($"DoodadFuncRatioChange : Ratio {Ratio}, PhaseRatio {owner.PhaseRatio + owner.CumulativePhaseRatio}, NextPhase {NextPhase}", Ratio, owner.FuncGroupId);

            owner.CumulativePhaseRatio += Ratio;
            return false; // let's continue with the phase functions
        }
    }
}
