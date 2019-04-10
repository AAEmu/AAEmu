using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptLevelUp : QuestActTemplate
    {
        public byte Level { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Debug("QuestActConAcceptLevelUp");
            
            return character.Level >= Level;
        }
    }
}
