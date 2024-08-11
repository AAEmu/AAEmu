using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncHousingArea : DoodadFuncTemplate
{
    // doodad_funcs
    public FactionsEnum FactionId { get; set; }
    public int Radius { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncHousingArea");

    }
}
