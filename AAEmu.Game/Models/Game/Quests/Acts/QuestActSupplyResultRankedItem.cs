using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: gives item_id x count at grade_id when the faction competition ended with result (won
/// or lost) at rank (quest_act_supply_result_ranked_items, 13 rows on quests 10752, 10569, 11132
/// and 11133, each progressing through QuestActObjFactionCompetition, which the server holds open
/// because nothing scores faction competitions). Rows 9 to 11 give 51578 x15 on a win at rank 1
/// and x5 on a loss at ranks 2 and 3 for quest 11132. The client reader
/// LoadQuestActSupplyResultRankedItemDescs reads id, count, grade_id,
/// item_id, rank, result. Without a result the accept is refused (QuestRewardSupportRules) and a
/// quest that still reaches this act reports once and completes without the item.
/// </summary>
public class QuestActSupplyResultRankedItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IUnsupportedRewardAct
{
    public bool Result { get; set; }
    public int Rank { get; set; }
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }

    public string MissingSubsystem => "faction competition result";

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no {1}); quest {2} for {3} completes without that reward",
                QuestActTemplateName, MissingSubsystem, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Result {Result}, Rank {Rank}, ItemId {ItemId}, Count {Count} skipped");
        return true;
    }
}
