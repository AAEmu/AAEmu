using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyItem : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public byte GradeId { get; set; }
        public bool ShowActionBar { get; set; }
        public bool Cleanup { get; set; }
        public bool DropWhenDestroy { get; set; }
        public bool DestroyWhenDrop { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyItem"); // TODO add item
            var tasks = new List<ItemTask>();
            var item = ItemManager.Instance.Create(ItemId, Count,  GradeId);
            //character.Inventory.AddItem(item);

            //InventoryHelper.AddItemAndUpdateClient(character, item);
              var res = character.Inventory.AddItem(item);
             if (res == null)
             {
                 ItemIdManager.Instance.ReleaseId((uint)item.Id);
              }

              if (res.Id != item.Id)
                  tasks.Add(new ItemCountUpdate(res, item.Count));
              else
                 tasks.Add(new ItemAdd(item));

              //tasks.Add(new ItemAdd(item));


              character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.QuestSupplyItems, tasks , new List<ulong>()));



            return false;
        }
    }
}
