using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckCompleteComponent : QuestActTemplate
    {
        public uint CompleteComponent { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActCheckCompleteComponent: Complete Component {0}", CompleteComponent);
            return false;
        }
    }
}
