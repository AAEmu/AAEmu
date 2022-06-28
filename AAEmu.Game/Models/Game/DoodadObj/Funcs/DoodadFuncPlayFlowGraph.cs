using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPlayFlowGraph : DoodadPhaseFuncTemplate
    {
        public uint EventOnPhaseChangeId { get; set; }
        public uint EventOnVisibleId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncPlayFlowGraph");
            return false;
        }
    }
}
