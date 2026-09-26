using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: finish the faction competition of zone_group_id at complete_rank or better
/// (use_result 't' on 4 of the 6 rows also asks for the competition result). Quests 9871, 9897,
/// 10569, 10752, 11132, 11133. The act metadata is loaded, but the server has no faction
/// competition scoring: no score state is applied, nothing sends
/// <c>SCFactionCompetitionUpdatePointPacket</c>, and <c>SpecialEffectType.GiveFactionCompetitionPoint</c>
/// has no handler. Until that subsystem exists this act keeps the quest open and reports once
/// (QuestUnsupportedProgressActRules).
/// </summary>
public class QuestActObjFactionCompetition(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public override int Count => 1;
    public uint ZoneGroupId { get; set; }
    public int CompleteRank { get; set; }
    public bool UseResult { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn(
                "{0} is not supported (no faction competition scoring); quest {1} for {2} stays open at this step",
                QuestActTemplateName, quest.TemplateId, quest.Owner.Name);
        return false;
    }
}
