using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConsumeItem : DoodadPhaseFuncTemplate
{
    public uint ItemId { get; set; }
    public int Count { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncConsumeItem");
        return false;
    }
}
