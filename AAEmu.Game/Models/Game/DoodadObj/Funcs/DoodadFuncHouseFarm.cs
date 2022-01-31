using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncHouseFarm : DoodadFuncTemplate
    {
        public uint ItemCategoryId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncHouseFarm");
            // owner.Use(caster);
            owner.NeedChangePhase = false;
        }
    }
}
