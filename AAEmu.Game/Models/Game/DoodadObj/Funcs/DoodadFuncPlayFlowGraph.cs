using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPlayFlowGraph : DoodadFuncTemplate
    {
        public uint EventOnPhaseChangeId { get; set; }
        public uint EventOnVisibleId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncPlayFlowGraph");
        }
    }
}
