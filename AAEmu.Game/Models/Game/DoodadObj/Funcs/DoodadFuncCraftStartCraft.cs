using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCraftStartCraft : DoodadPhaseFuncTemplate
    {
        public uint DoodadFuncCraftStartId { get; set; }
        public uint CraftId { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncCraftStartCraft");
            return false;
        }
    }
}
