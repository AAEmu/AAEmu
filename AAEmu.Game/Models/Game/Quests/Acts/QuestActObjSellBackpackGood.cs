using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: sell count trade packs matching content_item_type / content_item_id to an NPC of
/// quest_monster_group_id (QuestSellBackpackGoodRules). Raised by SpecialtyManager.SellSpecialty
/// after the sale is committed.
/// </summary>
public class QuestActObjSellBackpackGood(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint ContentItemId { get; set; }
    public string ContentItemType { get; set; } = string.Empty;
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, {4} {5} to group {6}, {7}/{8}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, ContentItemType, ContentItemId,
            QuestMonsterGroupId, currentObjectiveCount, Count);
        return QuestProgressActRules.ObjectiveMet(currentObjectiveCount, Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnSellBackpackGood += questAct.OnSellBackpackGood;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnSellBackpackGood -= questAct.OnSellBackpackGood;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnSellBackpackGood(QuestAct questAct, object sender, OnSellBackpackGoodArgs args)
    {
        if (questAct.Template.ActId != ActId)
            return;
        var npcInGroup = QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, args.NpcTemplateId);
        if (!QuestSellBackpackGoodRules.OutletMatches(QuestMonsterGroupId, npcInGroup))
            return;
        if (!QuestSellBackpackGoodRules.ContentMatches(
                ContentItemType, ContentItemId, args.BackpackTemplateId, ItemManager.Instance.HasItemTag))
            return;
        AddObjective(questAct, 1);
    }
}
