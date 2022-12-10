using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjLevel : QuestActTemplate
    {
        public byte Level { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Debug("QuestActObjLevel");
            return character.Level >= Level;
        }
    }
}
