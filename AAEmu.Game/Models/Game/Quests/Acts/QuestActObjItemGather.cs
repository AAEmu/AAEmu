using System;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGather(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IQuestActGenericItem // Сбор предметов
{
    public uint ItemId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    /// <summary>
    /// Checks if the number of items have been acquired 
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, Count {currentObjectiveCount}/{Count}");
        SetObjective(quest, quest.Owner.Inventory.GetItemsCount(ItemId));
        return GetObjective(quest) >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        SetObjective(quest, quest.Owner.Inventory.GetItemsCount(ItemId));

        // Register event handler
        quest.Owner.Events.OnItemGather += questAct.OnItemGather;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        base.FinalizeAction(quest, questAct);

        // Un-register event handler
        quest.Owner.Events.OnItemGather -= questAct.OnItemGather;
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (!Cleanup)
            return;

        // quest.Owner?.Inventory.ConsumeItem([], ItemTaskType.QuestRemoveSupplies, ItemId, MaxObjective(), null);
        var cleanupCount = Math.Min(GetObjective(quest), MaxObjective());
        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, cleanupCount, null);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (!DestroyWhenDrop)
            return;

        var cleanupCount = Math.Min(GetObjective(quest), MaxObjective());
        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, cleanupCount, null);
    }

    public override void OnItemGather(QuestAct questAct, object sender, OnItemGatherArgs args)
    {
        if ((questAct.Id != ActId) || (args.ItemId != ItemId))
            return;

        // Just adding/removing the count should technically be enough without having to do a new count
        // AddObjective(questAct, args.Count, Count);
        SetObjective((QuestAct)questAct, questAct.QuestComponent.Parent.Parent.Owner.Inventory.GetItemsCount(ItemId));
    }
}
