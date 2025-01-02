using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CleanupLookConvert : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.CleanupLookConvert;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Character)
        {
            Logger.Debug("Special effects: CleanupLookConvert value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }

        if (caster is not Character character)
        {
            skill.Cancelled = true;
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            skill.Cancelled = true;
            return;
        }

        var itemWithImage = character.Inventory.GetItemById(itemTarget.Id);
        if (itemWithImage == null)
        {
            skill.Cancelled = true;
            return;
        }

        if (itemWithImage.ImageItemTemplateId <= 0)
        {
            // Is not an item with an image on it
            skill.Cancelled = true;
            // TODO: Get the correct error message
            character.SendErrorMessage(ErrorMessageType.ItemLookConvertAsInvalidCombination);
            // character.SendErrorMessage(ErrorMessageType.FailedToChangeItemLook);
            return;
        }

        if (casterObj is not SkillItem powderSkillItem)
        {
            skill.Cancelled = true;
            return;
        }

        var powderItem = character.Inventory.GetItemById(powderSkillItem.ItemId);
        if (powderItem == null)
        {
            skill.Cancelled = true;
            return;
        }

        if (powderItem.Count < 1)
        {
            skill.Cancelled = true;
            return;
        }

        itemWithImage.ImageItemTemplateId = 0;
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ConvertItemLook, [new ItemUpdate(itemWithImage)], []));
        powderItem._holdingContainer.ConsumeItem(ItemTaskType.ConvertItemLook, powderItem.TemplateId, 1, powderItem);
    }
}
