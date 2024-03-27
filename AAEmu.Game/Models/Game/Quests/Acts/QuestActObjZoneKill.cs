using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjZoneKill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int CountPlayerKill { get; set; }
    public int CountNpc { get; set; }
    /// <summary>
    /// This is actually the ZoneGroupId
    /// </summary>
    public uint ZoneId { get; set; }
    public bool TeamShare { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public int LvlMin { get; set; }
    public int LvlMax { get; set; }
    public bool IsParty { get; set; }
    public int LvlMinNpc { get; set; }
    public int LvlMaxNpc { get; set; }
    public uint PcFactionId { get; set; }
    public bool PcFactionExclusive { get; set; }
    public uint NpcFactionId { get; set; }
    public bool NpcFactionExclusive { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjZoneKill");

        if (character.Transform.ZoneId != ZoneId)
        {
            return false;
        }

        return objective >= CountNpc || objective >= CountPlayerKill;
    }
}
