using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjInteraction(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint DoodadId { get; set; }
    public bool UseAlias { get; set; }
    public bool TeamShare { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint Phase { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Validate Phase when needed
        Logger.Debug($"QuestActObjInteraction: DoodadId {DoodadId}, Count {Count}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update(quest, questAct);

        return quest.GetQuestObjectiveStatus() >= QuestObjectiveStatus.CanEarlyComplete;
    }

    public override void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        // base.Update(quest, questAct, updateAmount);
        // Objective count is already set by CheckAct
        Logger.Info($"{QuestActTemplateName} - QuestActItemGather {DetailId} was updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }

    /// <summary>
    /// Checks if the number if interactions has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Count {currentObjectiveCount}/{Count}, WorldInteractionId {WorldInteractionId}, DoodadId {DoodadId}, TeamShare {TeamShare}, Phase {Phase}.");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Parent.RegisterEventHandler(quest.Parent.OnInteractionList, questAct);
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Parent.UnRegisterEventHandler(quest.Parent.OnInteractionList, questAct);
        base.FinalizeAction(quest, questAct);
    }
}
