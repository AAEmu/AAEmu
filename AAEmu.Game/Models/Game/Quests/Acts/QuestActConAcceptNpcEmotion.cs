using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpcEmotion(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public string Emotion { get; set; }

    /// <summary>
    /// Verifies that the NPC from the quest starter is valid, does not check the emote
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        // TODO: Somehow check if the emote was correct
        Logger.Warn($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DetailId: {DetailId}, NpcId {NpcId}, Emotion {Emotion}");
        return quest.QuestAcceptorType == QuestAcceptorType.Npc && quest.AcceptorId == NpcId;
    }
}
