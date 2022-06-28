using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncInsertCounter : DoodadFuncTemplate
    {
        // doodad_funcs
        public int Count { get; set; }
        public uint ItemId { get; set; }
        public int ItemCount { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncInsertCounter");

        }
    }
}
