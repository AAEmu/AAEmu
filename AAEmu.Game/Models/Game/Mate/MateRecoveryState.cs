namespace AAEmu.Game.Models.Game.Mate;

/// <summary>
/// Snapshot of the three recovery values authored on a summonable mate NPC. The field names
/// deliberately mirror the compact columns; interpreting the delay or applying a recovery is a
/// separate effect-layer responsibility.
/// </summary>
public readonly record struct MateRecoveryState(
    int MateReviveDelay,
    int MateReviveHpPercent,
    int MateReviveMpPercent)
{
    /// <summary>
    /// Restores a persisted profile. Legacy rows may have no profile yet; a partially written
    /// profile is corrupt and must not be completed with guessed values.
    /// </summary>
    public static MateRecoveryState? FromPersisted(
        int? mateReviveDelay,
        int? mateReviveHpPercent,
        int? mateReviveMpPercent)
    {
        if (mateReviveDelay is null && mateReviveHpPercent is null && mateReviveMpPercent is null)
            return null;

        if (mateReviveDelay is null || mateReviveHpPercent is null || mateReviveMpPercent is null)
        {
            throw new InvalidDataException(
                "Mate recovery persistence must contain all three recovery values or none.");
        }

        return new MateRecoveryState(
            mateReviveDelay.Value,
            mateReviveHpPercent.Value,
            mateReviveMpPercent.Value);
    }
}
