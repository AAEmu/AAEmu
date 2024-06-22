using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportNpc(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the current target is the specified Npc
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {NpcId}");
        // TODO Verify: Does it actually have to be targeted?
        return questAct.OverrideObjectiveCompleted || ((quest.Owner.CurrentTarget is Npc npc) && (npc.TemplateId == NpcId));
    }

    public override void InitializeQuest(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnReportNpc += questAct.OnReportNpc;
    }

    public override void FinalizeQuest(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnReportNpc -= questAct.OnReportNpc;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnReportNpc(IQuestAct questAct, object sender, OnReportNpcArgs args)
    {
        if ((questAct.Id != ActId) || (NpcId != args.NpcId))
            return;

        Logger.Debug($"QuestActConReportNpc({DetailId}).OnReportNpc: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), NpcId {args.NpcId}, Selected {args.Selected}");

        questAct.QuestComponent.Parent.Parent.SelectedRewardIndex = args.Selected;
        questAct.OverrideObjectiveCompleted = true;
        if (questAct.QuestComponent.Parent.Parent.Step <= QuestComponentKind.Progress)
            questAct.QuestComponent.Parent.Parent.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation(); // Manual request since this does not use objective counters to trigger
    }
}
