using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
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

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyItem");

            if (objective >= Count) // checking for call recursion
            {
                return true;
            }

            var acquireSuccessful = false;
            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                acquireSuccessful = character.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
            }
            else
            {
                acquireSuccessful = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, ItemId, Count, GradeId);
            }

            if (!acquireSuccessful)
            {
                var amountFree = character.Inventory.Bag.SpaceLeftForItem(ItemId);
                if (amountFree < Count)
                {
                    character.SendErrorMessage(ErrorMessageType.BagFull);
                }
            }

            return acquireSuccessful;
        }
    }
}
