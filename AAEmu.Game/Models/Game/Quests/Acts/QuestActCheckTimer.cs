using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckTimer(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int LimitTime { get; set; }
    /// <summary>
    /// ForceChangeComponent, true for all used quests
    /// </summary>
    public bool ForceChangeComponent { get; set; }
    /// <summary>
    /// NextComponent, not actively used
    /// </summary>
    public uint NextComponent { get; set; }
    /// <summary>
    /// PlaySkill Not actively used
    /// </summary>
    public bool PlaySkill { get; set; }
    /// <summary>
    /// SkillId Not actively used
    /// </summary>
    public uint SkillId { get; set; }
    /// <summary>
    /// CheckBuff is not used
    /// </summary>
    public bool CheckBuff { get; set; }
    /// <summary>
    /// BuffId is not used
    /// </summary>
    public uint BuffId { get; set; }
    /// <summary>
    /// SustainBuff not actively used
    /// </summary>
    public bool SustainBuff { get; set; }
    /// <summary>
    /// TimerNpcId not actively used
    /// </summary>
    public uint TimerNpcId { get; set; }
    /// <summary>
    /// IsKillPlayer not actively used
    /// </summary>
    public bool IsSkillPlayer { get; set; }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).InitializeAction Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");

        if (!QuestManager.Instance.AddQuestTimer(quest.Owner, quest, LimitTime))
            Logger.Warn($"{QuestActTemplateName}({DetailId}).InitializeAction Timer Already running, Quest {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");
        else
            quest.Owner.Events.OnTimerExpired += questAct.OnTimerExpired;
        quest.Owner.Events.OnQuestStepChanged += questAct.OnQuestStepChanged;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).FinalizeAction Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");

        // Don't remove timer on FinalizeAction as it needs to persist as long as the quest is open and not failed or completed
        // quest.Owner.Events.OnTimerExpired -= questAct.OnTimerExpired;
        // _ = QuestManager.Instance.RemoveQuestTimer(quest.Owner.Id, quest.TemplateId);
        // quest.Owner.Events.OnQuestStepChanged -= questAct.OnQuestStepChanged;

        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Quest timeout. Timers handled by InitializeAction/FinalizeAction
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>Always returns true</returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct Quest {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");
        return true;
    }

    /// <summary>
    /// Handles what needs to be done when timer expires
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public override void OnTimerExpired(QuestAct questAct, object sender, OnTimerExpiredArgs args)
    {
        if (questAct.Id != ActId)
            return;
        
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnTimerExpired Quest {args.QuestId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id})");

        // NOTE: All the still active quests don't use any of the special parameters that are possible
        // So basically any quest that uses this, makes the quest fail as a result.
        QuestManager.Instance.FailQuest(questAct.QuestComponent.Parent.Parent.Owner, questAct.QuestComponent.Parent.Parent.TemplateId);
    }

    /// <summary>
    /// Use the OnQuestStepChange to check if the timer task should be cancelled or not
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public override void OnQuestStepChanged(QuestAct questAct, object sender, OnQuestStepChangedArgs args)
    {
        if (questAct.Id != ActId)
            return;
        
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnTimerExpired Quest {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id})");
        switch (args.Step)
        {
            case QuestComponentKind.None:
                break;
            case QuestComponentKind.Start:
                break;
            case QuestComponentKind.Supply:
                break;
            case QuestComponentKind.Progress:
                break;
            case QuestComponentKind.Fail:
            case QuestComponentKind.Ready:
            case QuestComponentKind.Drop:
            case QuestComponentKind.Reward:
                // For any step that puts the quest in an "end state", try to remove the timer 
                questAct.QuestComponent.Parent.Parent.Owner.Events.OnQuestStepChanged -= questAct.OnQuestStepChanged;
                questAct.QuestComponent.Parent.Parent.Owner.Events.OnTimerExpired -= questAct.OnTimerExpired;
                _ = QuestManager.Instance.RemoveQuestTimer(questAct.QuestComponent.Parent.Parent.Owner.Id, questAct.QuestComponent.Parent.Parent.TemplateId);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
