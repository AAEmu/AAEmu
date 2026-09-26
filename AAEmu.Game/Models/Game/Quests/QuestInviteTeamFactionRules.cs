using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress act QuestActObjInviteTeamFaction: bring count players into the team faction named by
/// quest_act_obj_invite_id (1 expedition, the only enum_quest_act_obj_invite_types row). 4 rows,
/// quests 6801 to 6804 (count 1 to 4), all with buff_id 13921, the returning-player buff. The credit
/// goes to the inviter when the invitee's join is committed. The client reader
/// LoadQuestActObjInviteTeamFactionDescs reads id, buff_id, count,
/// quest_act_obj_alias_id, quest_act_obj_invite_id, use_alias.
/// </summary>
public static class QuestInviteTeamFactionRules
{
    /// <summary>buff_id 0 would accept any invitee; the shipped rows all require the buff.</summary>
    public static bool Counts(
        QuestActObjInviteType actInviteType,
        QuestActObjInviteType inviteType,
        uint buffId,
        bool invitedHasBuff)
        => actInviteType != 0 && actInviteType == inviteType && (buffId == 0 || invitedHasBuff);
}
