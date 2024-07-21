using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class RecoverItem : IWorldInteraction
{
    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
    {
        // check if you are equipped with a backpack or glider
        var hasBackPack = !((Character)caster).Inventory.CanReplaceGliderInBackpackSlot();

        if (target is Doodad doodad && doodad.AllowRemoval() && !hasBackPack)
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
        ((Unit)caster).SendErrorMessage(ErrorMessageType.FailedToUseItem);
        caster.InterruptSkills();
    }
}
