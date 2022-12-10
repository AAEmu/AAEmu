using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class CheckGrowth : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            // TODO Verification Needed
            if (!(target is Doodad doodad)) { return; }

            var func = DoodadManager.Instance.GetFunc(doodad.FuncGroupId, skillId);
            if (func == null) { return; }

            var grp = func.GroupId;
            func.Use(caster, doodad, skillId);

            var nextFunc = DoodadManager.Instance.GetFunc(doodad.FuncGroupId, skillId);
            if (nextFunc?.NextPhase == grp || nextFunc?.NextPhase == -1) { return; }

            nextFunc?.Use(caster, doodad, skillId);
        }
    }
}
