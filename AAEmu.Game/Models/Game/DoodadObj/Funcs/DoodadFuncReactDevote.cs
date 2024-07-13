using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncReactDevote : DoodadPhaseFuncTemplate
{
    public uint Count { get; set; }
    public int NextPhase { get; set; }
    public uint SkillId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncReactDevote: Count={Count}, NextPhase={NextPhase}, SkillId={SkillId}");
        return false;
    }
}
