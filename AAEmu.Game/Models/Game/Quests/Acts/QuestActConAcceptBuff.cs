using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptBuff(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint BuffId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptBuff: BuffId {BuffId}");
        return false;
    }
}
