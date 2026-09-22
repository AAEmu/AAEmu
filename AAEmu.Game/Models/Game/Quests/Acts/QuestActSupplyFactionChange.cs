using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: moves the character to system_faction_id (quest_act_supply_faction_changes, 27 rows:
/// 161 pirates on 12 rows, 0 on 3 rows of quests gated on being a pirate so back to the birth
/// faction, 199 and 200 the player-nation factions under 149 and 148 on 11 rows, 148 on one), with
/// ignore_limit on 6 rows and inferior_escape on 3. The client reader
/// LoadQuestActSupplyFactionChangeDescs (x2game-dev.dll FUN_39d48070) reads id, ignore_limit,
/// inferior_escape, system_faction_id. The limit the flag skips is faction_change_limit_nums (a
/// count per level band) and the accept-side kinds 123 FactionPower, 124
/// FactionChangePossibleFromTo and 125 FactionChangeCooldown of unit_reqs, none of which the server
/// records; Character.SetPirate exists but nothing calls it and 199 and 200 are not in FactionsEnum.
/// The accept is refused (QuestRewardSupportRules) and a quest that still reaches this act reports
/// once and completes without the change.
/// </summary>
public class QuestActSupplyFactionChange(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IUnsupportedRewardAct
{
    public uint SystemFactionId { get; set; }
    public bool IgnoreLimit { get; set; }
    public bool InferiorEscape { get; set; }

    public string MissingSubsystem => "faction change limits and cooldown";

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no {1}); quest {2} for {3} completes without that reward",
                QuestActTemplateName, MissingSubsystem, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), SystemFactionId {SystemFactionId}, IgnoreLimit {IgnoreLimit}, InferiorEscape {InferiorEscape} skipped");
        return true;
    }
}
