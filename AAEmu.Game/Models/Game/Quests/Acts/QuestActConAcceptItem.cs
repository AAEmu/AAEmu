using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptItem : QuestActTemplate, IQuestActGenericItem
{
    public uint ItemId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective) // triggered by using things
    {
        Logger.Debug($"QuestActConAcceptItem: ItemId {ItemId}");

        quest.QuestAcceptorType = QuestAcceptorType.Item;
        quest.AcceptorType = ItemId;

        return character.Inventory.CheckItems(Items.SlotType.Inventory, ItemId, 1);
    }
}
