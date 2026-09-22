using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// Victory and ending derivation from a match's <c>game_rule_sets</c> fields (pure, testable).
///
/// Evidence — shipped content columns vs the content enum <c>enum_battle_field_ending_reasons</c>:
/// <c>victory_score</c> → achievement_score, <c>victory_kill_count</c> → achievement_kill_count,
/// <c>victory_by_score</c> selects which tally decides a time-over result →
/// timeover_compare_score / timeover_compare_kill_count, and an even time-over is
/// timeover_draw. Threshold columns are disabled at 0 (every shipped battlefield rule set keeps
/// <c>victory_score</c> at 0, so score victories only happen where content asks for one — a
/// 0 threshold must never end the match on the first point).
/// Round-based endings (timeover/achievement_round_win_count, achievement_kill_corps_head,
/// achievement_all_kill_corps, unearned_win) need fields this derivation does not model yet.
/// </summary>
public static class InstantGameResultRules
{
    public static bool IsScoreVictory(int teamScore, int victoryScore) =>
        victoryScore > 0 && teamScore >= victoryScore;

    public static bool IsKillVictory(int teamTotalKills, int victoryKillCount) =>
        victoryKillCount > 0 && teamTotalKills >= victoryKillCount;

    /// <summary>
    /// Time over: which tally decides the match (per <c>victory_by_score</c>) and how it ended.
    /// </summary>
    public static (BattlefieldEndingReason Reason, VictoryState Corps1State, VictoryState Corps2State)
        DeriveTimeOver(int score1, int score2, int kills1, int kills2, bool victoryByScore)
    {
        var key1 = victoryByScore ? score1 : kills1;
        var key2 = victoryByScore ? score2 : kills2;
        if (key1 == key2)
            return (BattlefieldEndingReason.TimeoverDraw, VictoryState.Draw, VictoryState.Draw);

        var reason = victoryByScore
            ? BattlefieldEndingReason.TimeoverCompareScore
            : BattlefieldEndingReason.TimeoverCompareKillCount;
        return key1 > key2
            ? (reason, VictoryState.Win, VictoryState.Lose)
            : (reason, VictoryState.Lose, VictoryState.Win);
    }
}
