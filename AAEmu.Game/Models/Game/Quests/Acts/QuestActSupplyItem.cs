using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Quests.Templates;


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
            _log.Warn("QuestActSupplyItem");
            if (objective >= Count)
                return true;
            else
            {
                if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
                {
                    return character.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
                }
                else
                {
                    return character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
                }
                /*
                var template = ItemManager.Instance.GetTemplate(ItemId);
                if (template is BackpackTemplate backpackTemplate)
                {
                    if (character.Inventory.TakeoffBackpack(ItemTaskType.QuestSupplyItems, true))
                        return character.Inventory.Equipment.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
                    else
                        return false;
                }
                else
                {
                    return character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
                }
                */

            }
        }
    }
}
