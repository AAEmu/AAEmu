namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>
/// One settled siege: how a zone group's cycle ended, with the scores it ended on.
/// </summary>
/// <remarks>
/// Written once per (zone group, siege cycle). The unique key on those two columns is what makes the
/// settlement idempotent: a World restart between the phase change and the write, or a second tick that
/// sees the same transition, finds the row already there and leaves the outcome alone.
/// </remarks>
public sealed record SiegeSettlementRecord
{
    public required ushort ZoneGroupId { get; init; }

    /// <summary>The <c>siege_plans.week_start</c> of the cycle that was fought - the key with the zone group.</summary>
    public required DateTime CycleWeekStart { get; init; }

    public required DateTime SettledAtUtc { get; init; }

    /// <summary>The scores as they stood when the siege ended.</summary>
    public required SiegeScoreState Score { get; init; }

    public required SiegeOutcome Outcome { get; init; }

    public required uint DefenderFactionId { get; init; }

    /// <summary>The alliance that took the dominion, or 0 when the defender held it or the siege was contested.</summary>
    public required uint WinnerFactionId { get; init; }

    /// <summary>Human-readable explanation, stored so an operator reading the table does not have to re-derive it.</summary>
    public required string Reason { get; init; }
}
