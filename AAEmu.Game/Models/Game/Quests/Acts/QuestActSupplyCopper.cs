using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyCopper : QuestActTemplate
    {
        public int Amount { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Debug("QuestActSupplyCopper");

            character.Money += Amount;
            character.SendPacket(
                new SCItemTaskSuccessPacket(ItemTaskType.QuestComplete, new List<ItemTask> {new MoneyChange(Amount)}, new List<ulong>())
            );

            return true;
        }
    }
}
