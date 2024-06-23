using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used? Assumed to start a quest when an item is equipped?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptItemEquip(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Item && quest.AcceptorId == ItemId;
    }
}
