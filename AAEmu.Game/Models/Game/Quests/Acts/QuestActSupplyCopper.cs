using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyCopper : QuestActTemplate
    {
        public int Amount { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Debug("QuestActSupplyCopper");
            quest.QuestRewardCoinsPool += Amount;
            return true;
        }
    }
}
