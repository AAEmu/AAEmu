using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpc(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActConAcceptNpc");

        if (character.CurrentTarget is null or not Npc)
            return false;

        quest.QuestAcceptorType = QuestAcceptorType.Npc;
        quest.AcceptorId = NpcId;

        // Current target is the expected?
        return character.CurrentTarget.TemplateId == NpcId;
    }

    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptNpc({DetailId}).RunAct: Quest: {quest.TemplateId}, NpcId {NpcId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Npc && quest.AcceptorId == NpcId;
    }
}
