using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptSphere(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SphereId { get; set; }

    /// <summary>
    /// Checks if a Quest was started with the specified Sphere
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), SkillId {SphereId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Sphere && quest.AcceptorId == SphereId;
    }
}
