using System.Diagnostics.CodeAnalysis;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// This Template is not used for server mechanics, but rather contains the descriptions used for quest descriptions
/// </summary>
public class QuestActObjAlias(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public string Name { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjAlias");

        return character.Quests.IsQuestComplete(ParentQuestTemplate.Id);
    }

    /// <summary>
    /// Should never get called, return true regardless as it does not contain any actions
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"QuestActObjAlias({DetailId}).RunAct: Quest: {quest.TemplateId}");
        return true;
    }
}
