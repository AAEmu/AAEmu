using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncBuyFishItem : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public uint DoodadFuncBuyFishId { get; set; }
        public uint ItemId { get; set; }
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncBuyFishItem");
            return false;
        }
    }
}
