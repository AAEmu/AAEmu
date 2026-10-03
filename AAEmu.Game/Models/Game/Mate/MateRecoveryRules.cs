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
    /// <para>
    /// The owned row records the bars the mate had when it was last captured, and records nothing until
    /// the mate has been summoned once — which is <see cref="Unrecorded"/>, a state of its own, and not
    /// a number. A row that has never recorded a bar starts the mate at the maximum its content
    /// reconstructs right now.
    /// </para>
    /// <para>
    /// Making that a separate state rather than the number 0 is the whole point. Zero is a real bar: a
    /// mate that was captured dead, or at empty mana, has to come back that way, and reading its zero as
    /// "never recorded" restored it to full health and full mana. Reading it as 0 instead resurrects a
    /// dead mate, which is why the placeholder is its own value and the column is nullable.
    /// </para>
    /// <para>
    /// A recorded value is kept when it is inside the maximum and cut down to it when it is not: a mate
    /// that levelled while it was despawned, or that was saved against a smaller maximum, must never
    /// come back carrying more health or mana than it can hold, and every recovery that lands on it
    /// afterwards clamps against the same maximum.
    /// </para>
    /// </remarks>
    /// <param name="recordedPoints">
    /// What the owned row holds for this bar, or <see cref="Unrecorded"/> when the row has never held
    /// one. Zero is a real reading and is restored as zero.
    /// </param>
    /// <param name="maximum">The maximum this bar is reconstructed to, which must not be negative.</param>
    public static int RestorePoints(MateBar bar, int? recordedPoints, int maximum)
    {
        if (maximum < 0)
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "A mate maximum cannot be negative.");

        // Never recorded: start at the reconstructed maximum.
        if (recordedPoints is not { } recorded || recorded == Unrecorded)
            return maximum;

        var restored = Math.Min(recorded, maximum);

        // A recorded 0 health is the one recorded value that cannot be honoured. A dead mate is a
        // dead end: its regen tick returns early while it is dead, mounting refuses a dead mate, and
        // resurrection only handles characters, so nothing reads the mate revive settings and nothing
        // brings it back. Restoring 0 would leave an owned mate permanently unusable.
        //
        // Mana is not affected - an empty mana bar regens on its own, and restoring it as 0 is honest.
        // Health is restored to the maximum until a mate revive exists to carry the real bar through;
        // that is a deliberate stand-in for a missing feature, not the recorded value.
        if (bar == MateBar.Health && restored <= 0)
            return maximum;

        return restored;
    }

    /// <summary>Which of a mate's two recovery bars a restore is about.</summary>
    public enum MateBar
    {
        /// <summary>The health bar. A recorded zero means the mate was captured dead.</summary>
        Health = 0,

        /// <summary>The mana bar. A recorded zero is an empty bar that regens back on its own.</summary>
        Mana = 1
    }

    /// <summary>
    /// What the owned row holds for a bar it has never recorded. Distinct from zero, which is a bar
    /// that really was empty.
    /// </summary>
    /// <remarks>
    /// The column is nullable, so the database's own NULL is this state; this constant is what the
    /// in-memory record carries and what the writer stores, and the read maps NULL onto it.
    /// </remarks>
    public const int Unrecorded = -1;
}
