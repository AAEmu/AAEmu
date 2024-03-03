using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyJuryPoint : QuestActTemplate
{
    public int Point { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement Trial and Jury System
        Logger.Warn($"QuestActSupplyJuryPoint, Point: {Point}");
        return true;
    }
}
