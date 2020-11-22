using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCutdowning : DoodadFuncTemplate
    {
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            //TODO Tree falling effect goes here?
            // DoodadManager.Instance.TriggerFunc(GetType().Name, caster, owner, skillId);
            owner.Use(caster, skillId);
        }
    }
}
