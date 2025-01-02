using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Skinize : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Skinize;

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
            Logger.Debug("Special effects: Skinize value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
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

        var itemToImage = character.Inventory.GetItemById(itemTarget.Id);
        if (itemToImage == null)
        {
            skill.Cancelled = true;
            return;
        }

        if (itemToImage.HasFlag(ItemFlag.Skinized))
        {
            // Already an image item
            skill.Cancelled = true;
            // TODO: Get the correct error as this is likely not correct
            character.SendErrorMessage(ErrorMessageType.ItemLookConvertAsNotUseAsSkin);
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

        itemToImage.SetFlag(ItemFlag.Skinized);
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Skinize, [new ItemUpdateBits(itemToImage)], []));
        powderItem._holdingContainer.ConsumeItem(ItemTaskType.Skinize, powderItem.TemplateId, 1, powderItem);
    }
}
