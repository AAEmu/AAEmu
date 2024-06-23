using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplySelectiveItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }

    /// <summary>
    /// Does a selective item reward
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>Always returns true to allow progress even if this isn't the selected reward</returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, Count {Count}, GradeId {GradeId}, Selected {quest.SelectedRewardIndex}, This {ThisSelectiveIndex}");

        // Only add reward if it was this selection
        if (quest.SelectedRewardIndex == ThisSelectiveIndex)
            quest.QuestRewardItemsPool.Add(new ItemCreationDefinition(ItemId, Count, GradeId));

        return true;
    }
}
