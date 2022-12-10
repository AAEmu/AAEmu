using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class RecoverItem : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            if ((target is Doodad doodad) && doodad.AllowRemoval())
            {
                // Get Funcs for current doodad phase
                var funcs = DoodadManager.Instance.GetFuncsForGroup(doodad.FuncGroupId);
                // Check if it contains a DoodadRecoverItem func
                foreach (var func in funcs)
                {
                    var template = DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType);
                    if (template is DoodadFuncRecoverItem doodadFuncRecoverItemTemplate)
                    {
                        // Execute DoodadFuncRecoverItem
                        doodadFuncRecoverItemTemplate.Use(caster, doodad, skillId);
                        // Move to next phase to remove the doodad
                        //doodad.DoPhaseFuncs(caster, -1);
                        doodad.Delete();
                        return;
                    }
                }
            }

            // Something wasn't found or is invalid, so cancel whatever we're doing
            caster.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            caster.InterruptSkills();
        }
    }
}
