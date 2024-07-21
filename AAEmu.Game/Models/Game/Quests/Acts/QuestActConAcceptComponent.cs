using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptComponent(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestContextId { get; set; }

    /// <summary>
    /// This quest starter seems to always reference itself and assumes the quest was started in some other way?
    /// Seems to be mostly used to start "help kill xxx" quests using engage_combat_give_quest_id from npcs, but also some instances of item gains
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Warn($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), QuestContextId {QuestContextId}");
        // TODO: We don't do any actual checks here, just return true. Later maybe could check if the acceptor type is valid?
        return true;
    }
}
