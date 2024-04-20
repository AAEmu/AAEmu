using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestAct
{
    int CompareTo(QuestAct other);

    /// <summary>
    /// Same as Template.ActId
    /// </summary>
    uint Id { get; }
    
    /// <summary>
    /// Actual Template for this Act
    /// </summary>
    QuestActTemplate Template { get; }

    /// <summary>
    /// DetailType of this Act
    /// </summary>
    string DetailType { get; set; }

    /// <summary>
    /// Parent QuestComponent of this Act
    /// </summary>
    public QuestComponent QuestComponent { get; }

    /// <summary>
    /// Detail Id of this Act
    /// </summary>
    uint DetailId { get; }

    /// <summary>
    /// Set this to true for acts with "objectives" that don't really used objectives like most events in the Ready step
    /// RunAct is still executed, but the results will be overwritten
    /// </summary>
    bool OverrideObjectiveCompleted { get; set; }

    /// <summary>
    /// Set current Objective Count for this Act
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="value"></param>
    void SetObjective(Quest quest, int value);

    /// <summary>
    /// Get the current Objective Count for this Act
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    int GetObjective(Quest quest);

    /// <summary>
    /// Adds amount to current Objective Counter for this Act
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    /// <returns></returns>
    int AddObjective(Quest quest, int amount);

    /// <summary>
    /// Execute an Act and return true if successful (early complete quests should return true if minimum is met)
    /// </summary>
    /// <returns></returns>
    bool RunAct();

    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    void RequestEvaluation();
    
    #region event_handlers
    void OnMonsterGroupHunt(object sender, OnMonsterGroupHuntArgs args);
    void OnMonsterHunt(object sender, OnMonsterHuntArgs args);
    void OnItemGather(object sender, OnItemGatherArgs args);
    void OnItemGroupGather(object sender, OnItemGroupGatherArgs args);
    void OnTalkMade(object sender, OnTalkMadeArgs args);
    void OnTalkNpcGroupMade(object sender, OnTalkNpcGroupMadeArgs args);
    void OnAggro(object sender, OnAggroArgs args);
    void OnItemUse(object sender, OnItemUseArgs args);
    void OnItemGroupUse(object sender, OnItemGroupUseArgs args);
    void OnInteraction(object sender, OnInteractionArgs args);
    void OnCraft(object sender, OnCraftArgs args);
    void OnExpressFire(object sender, OnExpressFireArgs args);
    void OnLevelUp(object sender, OnLevelUpArgs args);
    void OnMateLevelUp(object sender, OnMateLevelUpArgs args);
    void OnAbilityLevelUp(object sender, OnAbilityLevelUpArgs args);
    void OnEnterSphere(object sender, OnEnterSphereArgs args);
    void OnExitSphere(object sender, OnExitSphereArgs args);
    void OnZoneKill(object sender, OnZoneKillArgs args);
    void OnCinemaStarted(object sender, OnCinemaStartedArgs args);
    void OnCinemaEnded(object sender, OnCinemaEndedArgs args);
    void OnReportNpc(object sender, OnReportNpcArgs args);
    void OnAcceptDoodad(object sender, OnAcceptDoodadArgs args);
    void OnReportDoodad(object sender, OnReportDoodadArgs args);
    void OnReportJournal(object sender, OnReportJournalArgs args);
    void OnQuestComplete(object sender, OnQuestCompleteArgs args);
    void OnKill(object sender, OnKillArgs args);
    void OnAttack(object sender, OnAttackArgs args);
    void OnAttacked(object sender, OnAttackedArgs args);
    void OnDamage(object sender, OnDamageArgs args);
    void OnDamaged(object sender, OnDamagedArgs args);
    void OnTimerExpired(object sender, OnTimerExpiredArgs args);
    void OnQuestStepChanged(object sender, OnQuestStepChangedArgs e);
    #endregion // Event Handlers
}
