using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: finish the faction competition of zone_group_id at complete_rank or better
/// (use_result 't' on 4 of the 6 rows also asks for the competition result). Quests 9871, 9897,
/// 10569, 10752, 11132, 11133. The act names its competition by <c>zone_group_id</c> (17, 20, 140
/// and 147 in the shipped rows), but no shipped column maps a zone group to a
/// <c>faction_competitions</c> row: that row's <c>detail_id</c> is keyed by <c>detail_type</c>
/// into <c>competition_pvps</c> / <c>competition_pves</c>, whose ids are a bare enumeration and do
/// not name a zone. A competition is also identified by more than its zone — the shipped rows put
/// two different competitions on the same <c>detail_id</c> under different detail types — so
/// neither the zone nor the detail id alone identifies which competition the act means.
/// <para>
/// Scoring itself now runs: <c>FactionCompetitionRuntime</c> owns the scores and resolves the
/// winner and the reset set. What is missing here is only the act's own join, and until the
/// content states it the act keeps its objective slot and reports once
/// (QuestUnsupportedProgressActRules) rather than guessing a competition.
/// </para>
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
                "{0} is not supported (no zone-group to competition join); quest {1} for {2} stays open at this step",
                QuestActTemplateName, quest.TemplateId, quest.Owner.Name);
        return false;
    }
}
