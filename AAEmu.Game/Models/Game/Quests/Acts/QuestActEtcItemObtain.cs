using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Checks if a item has been obtained since the quest was started (does not require the item in the inventory)
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActEtcItemObtain(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public bool Cleanup { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActEtcItemObtain");

        Update(quest, questAct);

        return quest.GetQuestObjectiveStatus() >= QuestObjectiveStatus.CanEarlyComplete;
    }

    public override void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        // base.Update(quest, questAct, updateAmount);
    }

    /// <summary>
    /// Checks if the Objective count has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActEtcItemObtain({DetailId}).RunAct: Quest: {quest.TemplateId}, ItemId {ItemId}, Count {Count}");
        return quest.GetActObjective(ThisComponentObjectiveIndex) >= Count;
    }

    public override void Initialize(Quest quest, IQuestAct questAct)
    {
        base.Initialize(quest, questAct);
        quest.Owner.Events.OnItemGather += OnItemGather;
    }

    public override void DeInitialize(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnItemGather -= OnItemGather;
        base.DeInitialize(quest, questAct);
    }

    private void OnItemGather(object sender, OnItemGatherArgs e)
    {
        // Check if obtained the specified item, there is no check for removing for EtcItemObtain
        if ((e.ItemId == ItemId) && (e.Count > 0))
            AddObjective(e.OwningQuest, e.Count, Count);
    }
}
