using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyLp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int LaborPower { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActSupplyLp, LaborPower: {LaborPower}");
        character.ChangeLabor((short)LaborPower, 0);
        return true;
    }
}
