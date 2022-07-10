using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCoffer : DoodadFuncTemplate
    {
        public int Capacity { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncCoffer");
            owner.ToPhaseAndUse = false;
            if ((caster is Character character) && (owner is DoodadCoffer coffer))
                if (coffer.OpenedBy?.Id == character.Id)
                    DoodadManager.Instance.CloseCofferDoodad(character, owner.ObjId);
                else
                    DoodadManager.Instance.OpenCofferDoodad(character, owner.ObjId);
        }
    }
}
