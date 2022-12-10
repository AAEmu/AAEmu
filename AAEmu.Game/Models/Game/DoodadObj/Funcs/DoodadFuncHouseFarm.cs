using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncHouseFarm : DoodadPhaseFuncTemplate
    {
        public uint ItemCategoryId { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncHouseFarm");
            // owner.Use(caster);
            return false;
        }
    }
}
