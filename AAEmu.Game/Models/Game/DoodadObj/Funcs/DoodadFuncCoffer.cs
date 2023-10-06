using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncCoffer : DoodadPhaseFuncTemplate
{
    // doodad_phase_funcs
    public int Capacity { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug("DoodadFuncCoffer");
        owner.ToNextPhase = false;
        if ((caster is Character character) && (owner is DoodadCoffer coffer))
            if (coffer.OpenedBy?.Id == character.Id)
                DoodadManager.CloseCofferDoodad(character, owner.ObjId);
            else
                DoodadManager.OpenCofferDoodad(character, owner.ObjId);
        Logger.Trace("DoodadFuncCoffer");
        return false;
    }
}
