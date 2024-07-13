using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuildConditionInfo : DoodadPhaseFuncTemplate
{
    public bool IsDevote { get; set; }
    public bool IsEnd { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncBuildConditionInfo: IsDevote={IsDevote}, IsEnd={IsEnd}");
        return false;
    }
}
