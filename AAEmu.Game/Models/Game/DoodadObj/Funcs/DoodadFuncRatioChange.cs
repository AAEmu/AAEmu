
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
            _log.Trace("DoodadFuncRatioChange : Ratio {0}, NextPhase {1}", Ratio, NextPhase);

            if (owner.PhaseRatio + owner.CumulativePhaseRatio > Ratio)
            {
                owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded
                _log.Trace("DoodadFuncRatioChange : OverridePhase {0}", NextPhase);
    
                return true; // необходимо прервать фазовые функции и перейти на NextPhase
            }
            _log.Trace("DoodadFuncRatioChange : NextPhase {0}", NextPhase);
            owner.CumulativePhaseRatio += Ratio;
            return false; // продолжим выполнять фазовые функции
        }
    }
}
