using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: count kills of npc_id the player contributed to (QuestContributionHuntRules).
/// </summary>
public class QuestActObjMonsterContrHunt(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool LongDist { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, Npc {4}, {5}/{6}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, NpcId, currentObjectiveCount, Count);
        return QuestProgressActRules.ObjectiveMet(currentObjectiveCount, Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMonsterContrHunt += questAct.OnMonsterContrHunt;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnMonsterContrHunt -= questAct.OnMonsterContrHunt;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnMonsterContrHunt(QuestAct questAct, object sender, OnMonsterContrHuntArgs args)
    {
        if (questAct.Template.ActId != ActId || args.NpcId != NpcId)
            return;
        if (!QuestContributionHuntRules.Contributes(LongDist, args.Distance, LootingContainer.MaxLootingRange))
            return;
        AddObjective(questAct, (int)args.Count);
    }
}
