using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used? Assumed to start a quest when a item is equipped?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptItemEquip(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptItemEquip: ItemId {ItemId}");
        return false;
    }

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Warn($"QuestActConAcceptItemEquip({DetailId}).RunAct: Quest: {quest.TemplateId}, ItemId {ItemId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Item && quest.AcceptorId == ItemId;
    }
}
