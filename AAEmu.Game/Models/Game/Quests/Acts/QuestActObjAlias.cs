using System.Diagnostics.CodeAnalysis;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjAlias(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public string Name { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjAlias");

        return character.Quests.IsQuestComplete(ParentQuestTemplate.Id);
    }

    /// <summary>
    /// Only used client-side to display extra quest information
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>True</returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActObjAlias({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");
        return true;
    }
}
