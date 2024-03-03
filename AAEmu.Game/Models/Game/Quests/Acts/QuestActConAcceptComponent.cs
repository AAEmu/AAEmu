using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptComponent : QuestActTemplate
{
    public uint QuestContextId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActConAcceptComponent: QuestContextId {QuestContextId}");
        return false;
    }
}
