using System;
using System.Linq;
using NLog;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate(QuestComponentTemplate parentComponent)
{
    private bool IsInitialized { get; set; }
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
    public int Count { get; set; } = 0;

    protected string QuestActTemplateName
    {
        get
        {
            return GetType().Name.Split(".").Last();
        }
    }

    public byte ThisComponentObjectiveIndex { get; set; } = 0xFF;

    /// <summary>
    /// Called for every QuestAct in a component when the component is activated (Step changed)
    /// </summary>
    public virtual void InitializeAction(Quest quest, IQuestAct questAct)
    {
        IsInitialized = true;
        Logger.Info($"{QuestActTemplateName} - Initialize {DetailId}.");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed or cancelled (Step changed)
    /// </summary>
    public virtual void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - DeInitialize {DetailId}.");
        IsInitialized = false;
    }

    /// <summary>
    /// Moves objective for this QuestAct one further
    /// </summary>
    public virtual void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        if (updateAmount == 0)
            return;
        questAct.AddObjective(quest, updateAmount);
        Logger.Info($"{QuestActTemplateName} - {DetailId} has been updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }

    /// <summary>
    /// Resets objective for this QuestAct
    /// </summary>
    public virtual void ClearStatus(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - Reset QuestAct {DetailId} objectives.");
        questAct.SetObjective(quest, 0);
    }

    public virtual bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Info($"{QuestActTemplateName} - Use QuestAct {DetailId}, Character: {character.Name}, Objective {objective}.");
        return false;
    }

    /// <summary>
    /// Execute and check a Act for it's results, called after updating objective counts, descendents should never call base()
    /// </summary>
    /// <param name="quest">Quest this RunAct is called for</param>
    /// <param name="questAct">IQuestAct this RunAct is called from</param>
    /// <param name="currentObjectiveCount">Current Objective Count</param>
    /// <returns>True if executed correctly, or objectives have been met</returns>
    public virtual bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"{QuestActTemplateName}({DetailId}).RunAct, Quest {quest.TemplateId}, Owner: {quest.Owner.Name}, not implemented!");
        return false;
    }

    public virtual int MaxObjective()
    {
        return ParentQuestTemplate.LetItDone ? Count * 3 / 2 : Count;
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    public void SetObjective(Quest quest, int value)
    {
        if (quest != null)
            quest.Objectives[ThisComponentObjectiveIndex] = value;
    }
    public void SetObjective(IQuestAct questAct, int value) => SetObjective(questAct.QuestComponent.Parent.Parent, value); 

    /// <summary>
    /// Get Current Objective Count for this Act (forwarded value from Quest)
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    public int GetObjective(Quest quest)
    {
        return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;
    }
    public int GetObjective(IQuestAct questAct) => GetObjective(questAct.QuestComponent.Parent.Parent);

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    /// <param name="maxValue">If defined, the objective count will be capped at maxValue if it gets more than that</param>
    public int AddObjective(Quest quest, int amount, int maxValue = 0)
    {
        if (quest == null)
            return 0;
        quest.Objectives[ThisComponentObjectiveIndex] += amount;
        if ((maxValue > 0) && (quest.Objectives[ThisComponentObjectiveIndex] > maxValue))
            quest.Objectives[ThisComponentObjectiveIndex] = maxValue;
        return quest.Objectives[ThisComponentObjectiveIndex];
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="amount"></param>
    /// <param name="maxValue"></param>
    /// <returns></returns>
    public int AddObjective(IQuestAct questAct, int amount, int maxValue = 0) => AddObjective(questAct.QuestComponent.Parent.Parent, amount, maxValue);

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
    public virtual void OnMonsterGroupHunt(IQuestAct questAct, object sender, OnMonsterGroupHuntArgs args)
    {
        //
    }

    /// <summary>
    /// OnMonsterHunt 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnMonsterHunt(IQuestAct questAct, object sender, OnMonsterHuntArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGather 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGather(IQuestAct questAct, object sender, OnItemGatherArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGroupGather 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupGather(IQuestAct questAct, object sender, OnItemGroupGatherArgs args)
    {
        //
    }

    /// <summary>
    /// OnTalkMade 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkMade(IQuestAct questAct, object sender, OnTalkMadeArgs args)
    {
        //
    }

    /// <summary>
    /// OnTalkNpcGroupMade 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnTalkNpcGroupMade(IQuestAct questAct, object sender, OnTalkNpcGroupMadeArgs args)
    {
        //
    }

    /// <summary>
    /// OnAggro 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAggro(IQuestAct questAct, object sender, OnAggroArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemUse 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemUse(IQuestAct questAct, object sender, OnItemUseArgs args)
    {
        //
    }

    /// <summary>
    /// OnItemGroupUse 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnItemGroupUse(IQuestAct questAct, object sender, OnItemGroupUseArgs args)
    {
        //
    }

    /// <summary>
    /// OnInteract 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnInteraction(IQuestAct questAct, object sender, OnInteractionArgs args)
    {
        //
    }

    /// <summary>
    /// OnCraft 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCraft(IQuestAct questAct, object sender, OnCraftArgs args)
    {
        //
    }

    /// <summary>
    /// OnExpressFire 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExpressFire(IQuestAct questAct, object sender, OnExpressFireArgs args)
    {
        //
    }

    /// <summary>
    /// OnLevelUp 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnLevelUp(IQuestAct questAct, object sender, OnLevelUpArgs args)
    {
        //
    }

    /// <summary>
    /// OnAbilityLevelUp 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAbilityLevelUp(IQuestAct questAct, object sender, OnAbilityLevelUpArgs args)
    {
        //
    }

    /// <summary>
    /// OnEnterSphere 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnEnterSphere(IQuestAct questAct, object sender, OnEnterSphereArgs args)
    {
        //
    }
    
    /// <summary>
    /// OnExitSphere 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnExitSphere(IQuestAct questAct, object sender, OnExitSphereArgs args)
    {
        //
    }

    /// <summary>
    /// OnZoneKill 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnZoneKill(IQuestAct questAct, object sender, OnZoneKillArgs args)
    {
        //
    }

    /// <summary>
    /// OnZoneMonsterHunt 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnZoneMonsterHunt(IQuestAct questAct, object sender, OnZoneMonsterHuntArgs args)
    {
        //
    }

    /// <summary>
    /// OnCinemaStarted 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaStarted(IQuestAct questAct, object sender, OnCinemaStartedArgs args)
    {
        //
    }

    /// <summary>
    /// OnCinemaEnded 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnCinemaEnded(IQuestAct questAct, object sender, OnCinemaEndedArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportNpc 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportNpc(IQuestAct questAct, object sender, OnReportNpcArgs args)
    {
        //
    }

    /// <summary>
    /// OnAcceptDoodad 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAcceptDoodad(IQuestAct questAct, object sender, OnAcceptDoodadArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportDoodad 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportDoodad(IQuestAct questAct, object sender, OnReportDoodadArgs args)
    {
        //
    }

    /// <summary>
    /// OnReportJournal 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnReportJournal(IQuestAct questAct, object sender, OnReportJournalArgs args)
    {
        //
    }

    /// <summary>
    /// OnQuestComplete 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnQuestComplete(IQuestAct questAct, object sender, OnQuestCompleteArgs args)
    {
        //
    }

    /// <summary>
    /// OnKill 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnKill(IQuestAct questAct, object sender, OnKillArgs args)
    {
        //
    }

    /// <summary>
    /// OnAttack 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttack(IQuestAct questAct, object sender, OnAttackArgs args)
    {
        //
    }

    /// <summary>
    /// OnAttacked 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnAttacked(IQuestAct questAct, object sender, OnAttackedArgs args)
    {
        //
    }

    /// <summary>
    /// OnDamage 
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamage(IQuestAct questAct, object sender, OnDamageArgs args)
    {
        //
    }

    /// <summary>
    /// OnDamaged
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    public virtual void OnDamaged(IQuestAct questAct, object sender, OnDamagedArgs args)
    {
        //
    }

    
    #endregion // Event Handlers
}
