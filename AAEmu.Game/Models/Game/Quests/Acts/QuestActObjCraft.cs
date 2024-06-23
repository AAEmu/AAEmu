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

    /// <summary>
    /// Checks if the number of crafts have been completed (or score has been met)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), CraftId {CraftId}, Count {Count}");
        return ParentQuestTemplate.Score > 0
            ? currentObjectiveCount * Count > ParentQuestTemplate.Score
            : currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnCraft += questAct.OnCraft;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnCraft -= questAct.OnCraft;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnCraft(QuestAct questAct, object sender, OnCraftArgs e)
    {
        if ((questAct.Template.ActId == ActId) && (e.CraftId == CraftId))
        {
            Logger.Debug($"{QuestActTemplateName}({DetailId}).OnCraft: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), CraftId {CraftId}");
            AddObjective(questAct.QuestComponent.Parent.Parent, 1);
        }
    }
}
