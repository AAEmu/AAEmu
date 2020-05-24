using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConvertItemLookPacket : GamePacket
    {
        public CSConvertItemLookPacket() : base(0x049, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var baseId = stream.ReadUInt64();
            var lookId = stream.ReadUInt64();

            var character = Connection.ActiveChar;

            Item toImage = character.Inventory.GetItemById(baseId);
            Item imageItem = character.Inventory.GetItemById(lookId);

            if (toImage == null || imageItem == null) return;

            if ((!(toImage is EquipItem itemToImage)) || (itemToImage == null))
                return;

            if ((!(itemToImage.Template is EquipItemTemplate template)) || (template == null))
                return;

            // Use powder
            if (character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, template.ItemLookConvert.RequiredItemId, template.ItemLookConvert.RequiredItemCount, null) <= 0)
            {
                // Not enough powder
                return;
            }

            // Update item looks
            itemToImage.ImageItemTemplateId = imageItem.TemplateId;
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, new List<ItemTask>() { new ItemUpdate(toImage) }, new List<ulong>()));

            // Remove image item
            imageItem._holdingContainer.RemoveItem(ItemTaskType.ConvertItemLook, imageItem, true);
        }
    }
}
