using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used? Maybe was supposed to be if you got a specific buff applied?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptBuff(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint BuffId { get; set; }

    /// <summary>
    /// Checks if the Quest was started from the specified Buff
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, Player {quest.Owner.Name} ({quest.Owner.Id}), BuffId {BuffId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Buff && quest.AcceptorId == BuffId;
    }
}
