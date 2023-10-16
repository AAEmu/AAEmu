using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncPlayFlowGraph : DoodadPhaseFuncTemplate
{
    public uint EventOnPhaseChangeId { get; set; }
    public uint EventOnVisibleId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncPlayFlowGraph");
        return false;
    }
}
