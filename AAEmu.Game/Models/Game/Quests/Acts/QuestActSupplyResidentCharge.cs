using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds charge copper to the resident balance of zone_group_id
/// (quest_act_supply_resident_charges, one row: 10000 for zone group 33 on dummy quest 9334). The
/// client reader LoadQuestActSupplyResidentChargeDescs (x2game-dev.dll FUN_39d48470) reads id,
/// charge, zone_group_id. The server keeps no resident balance (SCResidentBalanceInfoPacket is
/// sent with zeros), so the accept is refused (QuestRewardSupportRules) and a quest that still
/// reaches this act reports once and completes without the charge.
/// </summary>
public class QuestActSupplyResidentCharge(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IUnsupportedRewardAct
{
    public uint ZoneGroupId { get; set; }
    public int Charge { get; set; }

    public string MissingSubsystem => "resident balance";

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (QuestUnsupportedProgressActRules.ReportOnce(QuestActTemplateName))
            Logger.Warn("{0} is not supported (no {1}); quest {2} for {3} completes without that reward",
                QuestActTemplateName, MissingSubsystem, quest.TemplateId, quest.Owner.Name);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ZoneGroupId {ZoneGroupId}, Charge {Charge} skipped");
        return true;
    }
}
