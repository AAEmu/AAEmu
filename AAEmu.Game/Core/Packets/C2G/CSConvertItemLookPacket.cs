using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
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

        Item toImage = character.Inventory.GetItemById(baseId);
        Item imageItem = character.Inventory.GetItemById(lookId);

        if (toImage is null || imageItem is null)
            return;

        if (toImage is not EquipItem itemToImage)
            return;

        if (itemToImage.Template is not EquipItemTemplate template)
            return;

        // Use powder
        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, template.ItemLookConvert.RequiredItemId, template.ItemLookConvert.RequiredItemCount, null) <= 0)
        {
            // Not enough powder
            return;
        }

        // Update item looks
        itemToImage.ImageItemTemplateId = imageItem.TemplateId;
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, [new ItemUpdate(toImage)], []));

        // Remove image item
        imageItem._holdingContainer.RemoveItem(ItemTaskType.ConvertItemLook, imageItem, true);
    }
}
