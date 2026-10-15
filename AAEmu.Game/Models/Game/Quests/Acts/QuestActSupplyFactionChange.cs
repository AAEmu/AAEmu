using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: moves the character to system_faction_id (161 pirates, 199/200 the exile factions under Haranya
/// and Nuia, 148; 0 = back to the faction it had before its first exile) through the exile system, which
/// checks the route, its weekly slots and the faction change cooldown. ignore_limit skips the slots and the
/// cooldown; inferior_escape lets a member of an inferior faction leave on a full route.
/// </summary>
public class QuestActSupplyFactionChange(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SystemFactionId { get; set; }
    public bool IgnoreLimit { get; set; }
    public bool InferiorEscape { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        var owner = quest.Owner as Character;
        var before = owner?.Faction?.Id;
        var result = FactionExileManager.Instance.Exile(owner, SystemFactionId, IgnoreLimit, InferiorEscape, false);
        Logger.Info("{0}({1}): quest {2} for {3}: {4}", QuestActTemplateName, DetailId, quest.TemplateId, owner?.Name, result);
        if (owner != null && owner.Faction?.Id == before)
            owner.SendMessage($"Exile failed: {result}");
        return true;
    }
}
