namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>
/// The score each side has to reach to win a siege, read from <c>content_configs</c>:
/// <c>siege_defense_win_point</c>, <c>siege_offense_win_point</c> and <c>siege_outlaw_win_point</c>.
/// </summary>
public readonly record struct SiegeWinPoints(uint Defense, uint Offense, uint Outlaw)
{
    public uint For(SiegeScoreSide side) => side switch
    {
        SiegeScoreSide.Defense => Defense,
        SiegeScoreSide.Offense => Offense,
        SiegeScoreSide.Outlaw => Outlaw,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown siege score side."),
    };

    public string KeyFor(SiegeScoreSide side) => SiegeContentConfigKeys.KeyFor(side);
}

/// <summary>The <c>content_configs</c> keys the siege score and settlement read.</summary>
public static class SiegeContentConfigKeys
{
    public const string DefenseWinPoint = "siege_defense_win_point";
    public const string OffenseWinPoint = "siege_offense_win_point";
    public const string OutlawWinPoint = "siege_outlaw_win_point";

    public static string KeyFor(SiegeScoreSide side) => side switch
    {
        SiegeScoreSide.Defense => DefenseWinPoint,
        SiegeScoreSide.Offense => OffenseWinPoint,
        SiegeScoreSide.Outlaw => OutlawWinPoint,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown siege score side."),
    };
}

/// <summary>How one siege ended. Persisted with the settlement so a replay of the same cycle cannot change it.</summary>
public enum SiegeOutcome : byte
{
    /// <summary>Neither attacker reached its win point, so the defender held the ground - the shipped guide's defense condition.</summary>
    DefenseHeld = 1,

    /// <summary>The attacking alliance reached its win point and takes the dominion.</summary>
    OffenseBrokeThrough = 2,

    /// <summary>The raider alliance reached its win point and takes the dominion.</summary>
    OutlawBrokeThrough = 3,

    /// <summary>Two sides reached their win points in the same siege. Not a shipped case, so nothing is settled and the owner stands.</summary>
    Contested = 4,
}

/// <summary>What the settlement decided, and why - the reason is logged and stored, never parsed back.</summary>
/// <param name="Outcome">How the siege ended.</param>
/// <param name="DefenderFactionId">The alliance that held the ground during the siege.</param>
/// <param name="WinnerFactionId">
/// The alliance that took the dominion, or 0 when nobody did - the defender held it, or the siege was
/// contested. Zero is what siege_settlements stores, so the stored winner is never the alliance that simply
/// kept what it had.
/// </param>
/// <param name="Reason">Human-readable explanation, stored and logged.</param>
public readonly record struct SiegeSettlementDecision(
    SiegeOutcome Outcome,
    uint DefenderFactionId,
    uint WinnerFactionId,
    string Reason)
{
    /// <summary>Whether the dominion's ownership changes as a result.</summary>
    public bool ChangesOwner => Outcome is SiegeOutcome.OffenseBrokeThrough or SiegeOutcome.OutlawBrokeThrough;
}

/// <summary>
/// The siege settlement rules, as pure functions over a score state and the content win points.
/// </summary>
/// <remarks>
/// The shipped <c>ui_texts.siege_score_guide</c> states the three win conditions: the attacking side must
/// purify a set amount of the guard tower's magic power with its own faction's power, the raider must destroy
/// a set amount of it, and the defending side must prevent the purification and destruction until the siege
/// ends. Each attacker has its own configured win point, so an attacker that reached it wins, and a siege in
/// which neither did is held by the defender.
/// <para>
/// The defense counter is deliberately not consulted: the defender's win point is the magic power it kept,
/// which is the total of what the two attackers did not take. That total belongs to the guard-tower runtime
/// that produces the score, so asking this rule to derive it here would invent a tower size.
/// </para>
/// </remarks>
public static class SiegeScoreRules
{
    public static bool Reached(SiegeScoreState state, SiegeWinPoints winPoints, SiegeScoreSide side) =>
        state.For(side) >= winPoints.For(side);

    /// <summary>
    /// The side that reached its win point, or null when none did. Two sides reaching theirs in the same
    /// siege is reported as <see cref="SiegeOutcome.Contested"/> by <see cref="Resolve"/> rather than
    /// silently resolved here.
    /// </summary>
    public static SiegeScoreSide? ReachedSide(SiegeScoreState state, SiegeWinPoints winPoints)
    {
        var outlaw = Reached(state, winPoints, SiegeScoreSide.Outlaw);
        var offense = Reached(state, winPoints, SiegeScoreSide.Offense);
        return (outlaw, offense) switch
        {
            (false, false) => null,
            (true, false) => SiegeScoreSide.Outlaw,
            (false, true) => SiegeScoreSide.Offense,
            _ => null,
        };
    }

    public static SiegeSettlementDecision Resolve(
        SiegeScoreState state,
        SiegeWinPoints winPoints,
        SiegeFactionRoles roles,
        uint defenderFactionId)
    {
        var defender = roles.RequireDefender(defenderFactionId);

        var outlawReached = Reached(state, winPoints, SiegeScoreSide.Outlaw);
        var offenseReached = Reached(state, winPoints, SiegeScoreSide.Offense);

        if (outlawReached && offenseReached)
        {
            return new SiegeSettlementDecision(SiegeOutcome.Contested, defender, 0,
                $"both the raider ({state.OutlawPoint}/{winPoints.Outlaw}) and the attacking alliance " +
                $"({state.OffensePoint}/{winPoints.Offense}) reached their win point; ownership unchanged");
        }

        if (outlawReached)
        {
            return new SiegeSettlementDecision(SiegeOutcome.OutlawBrokeThrough, defender, roles.RaiderFactionId,
                $"raider {roles.RaiderFactionId} destroyed {state.OutlawPoint} of {winPoints.Outlaw} " +
                "guard-tower magic power");
        }

        if (offenseReached)
        {
            var winner = roles.OffenseAgainst(defender);
            return new SiegeSettlementDecision(SiegeOutcome.OffenseBrokeThrough, defender, winner,
                $"alliance {winner} purified {state.OffensePoint} of {winPoints.Offense} " +
                "guard-tower magic power");
        }

        // No winner is recorded when the dominion does not change hands: siege_settlements stores 0 for a
        // defended or contested siege, and the alliance that held the ground is in defender_faction_id.
        return new SiegeSettlementDecision(SiegeOutcome.DefenseHeld, defender, 0,
            $"neither attacker reached its win point (offense {state.OffensePoint}/{winPoints.Offense}, " +
            $"outlaw {state.OutlawPoint}/{winPoints.Outlaw}); alliance {defender} held the dominion");
    }
}
