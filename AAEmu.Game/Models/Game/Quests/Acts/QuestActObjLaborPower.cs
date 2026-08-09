using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress objective: spend labor after the quest is accepted.
/// Does not credit labor spent before accept (no retroactive / lifetime total).
/// </summary>
public class QuestActObjLaborPower(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;

    /// <summary>Optional actability group filter; 0 = any labor spend counts.</summary>
    public uint ActabilityGroupId { get; set; }

    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            $"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), " +
            $"Labor {currentObjectiveCount}/{Count}, ActabilityGroupId {ActabilityGroupId}");

        return ParentQuestTemplate.Score > 0
            ? currentObjectiveCount * Count > ParentQuestTemplate.Score
            : currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnLaborPower += questAct.OnLaborPower;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnLaborPower -= questAct.OnLaborPower;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnLaborPower(QuestAct questAct, object sender, OnLaborPowerArgs e)
    {
        if (questAct.Template.ActId != ActId)
            return;
        if (e.LaborUsed <= 0)
            return;
        if (ActabilityGroupId != 0 && e.ActabilityGroupId != ActabilityGroupId)
            return;

        Logger.Debug(
            $"{QuestActTemplateName}({DetailId}).OnLaborPower: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, " +
            $"Owner {questAct.QuestComponent.Parent.Parent.Owner.Name}, used {e.LaborUsed}, group {e.ActabilityGroupId}");

        AddObjective(questAct, e.LaborUsed);
    }
}
