using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: count kills of any NPC in quest_monster_group_id the player contributed to
/// (QuestContributionHuntRules). Hero quest 9118 weighs its four groups by count (30/15/5/1)
/// against quest_contexts.score 100, the same score arithmetic as QuestActObjNpcKill.
/// </summary>
public class QuestActObjMonsterContrGroupHunt(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool LongDist { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, Group {4}, {5}/{6}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, QuestMonsterGroupId,
            currentObjectiveCount, Count);
        return QuestProgressActRules.ObjectiveMet(currentObjectiveCount, Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMonsterContrGroupHunt += questAct.OnMonsterContrGroupHunt;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnMonsterContrGroupHunt -= questAct.OnMonsterContrGroupHunt;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnMonsterContrGroupHunt(QuestAct questAct, object sender, OnMonsterContrGroupHuntArgs args)
    {
        if (questAct.Template.ActId != ActId || args.GroupId != QuestMonsterGroupId)
            return;
        if (!QuestContributionHuntRules.Contributes(LongDist, args.Distance, LootingContainer.MaxLootingRange))
            return;
        AddObjective(questAct, (int)args.Count);
    }
}
