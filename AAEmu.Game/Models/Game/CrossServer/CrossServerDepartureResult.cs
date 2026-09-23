namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// Result of a departure attempt: the refusal/grant verdict plus the values the caller must
/// mirror onto the live character so a later <c>Character.Save()</c> cannot erase the park.
/// </summary>
/// <param name="Outcome">Grant or refusal; see <see cref="CrossServerTransferOutcome"/>.</param>
/// <param name="TargetServerKey">Resolved destination key (empty when the target was the problem).</param>
/// <param name="ParkedAtUtc">The park timestamp written into the journal; identical to the value the caller passed in.</param>
public sealed record CrossServerDepartureResult(
    CrossServerTransferOutcome Outcome,
    string TargetServerKey,
    DateTime ParkedAtUtc);
