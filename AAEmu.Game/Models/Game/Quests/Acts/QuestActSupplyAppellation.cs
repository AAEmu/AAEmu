using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyAppellation : QuestActTemplate
{
    public uint AppellationId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActSupplyAppellation, AppellationId: {AppellationId}");
        character.Appellations.Add(AppellationId);
        return true;
    }
}
