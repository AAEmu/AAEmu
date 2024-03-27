using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckDistance(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public bool WithIn { get; set; }
    public uint NpcId { get; set; }
    public int Distance { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActCheckDistance: WithIn {WithIn}, NpcId {NpcId}, Distance {Distance}");
        return false;
    }
}
