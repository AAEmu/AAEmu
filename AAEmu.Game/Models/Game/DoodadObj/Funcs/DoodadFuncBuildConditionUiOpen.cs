using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuildConditionUiOpen : DoodadFuncTemplate
{
    // doodad_funcs

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Debug("DoodadFuncBuildConditionUiOpen");

    }
}
