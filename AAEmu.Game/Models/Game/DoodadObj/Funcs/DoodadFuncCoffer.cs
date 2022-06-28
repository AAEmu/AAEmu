using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCoffer : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public int Capacity { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncCoffer");
            return false;
        }
    }
}
