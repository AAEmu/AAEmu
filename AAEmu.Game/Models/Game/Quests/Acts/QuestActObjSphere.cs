using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjSphere : QuestActTemplate
    {
        public uint SphereId { get; set; }
        public uint NpcId { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjSphere");
            character.SendMessage("[AAEmu] Your quest was auto-progressed because it uses Spheres which are not implemented yet. Please screenshot this and send it to the AAEmu team.");
            character.SendMessage("Quest {0}, Act {1}", quest.TemplateId, Id);
            return true;
        }
    }
}
