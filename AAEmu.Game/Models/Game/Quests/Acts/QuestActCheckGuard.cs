using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckGuard(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Warn($"QuestActCheckGuard: NpcId {NpcId}");
        return false;
    }

    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Warn($"QuestActCheckGuard({DetailId}).RunAct: Quest {quest.TemplateId}, NpcId {NpcId}");
        // TODO: This seems to be related to escort quests where you need to protect the NPC
        // TODO: Implement fail mechanics if they die?
        return true;
    }
}
