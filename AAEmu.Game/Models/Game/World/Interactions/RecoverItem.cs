using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class RecoverItem : IWorldInteraction
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
                    var itemId = doodad.ItemId;
                    var itemTemplateId = doodad.ItemTemplateId;
                    var item = ItemManager.Instance.GetItemByItemId(itemId);
                    // Execute DoodadFuncRecoverItem
                    doodad.ToNextPhase = false;
                    doodadFuncRecoverItemTemplate.Use(caster, doodad, skillId);
                    if (doodad.ToNextPhase)
                    {
                        if (item != null && doodad.IsPersistent && !DoodadItemPersistence.TrySaveRecovery(item, doodad))
                        {
                            if (!((Character)caster).Inventory.SystemContainer.AddOrMoveExistingItem(
                                ItemTaskType.RecoverDoodadItem,
                                item))
                            {
                                Logger.Error("Failed to restore item {0} to its system container after recovery persistence failed", item.Id);
                            }

                            doodad.ItemId = itemId;
                            doodad.ItemTemplateId = itemTemplateId;
                            doodad.ToNextPhase = false;
                            break;
                        }

                        doodad.Delete();
                        return;
                    }

                    break;
                }
            }
        }

        // Something wasn't found or is invalid, so cancel whatever we're doing
        ((Unit)caster).SendErrorMessage(ErrorMessageType.FailedToUseItem);
        caster.InterruptSkills();
    }
}
