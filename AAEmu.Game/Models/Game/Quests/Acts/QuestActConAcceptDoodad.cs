using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptDoodad(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint DoodadId { get; set; }

    /// <summary>
    /// Checks if the Quest starter was indeed the specified doodad
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DoodadId {DoodadId}");
        return (quest.QuestAcceptorType == QuestAcceptorType.Doodad) && (quest.AcceptorId == DoodadId);
    }
}
