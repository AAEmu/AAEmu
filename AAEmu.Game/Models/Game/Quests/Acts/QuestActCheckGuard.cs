using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckGuard : QuestActTemplate
{
    public uint NpcId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn($"QuestActCheckGuard: NpcId {NpcId}");
        return false;
    }
}
