using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

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

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjSphere Quest={0}, ComponentId={1}, Act={2}", quest.TemplateId, quest.ComponentId, Id);
            //character.SendMessage("[AAEmu] Your quest was completed automatically because that's how quest spheres are implemented...");
            //character.SendMessage("Quest={0}, ComponentId={1}, Act={2}", quest.TemplateId, quest.ComponentId, Id);

            return true;
        }
    }
}
