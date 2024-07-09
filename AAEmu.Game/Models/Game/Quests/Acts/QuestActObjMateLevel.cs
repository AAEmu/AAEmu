using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMateLevel(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint ItemId { get; set; }
    public byte Level { get; set; }
    public bool Cleanup { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if any of your items is the required summon item, and if it's level has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <returns>Level of the first mate that was valid</returns>
    private byte CalculateObjective(Quest quest)
    {
        // Get a list of all items with the specified template
        if (!quest.Owner.Inventory.GetAllItemsByTemplate(null, ItemId, -1, out var validItems, out _))
        {
            SetObjective(quest, 0);
            return 0;
        }

        foreach (var item in validItems)
        {
            // Sanity check, should already be the correct type
            if (item is not SummonMate summonMate)
                continue;

            // Check level on the Item itself, should be already updated by Mate's AddExp() function
            var res = summonMate.DetailLevel >= Level;
            if (res)
            {
                SetObjective(quest, 1);
                if (Cleanup)
                {
                    // Delete the mate if objective met
                    var removedCount = quest.Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, summonMate.TemplateId, 1,
                        summonMate);
                    if (removedCount < 1)
                    {
                        Logger.Warn($"{QuestActTemplateName}({DetailId}).CalculateObjective: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), failed to remove SummonMate item {summonMate.Id}");
                    }
                }
                return summonMate.DetailLevel;
            }
        }

        SetObjective(quest, 0);
        return 0;
    }

    /// <summary>
    /// Checks if you own a mate of specified type that is at least given Level
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        var res = CalculateObjective(quest);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {res}/{Level}");
        return res > 0;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMateLevelUp += questAct.OnMateLevelUp;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnMateLevelUp -= questAct.OnMateLevelUp;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Mate LevelUp event
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender">Mate</param>
    /// <param name="args"></param>
    public override void OnMateLevelUp(QuestAct questAct, object sender, OnMateLevelUpArgs args)
    {
        if (questAct.Id != ActId)
            return;

        var res = CalculateObjective(questAct.QuestComponent.Parent.Parent);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnMateLevelUp: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), Level {res}/{Level}");
    }
}
