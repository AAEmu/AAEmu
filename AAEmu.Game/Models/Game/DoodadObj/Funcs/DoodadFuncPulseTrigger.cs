using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPulseTrigger : DoodadPhaseFuncTemplate
    {
        public bool Flag { get; set; }
        public int NextPhase { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncPulseTrigger");
            if (Flag)
            {
                owner.OverridePhase = NextPhase;
                return true;
            }

            return false;
        }
    }
}
