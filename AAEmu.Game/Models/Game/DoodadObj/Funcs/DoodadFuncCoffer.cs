using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCoffer : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public int Capacity { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Debug("DoodadFuncCoffer");
            owner.ToNextPhase = false;
            if ((caster is Character character) && (owner is DoodadCoffer coffer))
                if (coffer.OpenedBy?.Id == character.Id)
                    DoodadManager.Instance.CloseCofferDoodad(character, owner.ObjId);
                else
                    DoodadManager.Instance.OpenCofferDoodad(character, owner.ObjId);
            _log.Trace("DoodadFuncCoffer");
            return false;
        }
    }
}
