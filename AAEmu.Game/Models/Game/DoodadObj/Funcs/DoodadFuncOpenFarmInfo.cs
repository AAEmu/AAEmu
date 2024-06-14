using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncOpenFarmInfo : DoodadFuncTemplate
{
    // doodad_funcs
    public uint FarmId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Info("DoodadFuncOpenFarmInfo");
        owner.ToNextPhase = true;
    }
}
