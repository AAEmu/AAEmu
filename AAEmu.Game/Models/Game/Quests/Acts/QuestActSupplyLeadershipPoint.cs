using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds point leadership points (quest_act_supply_leadership_points, 127 rows, 1 to 300;
/// 60 on "faction heroes" quests of category 137, 19 on category 147, the rest on hero missions,
/// sea and abyss quests). The client reader LoadQuestActSupplyLeadershipPointDescs reads id and point. Character.ChangeGamePoints(Leadership) moves the current
/// Period and, on a gain, the lifetime and daily figures, and pushes the season packet.
/// </summary>
public class QuestActSupplyLeadershipPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Point { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Point {Point}");
        var point = QuestSupplyPointRules.Grant(Point);
        if (point > 0)
            quest.Owner?.ChangeGamePoints(GamePointKind.Leadership, point);
        return true;
    }
}
