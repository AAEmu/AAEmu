using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyExp : QuestActTemplate
{
    public int Exp { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActSupplyExp, Exp: {Exp}");
        quest.QuestRewardExpPool += Exp;
        return true;
    }
}
