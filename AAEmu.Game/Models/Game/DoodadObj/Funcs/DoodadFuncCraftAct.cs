using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncCraftAct : DoodadFuncTemplate
{
    public string Model20 { get; set; }
    public string Model80 { get; set; }
    public string Model60 { get; set; }
    public string Model40 { get; set; }
    // doodad_funcs
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncCraftAct");

    }
}
