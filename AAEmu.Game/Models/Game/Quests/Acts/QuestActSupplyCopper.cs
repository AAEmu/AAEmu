using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyCopper : QuestActTemplate
{
    public int Amount { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActSupplyCopper, Amount: {Amount}");
        quest.QuestRewardCoinsPool += Amount;
        return true;
    }
}
