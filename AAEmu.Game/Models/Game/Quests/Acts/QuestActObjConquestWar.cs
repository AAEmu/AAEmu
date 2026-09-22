using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: finish the conquest war of zone_group_id at complete_rank or better. Quests 6572,
/// 9025, 9026, 9027 (zone group 78, rank 4) and 9873 (zone group 20, rank 1). The client reader
/// LoadQuestActObjConquestWarDescs (x2game-dev.dll FUN_39d46e90) reads id, complete_rank,
/// quest_act_obj_alias_id, use_alias, zone_group_id. SiegeManager keeps raid-team scores but has
/// no per-faction war result or ranking to read complete_rank against, so this act keeps the
/// quest open and reports once (QuestUnsupportedProgressActRules).
/// </summary>
public class QuestActObjConquestWar(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public override int Count => 1;
    public uint ZoneGroupId { get; set; }
    public int CompleteRank { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn(
                "{0} is not supported (no conquest war result ranking); quest {1} for {2} stays open at this step",
                QuestActTemplateName, quest.TemplateId, quest.Owner.Name);
        return false;
    }
}
