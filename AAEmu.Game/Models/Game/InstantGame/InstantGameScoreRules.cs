using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// Resolves a scoring event against a rule set's <c>game_score_rules</c> rows (pure, testable).
///
/// Resolution follows the shipped content: rows are keyed by event id + event value + event tag,
/// and a row may be side-agnostic (<see cref="AnyCorps"/>, the table's -1 default) or bound to one
/// side. A side-bound row for the scoring side wins over the side-agnostic row; a tag-conditioned
/// row only matches when the caller supplies the same tag. A rule set with no matching row scores
/// nothing — the caller reports the miss loudly instead of inventing a value.
/// </summary>
public static class InstantGameScoreRules
{
    /// <summary><c>game_score_rules.rule_set_corps</c> value meaning "applies to every side".</summary>
    public const int AnyCorps = -1;

    public static bool TryGetScore(IReadOnlyList<GameScoreRule> rules, GameScoreEventKind kind,
        int eventValue, int corps, uint eventTagId, out int score)
    {
        score = 0;
        if (rules == null)
            return false;

        var anySideMatch = false;
        foreach (var rule in rules)
        {
            if (rule.EventId != (int)kind || rule.EventValue != eventValue || rule.EventTagId != eventTagId)
                continue;

            if (rule.RuleSetCorps == corps)
            {
                // The scoring side's own row is authoritative; content never pairs it with a
                // second row for the same side + event + value + tag.
                score = rule.EventScore;
                return true;
            }

            if (rule.RuleSetCorps == AnyCorps)
            {
                score = rule.EventScore;
                anySideMatch = true;
            }
        }

        return anySideMatch;
    }
}
