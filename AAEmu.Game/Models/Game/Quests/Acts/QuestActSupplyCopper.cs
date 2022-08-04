using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

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
