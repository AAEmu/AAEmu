using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

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

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        questAct.SetObjective(quest, quest.Owner.Inventory.GetItemsCount(ItemId));

        // Register Handler if not at max yet
        if (questAct.GetObjective(quest) < MaxObjective())
            quest.Owner.Events.OnItemGather += questAct.OnItemGather;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        base.FinalizeAction(quest, questAct);

        // Un-register event handler
        quest.Owner.Events.OnItemGather -= questAct.OnItemGather;
    }
}
