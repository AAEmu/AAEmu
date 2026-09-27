using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Ready: the quest is turned in at any NPC of quest_monster_group_id (QuestNpcGroupRules over the
/// loaded quest_monster_npcs), the way QuestActConReportNpc turns in at one NPC.
/// quest_act_con_report_npc_groups has 266 enabled rows over 97 groups (101 anniversary, 52 patch
/// change, 18 daily life quests); group 702 (quest 7823) lists 9 faction leader deputies. The
/// client reader LoadQuestActConReportNpcGroupDescs reads id,
/// quest_act_obj_alias_id, quest_monster_group_id, use_alias.
/// </summary>
public class QuestActConReportNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), QuestMonsterGroupId {QuestMonsterGroupId}");
        if (questAct.OverrideObjectiveCompleted)
            return true;
        if (!CompletesFromCurrentTarget(questAct))
            return false;
        return quest.Owner.CurrentTarget is Npc npc && InGroup(npc.TemplateId);
    }

    public override void InitializeQuest(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnReportNpc += questAct.OnReportNpc;
    }

    public override void FinalizeQuest(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnReportNpc -= questAct.OnReportNpc;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnReportNpc(QuestAct questAct, object sender, OnReportNpcArgs args)
    {
        if (questAct.Id != ActId || !InGroup(args.NpcId))
            return;

        var quest = questAct.QuestComponent.Parent.Parent;
        var kind = questAct.Template.ParentComponent.KindId;
        if (QuestReportNpcRules.TalkCompletesWithoutProgress(kind))
        {
            if (!CompletesFromTalkEvent(questAct))
                return;
            questAct.OverrideObjectiveCompleted = true;
            questAct.RequestEvaluation();
            return;
        }

        // Same guard as QuestActConReportNpc: a turn-in at a shared NPC must not complete every
        // quest that reports there.
        var minimumProgress = questAct.Template.ParentComponent.ParentQuestTemplate.LetItDone
            ? QuestObjectiveStatus.CanEarlyComplete
            : QuestObjectiveStatus.QuestComplete;
        var isReady = quest.GetQuestObjectiveStatus() >= minimumProgress;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnReportNpc: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {args.NpcId}, Group {QuestMonsterGroupId}, Selected {args.Selected}, isReady {isReady}");

        if (!isReady)
            return;

        quest.SelectedRewardIndex = args.Selected;
        questAct.OverrideObjectiveCompleted = true;
        if (QuestReportNpcRules.TalkAdvancesToReady(kind) && quest.Step <= QuestComponentKind.Progress)
            quest.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation();
    }

    private bool InGroup(uint npcTemplateId) =>
        QuestNpcGroupRules.Matches(npcTemplateId, npcId => QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, npcId));

    private static bool CompletesFromCurrentTarget(QuestAct questAct) =>
        QuestReportNpcRules.CompletesFromCurrentTarget(
            questAct.Template.ParentComponent.KindId,
            questAct.Template.ParentComponent.PlayCinemaBeforeBubble,
            ProgressCinemaId(questAct));

    private static bool CompletesFromTalkEvent(QuestAct questAct) =>
        QuestReportNpcRules.CompletesFromTalkEvent(
            questAct.Template.ParentComponent.KindId,
            questAct.Template.ParentComponent.PlayCinemaBeforeBubble,
            ProgressCinemaId(questAct));

    private static uint ProgressCinemaId(QuestAct questAct) =>
        QuestCinemaRules.FirstCinema(
            questAct.QuestComponent.Parent.Parent.Template,
            QuestComponentKind.Progress);
}
