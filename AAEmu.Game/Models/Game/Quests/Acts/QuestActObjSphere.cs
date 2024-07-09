using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjSphere(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint SphereId { get; set; }
    public uint NpcId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the player is inside the sphere
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), SphereId {SphereId}, NpcId {NpcId}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnEnterSphere += questAct.OnEnterSphere;
        quest.Owner.Events.OnExitSphere += questAct.OnExitSphere;
        SphereQuestManager.Instance.AddSphereQuestTriggers(quest.Owner, quest, questAct.QuestComponent.Template.Id, NpcId);
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        SphereQuestManager.Instance.RemoveSphereQuestTriggers(quest.Owner.Id, quest.TemplateId);
        quest.Owner.Events.OnExitSphere -= questAct.OnExitSphere;
        quest.Owner.Events.OnEnterSphere -= questAct.OnEnterSphere;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnEnterSphere(QuestAct questAct, object sender, OnEnterSphereArgs args)
    {
        if ((questAct.Id != ActId) || (args.SphereQuest.ComponentId != questAct.QuestComponent.Template.Id))
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnEnterSphere: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), ComponentId {args.SphereQuest.ComponentId}");
        SetObjective((QuestAct)questAct, 1);
    }
    
    public override void OnExitSphere(QuestAct questAct, object sender, OnExitSphereArgs args)
    {
        if ((questAct.Id != ActId) || (args.SphereQuest.ComponentId != questAct.QuestComponent.Template.Id))
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnExitSphere: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), ComponentId {args.SphereQuest.ComponentId}");
        SetObjective((QuestAct)questAct, 0);
    }
   
}
