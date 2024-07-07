using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyJuryPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Point { get; set; } // is 1 for all entries

    /// <summary>
    /// Adds Jury Points (trials served?)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Point {Point}");
        if (quest.Owner is Character player)
        {
            player.JuryPoint += Point;
            player.SendPacket(new SCJuryPointChangedPacket(player.JuryPoint));
        }
        return true;
    }
}
