using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCompleteQuest(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestId { get; set; }
    public bool AcceptWith { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjCompleteQuest");
        return character.Quests.IsQuestComplete(QuestId) == AcceptWith;
    }

    /// <summary>
    /// Checks if a specific quest has been completed before
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjCompleteQuest({DetailId}).RunAct: Quest: {quest.TemplateId}, QuestId {QuestId}, AcceptWith {AcceptWith}");
        // TODO: Not sure what AcceptWith is supposed to do, but none of the still existing quests seem to use this
        // I'd assume this would indicate that you also automatically accept this quest when getting to this step?

        if ((currentObjectiveCount <= 0) && quest.Owner.Quests.HasQuestCompleted(QuestId))
            SetObjective(quest, 1);

        return GetObjective(quest) > 0;
    }
}
