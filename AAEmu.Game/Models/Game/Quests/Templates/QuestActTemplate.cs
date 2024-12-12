using System;
using System.Linq;
using NLog;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate(QuestComponentTemplate parentComponent)
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public QuestTemplate ParentQuestTemplate { get; set; } = parentComponent.ParentQuestTemplate;
    public QuestComponentTemplate ParentComponent { get; set; } = parentComponent;

    /// <summary>
    /// quest_acts Id, not really used anywhere
    /// </summary>
    public uint ActId { get; set; }

    /// <summary>
    /// quest_act_xxx Id / quest_acts DetailId
    /// </summary>
    public uint DetailId { get; set; }
    public string DetailType { get; set; }

    /// <summary>
    /// Total Objective Count needed to mark this Act as completed, also used for giving item count, as this is technically also a goal.
    /// </summary>
    public virtual int Count { get; set; }

    /// <summary>
    /// True if this Act should be considered an Objective and use the objective counters
    /// </summary>
    public virtual bool CountsAsAnObjective { get; set; }

    /// <summary>
    /// Resolved short name of this Class
    /// </summary>
    protected string QuestActTemplateName
    {
        get
        {
            return GetType().Name.Split(".").Last();
        }
    }

    /// <summary>
    /// Index inside this Component for objectives
    /// </summary>
    public byte ThisComponentObjectiveIndex { get; set; } = 0xFF;

    /// <summary>
    /// The index of this Selective Reward Act
    /// </summary>
    public int ThisSelectiveIndex { get; set; }

    /// <summary>
    /// Called for every QuestAct in a component when the component is activated (Step changed)
    /// </summary>
    public virtual void InitializeAction(Quest quest, QuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName}.InitializeAction({questAct.Template.DetailId}) Owner {quest.Owner.Name} ({quest.Owner.Id})");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed or cancelled (Step changed)
    /// </summary>
    public virtual void FinalizeAction(Quest quest, QuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName}.FinalizeAction({questAct.Template.DetailId}) Owner {quest.Owner.Name} ({quest.Owner.Id})");
    }

    /// <summary>
    /// Called for every QuestAct used by the Quest when creating the Quest (basically on Quest Constructor)
    /// </summary>
    public virtual void InitializeQuest(Quest quest, QuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName}.InitializeQuest({questAct.Template.DetailId}) Owner {quest.Owner.Name} ({quest.Owner.Id})");
    }

    /// <summary>
    /// Called for every QuestAct used by the Quest when the Quest gets removed in any way (basically on Quest Destructor)
    /// </summary>
    public virtual void FinalizeQuest(Quest quest, QuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName}.FinalizeQuest({questAct.Template.DetailId}) Owner {quest.Owner.Name} ({quest.Owner.Id})");
    }

    /// <summary>
    /// Execute and check an Act for its results, called after updating objective counts, descendents should never call base()
    /// </summary>
    /// <param name="quest">Quest this RunAct is called for</param>
    /// <param name="questAct">QuestAct this RunAct is called from</param>
    /// <param name="currentObjectiveCount">Current Objective Count</param>
    /// <returns>True if executed correctly, or objectives have been met</returns>
    public virtual bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"{QuestActTemplateName}({DetailId}).RunAct, Quest {quest.TemplateId}, Owner: {quest.Owner.Name}, not implemented!");
        return false;
    }

    /// <summary>
    /// Max Objective based if quest can overachieve
    /// </summary>
    /// <returns></returns>
    public virtual int MaxObjective()
    {
        var val = Count;
        // If Score-base, calculate max objective count needed to get score
        if (ParentComponent.ParentQuestTemplate.Score > 0)
            val = (ParentComponent.ParentQuestTemplate.Score / Count) + 1;
        return val > 0 ? (ParentQuestTemplate.LetItDone ? (int)Math.Ceiling(val * 3f / 2f) : val) : 1;
    }

    /// <summary>
    /// Set Current Objective Count for this Act
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="value"></param>
    protected void SetObjective(Quest quest, int value)
    {
        if (quest == null)
            return;

        // Don't go over max
        value = Math.Min(value, MaxObjective());
        if (quest.Objectives[ThisComponentObjectiveIndex] != value)
        {
            quest.Objectives[ThisComponentObjectiveIndex] = value;
            quest.RequestEvaluation();
        }
    }

    /// <summary>
    /// Set Current Objective Count for this Act, forwards using questAct's quest object
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="value"></param>
    protected void SetObjective(QuestAct questAct, int value) => SetObjective(questAct.QuestComponent.Parent.Parent, value);

    /// <summary>
    /// Get Current Objective Count for this Act (forwarded value from Quest)
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    public int GetObjective(Quest quest)
    {
        return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;
    }
    public int GetObjective(QuestAct questAct) => GetObjective(questAct.QuestComponent.Parent.Parent);

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    /// <returns>New amount for the objective</returns>
    protected int AddObjective(Quest quest, int amount)
    {
        if ((quest == null) || (amount == 0))
            return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;

        var maxValue = MaxObjective();
        if ((maxValue > 0) && (quest.Objectives[ThisComponentObjectiveIndex] + amount >= maxValue))
        {
            quest.Objectives[ThisComponentObjectiveIndex] = maxValue;
        }
        else
        {
            quest.Objectives[ThisComponentObjectiveIndex] += amount;
        }
        quest.RequestEvaluation();

        return quest.Objectives[ThisComponentObjectiveIndex];
    }

    /// <summary>
    /// Adds to current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="amount"></param>
    /// <returns>New amount for the objective</returns>
    public int AddObjective(QuestAct questAct, int amount) => AddObjective(questAct.QuestComponent.Parent.Parent, amount);

    /// <summary>
    /// Called when a quest ended or otherwise removed, use to clean up items and tasks
    /// </summary>
    /// <param name="quest"></param>
    public virtual void QuestCleanup(Quest quest)
    {
        // Nothing by default
    }

    /// <summary>
    /// Called when a quest got dropped by the player
    /// </summary>
    /// <param name="quest"></param>
    public virtual void QuestDropped(Quest quest)
    {
        // Nothing by default
    }

    // The handlers here are the ones that actually do something.
    // The versions in IQuestAct are the ones that get registered during Initialize/Finalize, and forward it to these 
    #region event_handlers

    /// <summary>
    /// OnMonsterGroupHunt 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMonsterGroupHunt(QuestAct questAct, object sender, OnMonsterGroupHuntArgs args)
    {
        //
    }

    /// <summary>
    /// OnMonsterHunt 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMonsterHunt(QuestAct questAct, object sender, OnMonsterHuntArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGather 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGather(QuestAct questAct, object sender, OnItemGatherArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGroupGather 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupGather(QuestAct questAct, object sender, OnItemGroupGatherArgs args)
    {
        //
    }

    /// <summary>
    /// OnTalkMade 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkMade(QuestAct questAct, object sender, OnTalkMadeArgs args)
    {
        //
    }

    /// <summary>
    /// OnTalkNpcGroupMade 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkNpcGroupMade(QuestAct questAct, object sender, OnTalkNpcGroupMadeArgs args)
    {
        //
    }

    /// <summary>
    /// OnAggro 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAggro(QuestAct questAct, object sender, OnAggroArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemUse 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemUse(QuestAct questAct, object sender, OnItemUseArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGroupUse 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupUse(QuestAct questAct, object sender, OnItemGroupUseArgs args)
    {
        //
    }

    /// <summary>
    /// OnInteract 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnInteraction(QuestAct questAct, object sender, OnInteractionArgs args)
    {
        //
    }

    /// <summary>
    /// OnCraft 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCraft(QuestAct questAct, object sender, OnCraftArgs args)
    {
        //
    }

    /// <summary>
    /// OnExpressFire 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExpressFire(QuestAct questAct, object sender, OnExpressFireArgs args)
    {
        //
    }

    /// <summary>
    /// OnLevelUp 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnLevelUp(QuestAct questAct, object sender, OnLevelUpArgs args)
    {
        //
    }

    /// <summary>
    /// OnMateLevelUp 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMateLevelUp(QuestAct questAct, object sender, OnMateLevelUpArgs args)
    {
        //
    }

    /// <summary>
    /// OnAbilityLevelUp 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAbilityLevelUp(QuestAct questAct, object sender, OnAbilityLevelUpArgs args)
    {
        //
    }

    /// <summary>
    /// OnEnterSphere 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnEnterSphere(QuestAct questAct, object sender, OnEnterSphereArgs args)
    {
        //
    }

    /// <summary>
    /// OnExitSphere 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExitSphere(QuestAct questAct, object sender, OnExitSphereArgs args)
    {
        //
    }

    /// <summary>
    /// OnZoneKill 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnZoneKill(QuestAct questAct, object sender, OnZoneKillArgs args)
    {
        //
    }

    /// <summary>
    /// OnCinemaStarted 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaStarted(QuestAct questAct, object sender, OnCinemaStartedArgs args)
    {
        //
    }

    /// <summary>
    /// OnCinemaEnded 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaEnded(QuestAct questAct, object sender, OnCinemaEndedArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportNpc 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportNpc(QuestAct questAct, object sender, OnReportNpcArgs args)
    {
        //
    }

    /// <summary>
    /// OnAcceptDoodad 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAcceptDoodad(QuestAct questAct, object sender, OnAcceptDoodadArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportDoodad 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportDoodad(QuestAct questAct, object sender, OnReportDoodadArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportJournal 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportJournal(QuestAct questAct, object sender, OnReportJournalArgs args)
    {
        //
    }

    /// <summary>
    /// OnQuestComplete 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnQuestComplete(QuestAct questAct, object sender, OnQuestCompleteArgs args)
    {
        //
    }

    /// <summary>
    /// OnKill 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnKill(QuestAct questAct, object sender, OnKillArgs args)
    {
        //
    }

    /// <summary>
    /// OnAttack 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttack(QuestAct questAct, object sender, OnAttackArgs args)
    {
        //
    }

    /// <summary>
    /// OnAttacked 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttacked(QuestAct questAct, object sender, OnAttackedArgs args)
    {
        //
    }

    /// <summary>
    /// OnDamage 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamage(QuestAct questAct, object sender, OnDamageArgs args)
    {
        //
    }

    /// <summary>
    /// OnDamaged
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamaged(QuestAct questAct, object sender, OnDamagedArgs args)
    {
        //
    }

    /// <summary>
    /// Triggered when a Quest Timer is expired
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTimerExpired(QuestAct questAct, object sender, OnTimerExpiredArgs args)
    {
        //
    }

    /// <summary>
    /// Triggered when a Quest changes step states
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnQuestStepChanged(QuestAct questAct, object sender, OnQuestStepChangedArgs args)
    {
        //
    }

    #endregion // Event Handlers
}
