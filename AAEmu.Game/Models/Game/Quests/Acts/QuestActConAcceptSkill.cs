using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptSkill : QuestActTemplate
    {
        public uint SkillId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptSkill: SkillId {0}", SkillId);

            quest.QuestAcceptorType = QuestAcceptorType.Skill;
            quest.AcceptorType = SkillId;

            return false;
        }
    }
}
