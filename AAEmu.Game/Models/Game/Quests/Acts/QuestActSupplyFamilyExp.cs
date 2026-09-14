using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>Awards family experience when the containing quest component completes.</summary>
public class QuestActSupplyFamilyExp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint Point { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (quest.Owner is Character character)
            FamilyManager.Instance.AddExperience(character, Point);

        return true;
    }
}
