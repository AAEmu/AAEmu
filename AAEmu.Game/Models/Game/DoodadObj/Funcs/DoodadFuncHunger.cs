using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncHunger : DoodadPhaseFuncTemplate
    {
        public int HungryTerm { get; set; }
        public int FullStep { get; set; }
        public int PhaseChangeLimit { get; set; }
        public int NextPhase { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncHunger");
            if (caster is Character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime
                owner.OverridePhase = NextPhase;
                return true;
            }

            return false;
        }
    }
}
