using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Models.Game.InstantGame;

public class GameRuleSet
{
    public uint Id { get; set; }
    public int TimeEnding { get; set; }
    public int TimePlaying { get; set; }

    /// <summary>
    /// Seconds the match holds its players once everyone has arrived, before the countdown to the
    /// opening bell. Zero starts the countdown as soon as the last player is in.
    /// </summary>
    public int TimeReady { get; set; }

    /// <summary>Seconds between a death and its respawn at the corps spawn (content: time_resurrection_delay).</summary>
    public int TimeResurrectionDelay { get; set; }

    public int VictoryScore { get; set; }

    /// <summary>content: victory_kill_count; 0 disables the kill-count ending.</summary>
    public int VictoryKillCount { get; set; }

    /// <summary>content: victory_by_score; picks score (true) or kill count (false) as the time-over decider.</summary>
    public bool VictoryByScore { get; set; }

    public uint BattlefieldId { get; set; }

    /// <summary>
    /// The rule set's <c>game_score_rules</c> rows. Every scoring decision goes through these;
    /// a rule set shipped without rows scores nothing and reports the miss.
    /// </summary>
    public IReadOnlyList<GameScoreRule> ScoreRules { get; set; } = [];

    /// <summary>Points this rule set awards for one scoring event. False when no row matches.</summary>
    public bool TryGetEventScore(GameScoreEventKind kind, int eventValue, int corps, uint eventTagId,
        out int score) =>
        InstantGameScoreRules.TryGetScore(ScoreRules, kind, eventValue, corps, eventTagId, out score);

    // 10.0.2.13: corps_size, corps1_faction_id, corps2_faction_id, time_opening were removed from game_rule_sets.
    // The v10 battlefield corps/faction model differs and has no per-ruleset corps factions yet. These are
    // documented stubs (distinct faction ids so the corps dictionary keys don't collide) that keep the
    // instant-game subsystem compiling and running with current behavior. TODO(v10): redesign battlefield corps.
    public uint Corps1FactionId { get; set; } = 1;
    public uint Corps2FactionId { get; set; } = 2;
    public int CorpsSize { get; set; } = 1;
    public int TimeOpening { get; set; }
    // TODO fill out the rest of the fields
}
