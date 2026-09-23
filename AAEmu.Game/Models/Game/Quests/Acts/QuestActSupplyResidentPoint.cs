using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds resident points for zone_group_id (quest_act_supply_resident_points, 41 rows:
/// 40 Auroria territory quests of category 155 over zone groups 33, 34, 43 and 44 giving 10 or 15,
/// and dummy quest 9334). The client reader LoadQuestActSupplyResidentPointDescs reads
/// id, point, zone_group_id. The points settle into <c>character_resident_state</c> through the
/// ResidentManager — a server-originated contribution, so unlike the client packet it needs no
/// residency gate. Zone groups without a <c>local_developments</c> row still settle; the
/// development phase step skips loudly for them.
/// </summary>
public class QuestActSupplyResidentPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ZoneGroupId { get; set; }
    public int Point { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (Point <= 0 || ZoneGroupId == 0 || ZoneGroupId > short.MaxValue)
        {
            Logger.Warn("{0}({1}): point {2} / zone group {3} is not settleable; quest {4} completes without that reward",
                QuestActTemplateName, DetailId, Point, ZoneGroupId, quest.TemplateId);
            return true;
        }

        var status = ResidentManager.Instance.AddServicePoint(quest.Owner.Id, (short)ZoneGroupId, (uint)Point);
        Logger.Debug("{0}({1}).RunAct: Quest: {2}, Owner {3} ({4}), ZoneGroupId {5}, Point {6} -> {7}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, quest.Owner.Id, ZoneGroupId, Point, status);
        return true;
    }
}
