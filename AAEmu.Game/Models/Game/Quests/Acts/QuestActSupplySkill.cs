using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplySkill : QuestActTemplate
{
    public uint SkillId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        // TODO: Save the new skill somewhere maybe? There is no active quest that seems to be using this.
        Logger.Warn($"QuestActSupplySkill, SkillId: {SkillId}");
        return true;
    }
}
