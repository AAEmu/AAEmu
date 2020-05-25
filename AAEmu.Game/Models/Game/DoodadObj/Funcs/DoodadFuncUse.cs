using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {

            var func = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            if (func.NextPhase > 0)
            {
                //_log.Warn("Current Phase: " + owner.FuncGroupId + ", Next Phase: " + func.NextPhase);
                owner.FuncGroupId = (uint)func.NextPhase;
                DoodadManager.Instance.TriggerActionFunc(GetType().Name, caster, owner, skillId);
            }
        }
    }
}
