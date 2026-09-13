using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>Awards guild experience through the normal capped, persisted expedition path.</summary>
public class QuestActSupplyExpeditionExp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint Point { get; set; }

    internal Func<Character, uint, bool> AddExp { get; init; } =
        (character, point) => character.Expedition != null &&
                              ExpeditionManager.Instance.AddExp(character.Expedition, point);

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (quest.Owner is Character character && Point > 0)
            AddExp(character, Point);
        return true;
    }
}
