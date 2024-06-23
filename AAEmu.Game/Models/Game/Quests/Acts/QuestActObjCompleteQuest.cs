using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCompleteQuest(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestId { get; set; }
    public bool AcceptWith { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if a specific quest has been completed before
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Warn($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), QuestId {QuestId}, AcceptWith {AcceptWith}");
        // TODO: Not sure what AcceptWith is supposed to do, but none of the still existing quests seem to use this
        // I'd assume this would indicate that you also automatically accept this quest when getting to this step?

        if ((currentObjectiveCount <= 0) && quest.Owner.Quests.HasQuestCompleted(QuestId))
            SetObjective(quest, 1);

        return GetObjective(quest) > 0;
    }
}
