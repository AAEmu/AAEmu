using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSConvertItemLookPacket : GamePacket
{
    public CSConvertItemLookPacket() : base(CSOffsets.CSConvertItemLookPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var baseId = stream.ReadUInt64();
        var lookId = stream.ReadUInt64();

        var character = Connection.ActiveChar;

        var toImage = character.Inventory.GetItemById(baseId);
        var imageItem = character.Inventory.GetItemById(lookId);

        if (toImage is null || imageItem is null)
        {
            character.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            return;
        }

        if (toImage is not EquipItem itemToImage)
        {
            character.SendErrorMessage(ErrorMessageType.ItemLookConvertAsInvalidCombination);
            return;
        }

        if (itemToImage.Template is not EquipItemTemplate template)
        {
            return;
        }

        // Use powder
        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, template.ItemLookConvert.RequiredItemId, template.ItemLookConvert.RequiredItemCount, null) <= 0)
        {
            // Not enough powder
            // Probably not the correct error, but the client should have already caught this
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem, template.ItemLookConvert.RequiredItemId);
            return;
        }

        // Update item looks
        itemToImage.ImageItemTemplateId = imageItem.TemplateId;
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ConvertItemLook, [new ItemUpdate(toImage)], []));

        // Remove image item
        imageItem._holdingContainer.RemoveItem(ItemTaskType.SkillReagents, imageItem, true);
    }
}
