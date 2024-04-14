using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterHunt(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActObjMonsterHunt: NpcId {NpcId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update(quest, questAct);

        return quest.GetQuestObjectiveStatus() >= QuestObjectiveStatus.CanEarlyComplete;
    }

    public override void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        // base.Update(quest, questAct, updateAmount);
        // Objective count is already set by CheckAct
        Logger.Info($"{QuestActTemplateName} - QuestActObjMonsterHunt {DetailId} was updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }
    
    /// <summary>
    /// Checks if the amount of monster kill has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {NpcId}, Count {currentObjectiveCount}/{Count}");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMonsterHunt += questAct.OnMonsterHunt;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnMonsterHunt -= questAct.OnMonsterHunt;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnMonsterHunt(IQuestAct questAct, object sender, OnMonsterHuntArgs args)
    {
        if ((questAct.Id != ActId) || (args.NpcId != NpcId))
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnMonsterHunt: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), NpcId {args.NpcId}, Count {args.Count}");
        AddObjective(questAct, (int)args.Count);
    }
}
