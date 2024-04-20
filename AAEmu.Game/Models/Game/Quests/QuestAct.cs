using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public class QuestAct(QuestComponent parentComponent, QuestActTemplate template) : IComparable<QuestAct>, IQuestAct
{
    /// <summary>
    /// Same as Template.ActId
    /// </summary>
    public uint Id => Template?.ActId ?? 0;
    public uint DetailId => Template?.DetailId ?? 0;
    public string DetailType { get; set; }
    public byte ThisComponentObjectiveIndex { get; set; }

    public QuestComponent QuestComponent { get; } = parentComponent;
    public QuestActTemplate Template { get; set; } = template;

    #region objectives

    private bool _overrideObjectiveCompleted;
    public bool OverrideObjectiveCompleted
    {
        get => _overrideObjectiveCompleted;
        set
        {
            if (_overrideObjectiveCompleted == value)
                return;
            _overrideObjectiveCompleted = value;
            RequestEvaluation();
        }
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    public void SetObjective(Quest quest, int value)
    {
        if (quest != null)
            quest.Objectives[ThisComponentObjectiveIndex] = value;
    }

    /// <summary>
    /// Get Current Objective Count for this Act (forwarded value from Quest)
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    public int GetObjective(Quest quest)
    {
        return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    public int AddObjective(Quest quest, int amount)
    {
        if (quest == null)
            return 0;
        quest.Objectives[ThisComponentObjectiveIndex] += amount;
        return quest.Objectives[ThisComponentObjectiveIndex];
    }
    #endregion

    public bool RunAct()
    {
        var count = (QuestComponent.Template.KindId == QuestComponentKind.Progress) && (Template.ThisComponentObjectiveIndex < QuestComponent.Parent.Parent.Objectives.Length) ? QuestComponent.Parent.Parent.Objectives[Template.ThisComponentObjectiveIndex] : 0;
        return Template.RunAct(QuestComponent.Parent.Parent, this, count) || OverrideObjectiveCompleted;
    }

    /// <summary>
    /// Compare Ids
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(QuestAct other)
    {
        return Id.CompareTo(other.Id);
    }
    
    #region event_handlers

    /// <summary>
    /// OnMonsterGroupHunt 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMonsterGroupHunt(object sender, OnMonsterGroupHuntArgs args)
    {
        Template.OnMonsterGroupHunt(this, sender, args);
    }

    /// <summary>
    /// OnMonsterHunt 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMonsterHunt(object sender, OnMonsterHuntArgs args)
    {
        Template.OnMonsterHunt(this, sender, args);
    }

    /// <summary>
    /// OnItemGather 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGather(object sender, OnItemGatherArgs args)
    {
        Template.OnItemGather(this, sender, args);
    }

    /// <summary>
    /// OnItemGroupGather 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupGather(object sender, OnItemGroupGatherArgs args)
    {
        Template.OnItemGroupGather(this, sender, args);
    }

    /// <summary>
    /// OnTalkMade 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkMade(object sender, OnTalkMadeArgs args)
    {
        Template.OnTalkMade(this, sender, args);
    }

    /// <summary>
    /// OnTalkNpcGroupMade 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkNpcGroupMade(object sender, OnTalkNpcGroupMadeArgs args)
    {
        Template.OnTalkNpcGroupMade(this, sender, args);
    }

    /// <summary>
    /// OnAggro 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAggro(object sender, OnAggroArgs args)
    {
        Template.OnAggro(this, sender, args);
    }

    /// <summary>
    /// OnItemUse 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemUse(object sender, OnItemUseArgs args)
    {
        Template.OnItemUse(this, sender, args);
    }

    /// <summary>
    /// OnItemGroupUse 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupUse(object sender, OnItemGroupUseArgs args)
    {
        Template.OnItemGroupUse(this, sender, args);
    }

    /// <summary>
    /// OnInteract 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnInteraction(object sender, OnInteractionArgs args)
    {
        Template.OnInteraction(this, sender, args);
    }

    /// <summary>
    /// OnCraft 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCraft(object sender, OnCraftArgs args)
    {
        Template.OnCraft(this, sender, args);
    }

    /// <summary>
    /// OnExpressFire 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExpressFire(object sender, OnExpressFireArgs args)
    {
        Template.OnExpressFire(this, sender, args);
    }

    /// <summary>
    /// OnLevelUp 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnLevelUp(object sender, OnLevelUpArgs args)
    {
        Template.OnLevelUp(this, sender, args);
    }

    /// <summary>
    /// OnMateLevelUp
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMateLevelUp(object sender, OnMateLevelUpArgs args)
    {
        Template.OnMateLevelUp(this, sender, args);
    }

    /// <summary>
    /// OnAbilityLevelUp 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAbilityLevelUp(object sender, OnAbilityLevelUpArgs args)
    {
        Template.OnAbilityLevelUp(this, sender, args);
    }

    /// <summary>
    /// OnEnterSphere 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnEnterSphere(object sender, OnEnterSphereArgs args)
    {
        Template.OnEnterSphere(this, sender, args);
    }
    
    /// <summary>
    /// OnExitSphere 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExitSphere(object sender, OnExitSphereArgs args)
    {
        Template.OnExitSphere(this, sender, args);
    }

    /// <summary>
    /// OnZoneKill 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnZoneKill(object sender, OnZoneKillArgs args)
    {
        Template.OnZoneKill(this, sender, args);
    }

    /// <summary>
    /// OnCinemaStarted 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaStarted(object sender, OnCinemaStartedArgs args)
    {
        Template.OnCinemaStarted(this, sender, args);
    }

    /// <summary>
    /// OnCinemaEnded 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaEnded(object sender, OnCinemaEndedArgs args)
    {
        Template.OnCinemaEnded(this, sender, args);
    }

    /// <summary>
    /// OnReportNpc 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportNpc(object sender, OnReportNpcArgs args)
    {
        Template.OnReportNpc(this, sender, args);
    }

    /// <summary>
    /// OnAcceptDoodad 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAcceptDoodad(object sender, OnAcceptDoodadArgs args)
    {
        Template.OnAcceptDoodad(this, sender, args);
    }

    /// <summary>
    /// OnReportDoodad 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportDoodad(object sender, OnReportDoodadArgs args)
    {
        Template.OnReportDoodad(this, sender, args);
    }

    /// <summary>
    /// OnReportJournal 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportJournal(object sender, OnReportJournalArgs args)
    {
        Template.OnReportJournal(this, sender, args);
    }

    /// <summary>
    /// OnQuestComplete 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnQuestComplete(object sender, OnQuestCompleteArgs args)
    {
        Template.OnQuestComplete(this, sender, args);
    }

    /// <summary>
    /// OnKill 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnKill(object sender, OnKillArgs args)
    {
        Template.OnKill(this, sender, args);
    }

    /// <summary>
    /// OnAttack 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttack(object sender, OnAttackArgs args)
    {
        Template.OnAttack(this, sender, args);
    }

    /// <summary>
    /// OnAttacked 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttacked(object sender, OnAttackedArgs args)
    {
        Template.OnAttacked(this, sender, args);
    }

    /// <summary>
    /// OnDamage 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamage(object sender, OnDamageArgs args)
    {
        Template.OnDamage(this, sender, args);
    }

    /// <summary>
    /// OnDamaged
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamaged(object sender, OnDamagedArgs args)
    {
        Template.OnDamaged(this, sender, args);
    }

    public void OnTimerExpired(object sender, OnTimerExpiredArgs args)
    {
        Template.OnTimerExpired(this, sender, args);
    }
    
    public void OnQuestStepChanged(object sender, OnQuestStepChangedArgs args)
    {
        Template.OnQuestStepChanged(this, sender, args);
    }

    #endregion // Event Handlers
    
    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    public void RequestEvaluation()
    {
        QuestComponent.RequestEvaluation();
    }
}
