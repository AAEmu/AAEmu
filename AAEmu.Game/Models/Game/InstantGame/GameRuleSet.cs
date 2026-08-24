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

    public int VictoryScore { get; set; }
    public uint BattlefieldId { get; set; }

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