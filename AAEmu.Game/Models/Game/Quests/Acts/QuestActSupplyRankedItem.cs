using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: gives item_id x count at grade_id when the quest's war or competition finished at rank
/// (quest_act_supply_ranked_items, 23 rows on 7 quests; every one of those quests progresses
/// through QuestActObjConquestWar or QuestActObjFactionCompetition, which the server holds open
/// because it has no war result or competition ranking). Rows 1 to 16 give 43779 x8, x4, x2, x1
/// for ranks 1 to 4 on quests 6572 and 9025 to 9027; rows 18 to 26 give 47674 x24, x20, x16 on
/// 9871 and 9897. The client reader LoadQuestActSupplyRankedItemDescs (x2game-dev.dll FUN_39d42fb0)
/// reads id, count, grade_id, item_id, rank. Without a rank to compare the accept is refused
/// (QuestRewardSupportRules) and a quest that still reaches this act reports once and completes
/// without the item.
/// </summary>
public class QuestActSupplyRankedItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IUnsupportedRewardAct
{
    public int Rank { get; set; }
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }

    public string MissingSubsystem => "conquest war and faction competition ranking";

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no {1}); quest {2} for {3} completes without that reward",
                QuestActTemplateName, MissingSubsystem, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Rank {Rank}, ItemId {ItemId}, Count {Count} skipped");
        return true;
    }
}
