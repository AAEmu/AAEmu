using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCutdowning : DoodadFuncTemplate
    {
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {

            //TODO Cut down effect goes here
            var nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            owner.FuncGroupId = (uint)nextfunc.NextPhase; 
            //^ Note, we are looking for the next next func since there was no phase change by this point
            DoodadManager.Instance.TriggerActionFunc(GetType().Name, caster, owner, skillId);
        }
    }
}
