using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptSkill : QuestActTemplate
{
    public uint SkillId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptSkill: SkillId {SkillId}");

        quest.QuestAcceptorType = QuestAcceptorType.Skill;
        quest.AcceptorType = SkillId;

        return false;
    }
}
