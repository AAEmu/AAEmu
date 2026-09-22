namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Point supplies with one content amount: quest_act_supply_leadership_points.point (127 rows, 1 to
/// 300), quest_act_supply_actabilities.point (633 rows, 10 to 5000) and
/// quest_act_supply_local_lps.local_lp (2 rows, 100 and 150). Content has no zero or negative row;
/// a non-positive amount grants nothing rather than draining the character.
/// </summary>
public static class QuestSupplyPointRules
{
    public static int Grant(int point) => point > 0 ? point : 0;
}
