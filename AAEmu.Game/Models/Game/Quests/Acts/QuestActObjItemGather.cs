using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Static;
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

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActObjItemGather: ItemId {ItemId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}, Score {ParentQuestTemplate.Score}");

        var res = quest.GetQuestObjectiveStatus() >= QuestObjectiveStatus.CanEarlyComplete;
        var maxCleanup = ParentQuestTemplate.LetItDone ? Count * 3 / 2 : Count;

        Update(quest, questAct, objective);

        if (res && Cleanup)
            quest.QuestCleanupItemsPool.Add(new ItemCreationDefinition(ItemId, Math.Min(maxCleanup, objective)));

        return res;
    }

    public override void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        base.Update(quest, questAct, updateAmount);
        // Objective count is already set by CheckAct
        Logger.Info($"{QuestActTemplateName} - QuestActItemGather {DetailId} was updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }

    /// <summary>
    /// Checks if the number of items have been acquired 
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, Count {currentObjectiveCount}/{Count}");
        SetObjective(quest, quest.Owner.Inventory.GetItemsCount(ItemId));
        return GetObjective(quest) >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        questAct.SetObjective(quest, quest.Owner.Inventory.GetItemsCount(ItemId));

        // Register event handler
        quest.Owner.Events.OnItemGather += questAct.OnItemGather;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
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

        quest.Owner?.Inventory.ConsumeItem([], ItemTaskType.QuestRemoveSupplies, ItemId, MaxObjective(), null);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (!DestroyWhenDrop)
            return;

        quest.Owner?.Inventory.ConsumeItem([], ItemTaskType.QuestRemoveSupplies, ItemId, MaxObjective(), null);
    }

    public override void OnItemGather(IQuestAct questAct, object sender, OnItemGatherArgs args)
    {
        if ((questAct.Id != ActId) || (args.ItemId != ItemId))
            return;

        // Just adding/removing the count should technically be enough without having to do a new count
        // AddObjective(questAct, args.Count);
        SetObjective(questAct, questAct.QuestComponent.Parent.Parent.Owner.Inventory.GetItemsCount(ItemId));
    }
}
