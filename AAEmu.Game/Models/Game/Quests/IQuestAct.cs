using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestAct
{
    /// <summary>
    /// Same as Template.ActId
    /// </summary>
    uint Id { get; }
    QuestActTemplate Template { get; }
    uint ComponentId { get; set; }
    string DetailType { get; set; }
    public QuestComponent QuestComponent { get; }
    uint DetailId { get; }
    void SetObjective(Quest quest, int value);
    int GetObjective(Quest quest);

    int CompareTo(QuestAct other);
    QuestActTemplate GetTemplate();
    T GetTemplate<T>() where T : QuestActTemplate;
    bool Use(ICharacter character, Quest quest, int objective);
    int AddObjective(Quest quest, int amount);
    /// <summary>
    /// Execute a Act and return true if successful (early complete quests should return true if minimum is met)
    /// </summary>
    /// <returns></returns>
    bool RunAct();
    
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
    void OnZoneMonsterHunt(object sender, OnZoneMonsterHuntArgs args);
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

    #endregion // Event Handlers

}
