using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpcKill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptNpcKill: NpcId {NpcId}");

        if (character.CurrentTarget is not Npc npc)
            return false;

        quest.QuestAcceptorType = QuestAcceptorType.Npc;
        quest.AcceptorId = NpcId;

        return npc.TemplateId == NpcId;
    }

    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Warn("QuestActConAcceptNpcKill({DetailId}).RunAct: Quest: {quest.TemplateId}, NpcId {NpcId}");
        return quest.QuestAcceptorType == QuestAcceptorType.Npc && quest.AcceptorId == NpcId;
    }
}
