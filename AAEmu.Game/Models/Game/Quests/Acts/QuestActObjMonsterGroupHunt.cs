using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterGroupHunt(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    /// <summary>
    /// Checks if the amount of monsters in the given group has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), QuestMonsterGroupId {QuestMonsterGroupId}, Count {currentObjectiveCount}/{Count}");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMonsterGroupHunt += questAct.OnMonsterGroupHunt;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnMonsterGroupHunt -= questAct.OnMonsterGroupHunt;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnMonsterGroupHunt(IQuestAct questAct, object sender, OnMonsterGroupHuntArgs args)
    {
        if (questAct.Id != ActId)
            return;

        if (QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, args.NpcId))
        {
            Logger.Debug($"{QuestActTemplateName}({DetailId}).OnMonsterGroupHunt: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), Npc {args.NpcId}, Count {args.Count}");
            AddObjective(questAct, (int)args.Count);
        }
    }
}
