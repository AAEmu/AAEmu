using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used? Maybe was supposed to be if you got a specific buff applied?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptBuff(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint BuffId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptBuff: BuffId {BuffId}");
        return false;
    }

    /// <summary>
    /// Checks if the Quest was started from the specified Buff
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptBuff({DetailId}).RunAct: Quest {quest.TemplateId}, BuffId {BuffId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Buff && quest.AcceptorId == BuffId;
    }
}
