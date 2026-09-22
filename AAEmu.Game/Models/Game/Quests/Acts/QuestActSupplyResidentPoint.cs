using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds point resident points for zone_group_id (quest_act_supply_resident_points, 41 rows:
/// 40 Auroria territory quests of category 155 over zone groups 33, 34, 43 and 44 giving 10 or 15,
/// and dummy quest 9334). The client reader LoadQuestActSupplyResidentPointDescs (x2game-dev.dll
/// FUN_39d482b0) reads id, point, zone_group_id. The server has no resident progression to add to:
/// HousingManager answers the resident packets with zero points and balances and nothing loads
/// resident_conditions or resident_rewards, so the accept is refused (QuestRewardSupportRules) and
/// a quest that still reaches this act reports once and completes without the points.
/// </summary>
public class QuestActSupplyResidentPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IUnsupportedRewardAct
{
    public uint ZoneGroupId { get; set; }
    public int Point { get; set; }

    public string MissingSubsystem => "resident points";

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no {1}); quest {2} for {3} completes without that reward",
                QuestActTemplateName, MissingSubsystem, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ZoneGroupId {ZoneGroupId}, Point {Point} skipped");
        return true;
    }
}
