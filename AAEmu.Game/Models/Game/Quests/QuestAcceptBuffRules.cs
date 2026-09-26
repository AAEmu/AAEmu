using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// quest_act_con_accept_buffs (25 rows, all Start): the quest starts from buff_id. 24 of the 25
/// quests are started server side by an AcceptQuestEffect on a buff_triggers row of that buff (21
/// on event 12 Started, 2 on event 6 Timeout for buffs 31621 and 31622, 1 on event 1 Attack for
/// 25912), so the acceptor is the buff the trigger fired from. A quest started another way (a
/// CSStartQuestContext with no source, or quest 5277 whose buff 838 has no trigger) passes while
/// the character still carries the buff. The client reader LoadQuestActConAcceptBuffDescs
/// reads id and buff_id.
/// </summary>
public static class QuestAcceptBuffRules
{
    public static bool Accepts(QuestAcceptorType acceptorType, uint acceptorId, uint buffId, bool ownerHasBuff)
    {
        if (buffId == 0)
            return false;
        if (acceptorType == QuestAcceptorType.Buff && acceptorId == buffId)
            return true;
        return ownerHasBuff;
    }
}
