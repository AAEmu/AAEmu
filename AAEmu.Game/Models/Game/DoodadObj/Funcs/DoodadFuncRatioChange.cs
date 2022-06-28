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
            if (caster is Character)
                _log.Debug("DoodadFuncRatioChange : Ratio {0}, NextPhase {1}", Ratio, NextPhase);
            else
                _log.Trace("DoodadFuncRatioChange : Ratio {0}, NextPhase {1}", Ratio, NextPhase);

            if (owner.PhaseRatio + owner.CumulativePhaseRatio <= Ratio)
            {
                owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded
                if (caster is Character)
                    _log.Debug("DoodadFuncRatioChange : OverridePhase {0}", NextPhase);
                else
                    _log.Trace("DoodadFuncRatioChange : OverridePhase {0}", NextPhase);

                return true; // необходимо прервать фазовые функции и перейти на NextPhase
            }
            if (caster is Character)
                _log.Debug("DoodadFuncRatioChange : NextPhase {0}", NextPhase);
            else
                _log.Trace("DoodadFuncRatioChange : NextPhase {0}", NextPhase);

            //owner.CumulativePhaseRatio += Ratio; // мне кажется не надо увеличивать вероятность
            return false; // продолжим выполнять фазовые функции
        }
    }
}
