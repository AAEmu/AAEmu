using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds local_lp to the account's server-local ("online") labor pool
/// (quest_act_supply_local_lps, 2 rows: 100 on quest 11141, 150 on festival quest 11175). The client
/// reader LoadQuestActSupplyLocalLpDescs (x2game-dev.dll FUN_39d43ee0) reads id and local_lp.
/// Character.AddLocalLaborPower is the one path that raises that pool; it clamps to
/// premium_grades.max_local_labor and sends the labor delta.
/// </summary>
public class QuestActSupplyLocalLp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int LocalLp { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), LocalLp {LocalLp}");
        var amount = QuestSupplyPointRules.Grant(LocalLp);
        if (amount > 0 && quest.Owner is Character player)
            player.AddLocalLaborPower(amount);
        return true;
    }
}
