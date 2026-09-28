namespace AAEmu.Game.Models.Game.Mate;

/// <summary>
/// How a mate's own recorded recovery state is read back onto a freshly summoned mate.
/// </summary>
public static class MateRecoveryRules
{
    /// <summary>
    /// The points one bar starts a summon with.
    /// </summary>
    /// <remarks>
    /// The owned row records the bars the mate had when it was last captured, and it records nothing at
    /// all — zero on both bars — until the mate has been summoned once, so a recorded zero means "never
    /// recovered" and the mate starts at the maximum its content reconstructs right now. A recorded
    /// value is kept when it is inside that maximum and cut down to it when it is not: a mate that
    /// levelled while it was despawned, or that was saved against a smaller maximum, must never come back
    /// carrying more health or mana than it can hold, and every recovery that lands on it afterwards
    /// (regeneration, a recovery item, a recovery skill) clamps against the same maximum.
    /// </remarks>
    /// <param name="recordedPoints">What the owned row holds for this bar.</param>
    /// <param name="maximum">The maximum this bar is reconstructed to, which must not be negative.</param>
    public static int RestorePoints(int recordedPoints, int maximum)
    {
        if (maximum < 0)
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "A mate maximum cannot be negative.");

        return recordedPoints > 0 ? Math.Min(recordedPoints, maximum) : maximum;
    }
}
