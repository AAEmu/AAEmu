using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: invite count players carrying buff_id into the team faction of quest_act_obj_invite_id
/// (QuestInviteTeamFactionRules). Raised on the inviter by ExpeditionManager.ReplyInvite once the
/// join is committed.
/// </summary>
public class QuestActObjInviteTeamFaction(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public QuestActObjInviteType InviteType { get; set; }
    public uint BuffId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, {4} invites with buff {5}, {6}/{7}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, InviteType, BuffId,
            currentObjectiveCount, Count);
        return QuestProgressActRules.ObjectiveMet(currentObjectiveCount, Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnInviteTeamFaction += questAct.OnInviteTeamFaction;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnInviteTeamFaction -= questAct.OnInviteTeamFaction;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnInviteTeamFaction(QuestAct questAct, object sender, OnInviteTeamFactionArgs args)
    {
        if (questAct.Template.ActId != ActId)
            return;
        var invitedHasBuff = BuffId != 0 && (args.Invited?.Buffs?.CheckBuff(BuffId) ?? false);
        if (!QuestInviteTeamFactionRules.Counts(InviteType, args.InviteType, BuffId, invitedHasBuff))
            return;
        AddObjective(questAct, 1);
    }
}
