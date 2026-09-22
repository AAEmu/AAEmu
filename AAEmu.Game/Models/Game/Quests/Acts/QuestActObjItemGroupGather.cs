using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: hold count items of quest item group item_group_id. Same absolute bag count as
/// QuestActObjItemGather, summed over the group (QuestItemGroupGatherRules).
/// </summary>
public class QuestActObjItemGroupGather(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint ItemGroupId { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }
    /// <summary>check_exist is 'f' on all 56 rows; loaded and not acted on, as QuestActObjItemGather does.</summary>
    public bool CheckExist { get; set; }

    private int CountInBag(Quest quest)
        => QuestItemGroupGatherRules.CountInGroup(
            QuestManager.Instance.GetGroupItems(ItemGroupId),
            itemId => quest.Owner.Inventory.GetItemsCount(itemId));

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, Group {4}, {5}/{6}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, ItemGroupId, currentObjectiveCount, Count);
        SetObjective(quest, CountInBag(quest));
        return QuestProgressActRules.ObjectiveMet(GetObjective(quest), Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        SetObjective(quest, CountInBag(quest));
        quest.Owner.Events.OnItemGroupGather += questAct.OnItemGroupGather;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnItemGroupGather -= questAct.OnItemGroupGather;
        base.FinalizeAction(quest, questAct);
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (Cleanup)
            RemoveGatheredItems(quest);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (DestroyWhenDrop)
            RemoveGatheredItems(quest);
    }

    public override void OnItemGroupGather(QuestAct questAct, object sender, OnItemGroupGatherArgs args)
    {
        if (questAct.Template.ActId != ActId || args.ItemGroupId != ItemGroupId)
            return;
        SetObjective(questAct, CountInBag(questAct.QuestComponent.Parent.Parent));
    }

    private void RemoveGatheredItems(Quest quest)
    {
        if (quest?.Owner == null)
            return;
        var plan = QuestItemGroupGatherRules.CleanupPlan(
            QuestManager.Instance.GetGroupItems(ItemGroupId),
            itemId => quest.Owner.Inventory.GetItemsCount(itemId),
            Math.Min(GetObjective(quest), MaxObjective()));
        foreach (var (itemId, count) in plan)
            quest.Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, itemId, count, null);
    }
}
