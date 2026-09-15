using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>Awards personal guild contribution points for ordinary quest rewards.</summary>
public class QuestActSupplyContributionPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint Point { get; set; }

    internal Func<Character, uint, bool> AddContribution { get; init; } =
        (character, point) => point <= int.MaxValue &&
                              ExpeditionManager.Instance.TryChangeContributionPoints(
                                  character, (int)point, addToWeeklyTotal: true);

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        // Expedition-detail quests are shared guild assignments. Their reward transition owns the
        // contribution award once for the guild; executing this personal action would multiply it.
        if (ParentQuestTemplate.DetailId == QuestDetail.Expedition)
            return true;

        if (quest.Owner is Character character && Point > 0)
            AddContribution(character, Point);
        return true;
    }
}
