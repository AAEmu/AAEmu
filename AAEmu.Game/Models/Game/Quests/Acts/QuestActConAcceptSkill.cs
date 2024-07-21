using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptSkill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SkillId { get; set; }

    /// <summary>
    /// Checks if the Quest Acceptor is the correct skill
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), SkillId {SkillId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Skill && quest.AcceptorId == SkillId;
    }
}
