using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjDistance(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public bool WithIn { get; set; }
    public uint NpcId { get; set; }
    public int Distance { get; set; }
    public uint HighlightDoodadId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Actually check the distance?
        Logger.Debug("QuestActObjDistance");
        return false;
    }
}
