using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportNpc(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActConReportNpc");

        if (character.CurrentTarget is not Npc npc)
            return false;

        return npc.TemplateId == NpcId;
    }

    /// <summary>
    /// Checks if the current target is the specified Npc
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActConReportNpc({DetailId}).RunAct: Quest: {quest.TemplateId}, NpcId {NpcId}");
        return (quest.Owner.CurrentTarget is Npc npc) && (npc.TemplateId == NpcId);
    }
}
