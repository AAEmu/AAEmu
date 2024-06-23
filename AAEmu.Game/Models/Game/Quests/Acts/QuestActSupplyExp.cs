using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyExp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Exp { get; set; }

    /// <summary>
    /// Adds Xp
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Exp {Exp}");
        var player = quest.Owner as Character;
        player?.AddExp(Exp, true);
        return true;
    }
}
