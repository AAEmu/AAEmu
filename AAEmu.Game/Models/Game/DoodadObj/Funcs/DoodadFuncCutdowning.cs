using AAEmu.Game.Models.Game.Char;

using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncCutdowning : DoodadFuncTemplate
{
    // doodad_funcs
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character)
            Logger.Debug("DoodadFuncCutdowning");
        else
            Logger.Trace("DoodadFuncCutdowning");

        // Cutdown.Execute emits SCVegetationCutdowning after this function advances the
        // doodad state. Do not send it here as well: that would play the fall twice.

        owner.ToNextPhase = true;
    }
}
