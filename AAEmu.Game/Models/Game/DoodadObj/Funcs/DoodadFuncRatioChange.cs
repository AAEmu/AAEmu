
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRatioChange : DoodadFuncTemplate
    {
        public int Ratio { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncRatioChange : Ratio {0}, NextPhase {1}, SkillId {2}", Ratio, NextPhase, skillId);

            if (owner.PhaseRatio + owner.CumulativePhaseRatio <= Ratio)
            {
                owner.OverridePhase = NextPhase; //Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded
                _log.Debug("DoodadFuncRatioChange : OverridePhase {0}", NextPhase);
                owner.ToPhaseAndUse = true;
                return;
            }
            _log.Debug("DoodadFuncRatioChange : NextPhase {0}", NextPhase);
            owner.CumulativePhaseRatio += Ratio;
            owner.ToPhaseAndUse = false;
        }
    }
}
