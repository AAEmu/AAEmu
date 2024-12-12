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
        // TODO ...
        if (caster is Character)
        {
            Logger.Debug("Special effects: Skinize value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }

        if (caster is not Character character || character is null)
        {
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget || itemTarget is null)
        {
            return;
        }

        var itemToImage = character.Inventory.GetItemById(itemTarget.Id);
        if (itemToImage == null)
        {
            return;
        }

        if (itemToImage.HasFlag(ItemFlag.Skinized))
        {
            // Already a image item
            return;
        }

        if (casterObj is not SkillItem powderSkillItem || powderSkillItem is null)
        {
            return;
        }

        Item powderItem = character.Inventory.GetItemById(powderSkillItem.ItemId);
        if (powderItem == null)
        {
            return;
        }

        if (powderItem.Count < 1)
        {
            return;
        }

        itemToImage.SetFlag(ItemFlag.Skinized);
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Sknize, [new ItemUpdateBits(itemToImage)], []));
        powderItem._holdingContainer.ConsumeItem(ItemTaskType.Sknize, powderItem.TemplateId, 1, powderItem);
    }
}
