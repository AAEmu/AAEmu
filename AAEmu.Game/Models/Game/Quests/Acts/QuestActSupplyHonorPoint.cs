using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyHonorPoint : QuestActTemplate
    {
        public int Point { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyHonorPoint");
            quest.AddCurrencyToQuestActCoinsPool(ShopCurrencyType.Honor, Point);
            return true;
        }
    }
}
