using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// quest_act_con_accept_npc_groups (145 rows, Start) and quest_act_con_report_npc_groups (266
/// enabled rows, Ready) name a quest_monster_groups id whose members are the quest_monster_npcs
/// rows of that group (group 698 has 78 npcs, group 750 has 6). The accept passes when the acceptor
/// is an NPC of the group; a report talk counts when the NPC talked to is a member. Twelve report
/// rows name groups 894 to 897, which have no quest_monster_groups row and no members (dummy quests
/// 9139 to 9156), so they never match.
/// </summary>
public static class QuestNpcGroupRules
{
    public static bool Accepts(QuestAcceptorType acceptorType, uint acceptorId, Func<uint, bool> npcInGroup)
        => acceptorType == QuestAcceptorType.Npc && Matches(acceptorId, npcInGroup);

    public static bool Matches(uint npcTemplateId, Func<uint, bool> npcInGroup)
        => npcTemplateId != 0 && npcInGroup != null && npcInGroup(npcTemplateId);
}
