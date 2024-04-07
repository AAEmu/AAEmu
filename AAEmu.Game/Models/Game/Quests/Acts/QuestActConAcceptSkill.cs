using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptSkill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SkillId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptSkill: SkillId {SkillId}");

        quest.QuestAcceptorType = QuestAcceptorType.Skill;
        quest.AcceptorId = SkillId;

        return false;
    }

    /// <summary>
    /// Checks if the Quest Acceptor is the correct skill
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptSkill({DetailId}).RunAct: Quest: {quest.TemplateId}, SkillId {SkillId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Skill && quest.AcceptorId == SkillId;
    }
}
