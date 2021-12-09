using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjAliase : QuestActTemplate
    {
		public string Name { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjAliase");

            return character.Quests.IsQuestComplete(quest.TemplateId);
        }
    }
}
