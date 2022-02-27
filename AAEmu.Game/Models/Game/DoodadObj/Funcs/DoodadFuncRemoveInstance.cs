using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRemoveInstance : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ZoneId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncRemoveInstance, ZoneId: {0}", ZoneId);

        }
    }
}
