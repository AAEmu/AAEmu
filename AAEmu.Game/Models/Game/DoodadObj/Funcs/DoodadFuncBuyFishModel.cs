using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncBuyFishModel : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public string Name { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncBuyFishModel");
            return false;
        }
    }
}
