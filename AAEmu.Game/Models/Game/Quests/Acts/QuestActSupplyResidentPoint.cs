using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds point resident points for zone_group_id (quest_act_supply_resident_points, 41 rows:
/// 40 Auroria territory quests of category 155 over zone groups 33, 34, 43 and 44 giving 10 or 15,
/// and dummy quest 9334). The client reader LoadQuestActSupplyResidentPointDescs (x2game-dev.dll
/// FUN_39d482b0) reads id, point, zone_group_id. The server has no resident progression to add to:
/// HousingManager answers the resident packets with zero points and balances and nothing loads
/// resident_conditions or resident_rewards. The points are 10, 15 or 30, so the rest of the Reward
/// step is worth more than the refusal: the accept is not refused (unlike QuestActSupplyResidentCharge,
/// whose balance is the whole grant) and a quest that reaches this act reports once and completes
/// without the points.
/// </summary>
public class QuestActSupplyResidentPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ZoneGroupId { get; set; }
    public int Point { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no resident points); quest {1} for {2} completes without that reward",
                QuestActTemplateName, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ZoneGroupId {ZoneGroupId}, Point {Point} skipped");
        return true;
    }
}
