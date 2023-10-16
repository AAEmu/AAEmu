using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplySkill : QuestActTemplate
{
    public uint SkillId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActSupplySkill");
        return true;
    }
}
