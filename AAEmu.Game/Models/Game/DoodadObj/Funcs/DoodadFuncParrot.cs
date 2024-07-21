using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncParrot : DoodadPhaseFuncTemplate
{
    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncParrot");
        return false;
    }
}
