using System.Diagnostics.CodeAnalysis;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// This Template does not seem to be used for server mechanics
/// </summary>
public class QuestActObjAlias : QuestActTemplate
{
    public string Name { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjAlias");

        return character.Quests.IsQuestComplete(ParentQuestTemplate.Id);
    }
}
