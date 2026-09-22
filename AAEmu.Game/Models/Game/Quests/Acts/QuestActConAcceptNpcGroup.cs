using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Start: the accepting NPC must belong to quest_monster_group_id (QuestNpcGroupRules over the
/// loaded quest_monster_npcs). quest_act_con_accept_npc_groups has 145 rows over 56 groups; 42 sit
/// on "first steps" guide quests (category 216) and 11 on Auroria territory quests. Group 698
/// (quest 7823) lists 78 ability managers, group 750 (quests 8297 to 8300) 6 territory managers.
/// The client reader LoadQuestActConAcceptNpcGroupDescs (x2game-dev.dll FUN_39d47cb0) reads id and
/// quest_monster_group_id.
/// </summary>
public class QuestActConAcceptNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), QuestMonsterGroupId {QuestMonsterGroupId}, Acceptor {quest.QuestAcceptorType} {quest.AcceptorId}");
        return QuestNpcGroupRules.Accepts(
            quest.QuestAcceptorType,
            quest.AcceptorId,
            npcId => QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, npcId));
    }
}
