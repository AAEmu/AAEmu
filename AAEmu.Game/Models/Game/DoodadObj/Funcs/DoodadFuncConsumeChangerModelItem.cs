using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncConsumeChangerModelItem : DoodadPhaseFuncTemplate
    {
        public uint DoodadFuncConsumeChangerModelId { get; set; }
        public uint ItemId { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncConsumeChangerModelItem");
            return false;
        }
    }
}
