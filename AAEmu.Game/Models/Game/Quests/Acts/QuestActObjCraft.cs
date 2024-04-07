using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCraft(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CraftId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjCraft");
        return ParentQuestTemplate.Score > 0 ? objective * Count >= ParentQuestTemplate.Score : objective >= Count;
    }

    /// <summary>
    /// Checks if the number of crafts have been completed (or score has been met)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjCinema({DetailId}).RunAct: Quest: {quest.TemplateId}, CraftId {CraftId}, Count {Count}");
        return ParentQuestTemplate.Score > 0
            ? currentObjectiveCount * Count > ParentQuestTemplate.Score
            : currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnCraft += OnCraft;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnCraft -= OnCraft;
        base.FinalizeAction(quest, questAct);
    }

    private void OnCraft(object sender, OnCraftArgs e)
    {
        if ((e.OwningQuest.TemplateId == ParentQuestTemplate.Id) && (e.CraftId == CraftId))
            AddObjective(e.OwningQuest, 1);
    }
}
