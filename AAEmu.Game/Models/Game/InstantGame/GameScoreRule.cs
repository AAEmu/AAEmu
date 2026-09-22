namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// One <c>game_score_rules</c> row: how many points a scoring event awards inside one rule set.
/// Column names and value domains come from the shipped content database (read-only):
/// <c>rule_set_corps</c> is -1 for "any side" and otherwise the side the row applies to,
/// <c>event_id</c> is an <c>enum_game_score_events</c> id, <c>event_value</c> is the event's own
/// context id (quest/doodad/house id for those events, 0 for the plain kill events),
/// <c>event_tag_id</c> is 0 unless the row only applies while a tag is held, and
/// <c>event_score</c> is the points awarded (it may be negative for penalty rows).
/// </summary>
/// <param name="RuleSetCorps">Side this row applies to; <see cref="InstantGameScoreRules.AnyCorps"/> for any side.</param>
/// <param name="EventId"><c>enum_game_score_events.id</c> this row scores.</param>
/// <param name="EventValue">Event context value the row must match.</param>
/// <param name="EventScore">Points awarded by the row.</param>
/// <param name="EventTagId">Required tag, or 0 when the row has no tag condition.</param>
public sealed record GameScoreRule(int RuleSetCorps, int EventId, int EventValue, int EventScore, uint EventTagId);
