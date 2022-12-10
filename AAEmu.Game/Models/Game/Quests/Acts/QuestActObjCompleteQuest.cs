using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjCompleteQuest : QuestActTemplate
    {
        public uint QuestId { get; set; }
        public bool AcceptWith { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Debug("QuestActObjCompleteQuest");
            return character.Quests.IsQuestComplete(QuestId) == AcceptWith;
        }
    }
}
