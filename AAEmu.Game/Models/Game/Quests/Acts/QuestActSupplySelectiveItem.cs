using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplySelectiveItem : QuestActTemplate
{
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActSupplySelectiveItem, ItemId: {ItemId}, Count: {Count}, GradeId: {GradeId}");

        quest.QuestRewardItemsPool.Add(new ItemCreationDefinition(ItemId, Count, GradeId));
        // the item will be added in the DistributeRewards() method

        return true;
    }
}
