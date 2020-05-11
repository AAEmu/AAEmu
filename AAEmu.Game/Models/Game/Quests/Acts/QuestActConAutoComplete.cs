using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAutoComplete : QuestActTemplate
    {
        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAutoComplete");

            return character.Quests.IsQuestComplete(quest.TemplateId);
        }
    }
}
