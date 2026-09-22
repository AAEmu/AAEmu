using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Start: the quest was started from buff_id, or the character still carries it
/// (QuestAcceptBuffRules). The 25 rows sit on capture, exile and festival quests; the buff's
/// buff_triggers row fires AcceptQuestEffect, which passes the buff through as the acceptor.
/// </summary>
public class QuestActConAcceptBuff(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint BuffId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), BuffId {BuffId}, Acceptor {quest.QuestAcceptorType} {quest.AcceptorId}");
        var ownerHasBuff = quest.Owner?.Buffs?.CheckBuff(BuffId) == true;
        return QuestAcceptBuffRules.Accepts(quest.QuestAcceptorType, quest.AcceptorId, BuffId, ownerHasBuff);
    }
}
