using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptSkill : QuestActTemplate
    {
        public uint SkillId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptSkill: SkillId {0}", SkillId);
            return false;
        }
    }
}
