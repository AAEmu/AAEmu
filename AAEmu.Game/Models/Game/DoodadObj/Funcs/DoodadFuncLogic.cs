using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLogic : DoodadPhaseFuncTemplate
    {
        public uint OperationId { get; set; }
        public uint DelayId { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncLogic");
            return false;
        }
    }
}
