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

            Item toImage = character.Inventory.GetItem(baseId);
            Item imageItem = character.Inventory.GetItem(lookId);

            if (toImage == null || imageItem == null) return;

            EquipItem itemToImage = (EquipItem)toImage;

            if (itemToImage == null) return;

            EquipItemTemplate template = (EquipItemTemplate)itemToImage.Template;
            if (template == null) return;

            if (!character.Inventory.CheckItems(template.ItemLookConvert.RequiredItemId, template.ItemLookConvert.RequiredItemCount)) return;
            Item powder = character.Inventory.GetItemByTemplateId(template.ItemLookConvert.RequiredItemId);

            itemToImage.ImageItemTemplateId = imageItem.TemplateId;

            var removeItemTasks = new List<ItemTask>();
            var updateItemTasks = new List<ItemTask>();

            updateItemTasks.Add(new ItemUpdate(toImage));
            removeItemTasks.Add(InventoryHelper.GetTaskAndRemoveItem(character, imageItem, 1));
            removeItemTasks.Add(InventoryHelper.GetTaskAndRemoveItem(character, powder, template.ItemLookConvert.RequiredItemCount));

            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, removeItemTasks, new List<ulong>()));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ConvertItemLook, updateItemTasks, new List<ulong>()));
        }
    }
}
