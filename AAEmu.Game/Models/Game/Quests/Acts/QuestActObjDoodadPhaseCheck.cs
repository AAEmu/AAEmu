using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Completes when the player observes the configured doodad in either configured function group.
/// </summary>
public class QuestActObjDoodadPhaseCheck(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint DoodadId { get; set; }
    public uint Phase1 { get; set; }
    public uint Phase2 { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DoodadId {DoodadId}, Phase1 {Phase1}, Phase2 {Phase2}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnDoodadPhaseCheck += questAct.OnDoodadPhaseCheck;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnDoodadPhaseCheck -= questAct.OnDoodadPhaseCheck;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnDoodadPhaseCheck(QuestAct questAct, object sender, OnDoodadPhaseCheckArgs args)
    {
        if (questAct.Id != ActId || args.DoodadId != DoodadId)
            return;

        if (args.DoodadFuncGroupId != Phase1 &&
            (Phase2 == 0 || args.DoodadFuncGroupId != Phase2))
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnDoodadPhaseCheck: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), DoodadId {DoodadId}, Phase {args.DoodadFuncGroupId}");
        SetObjective(questAct, 1);
    }
}
