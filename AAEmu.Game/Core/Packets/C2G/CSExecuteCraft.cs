using AAEmu.Commons.Network;
using AAEmu.Game.Core.Helper;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExecuteCraft : GamePacket
    {
        public CSExecuteCraft() : base(0x0f5, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var craftId = stream.ReadUInt32();
            var objId = stream.ReadBc();  // no idea what this one is boys.
            var count = stream.ReadInt32();

            _log.Debug("CSExecuteCraft, craftId : {0} , objId : {1}, count : {2}", craftId, objId, count);
        
        
            /*      tests        */
            Character character = Connection.ActiveChar;
            Craft craft = CraftManager.Instance.GetCraftById(craftId);
            List<CraftMaterial> craftMaterials = CraftManager.Instance.GetMaterialsForCraftId(craftId);
            CraftProduct craftProduct = CraftManager.Instance.GetResultForCraftId(craftId);
            
            //for every item we make
            for (int i = 0; i < count; i++) {
                //check that player has the materials
                bool hasMaterials = true;
                List<Item> materialItems = new List<Item>();
                List<int> materialAmount = new List<int>();
                foreach(CraftMaterial craftMaterial in craftMaterials) {
                    Item materialItem = character.Inventory.GetItemByTemplateId(craftMaterial.ItemId);
                    if (materialItem == null || materialItem.Count < craftMaterial.Amount) {
                        hasMaterials = false;
                        _log.Debug("Player does not have material id {0}", craftMaterial.Id);
                    } else {
                        materialItems.Add(materialItem);
                        materialAmount.Add(craftMaterial.Amount);
                    }
                }

                //if player has the materials, remove them, and add result
                if (hasMaterials) {
                    for(int j = 0; j < materialItems.Count; j++) {
                        InventoryHelper.RemoveItemAndUpdateClient(character, materialItems[j], materialAmount[j]);
                    }

                    Item resultItem = ItemManager.Instance.Create(craftProduct.ItemId, craftProduct.Amount, 0);
                    InventoryHelper.AddItemAndUpdateClient(character, resultItem);
                }
            }
        }
    }
}
