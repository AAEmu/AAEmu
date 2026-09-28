namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// What the farm expiry pass decided about one planted crop.
/// </summary>
public enum CommonFarmExpiryDecision
{
    /// <summary>The protection window has run out; the crop leaves the farm.</summary>
    Expire = 0,

    /// <summary>The protection window is still open, or it has not started yet.</summary>
    Keep,

    /// <summary>
    /// Content defines no protection window for this crop's doodad group, so its age cannot be
    /// compared against anything. The crop is left alone rather than retired on a number nobody wrote.
    /// </summary>
    GuardTimeNotConfigured
}

/// <summary>
/// The decision the farm expiry pass makes for a single planted crop, as a pure function.
/// </summary>
/// <remarks>
/// <para>
/// Every minute the world asks the same question about every planted crop: has the protection
/// window run out? The answer is a comparison between content — how long a crop of this doodad
/// group stays protected — and the clock.
/// </para>
/// <para>
/// The two inputs have to stay separable, because either can be missing and neither may be
/// invented. Content that names no <c>doodad_groups</c> row for the crop's group leaves the window
/// length unknown, and a crop of unknown age is not an old crop: answering "expired" for it retires
/// a crop the moment it is planted, which is the exact opposite of a protection window. A
/// <c>false</c> capacity is likewise not a zero capacity, and both facts are stated here rather than
/// collapsed into a number at the call site.
/// </para>
/// </remarks>
public static class CommonFarmExpiryRules
{
    /// <summary>
    /// Decides what happens to one planted crop.
    /// </summary>
    /// <param name="guardTimeConfigured">
    /// Whether content supplies a protection window for this crop's doodad group. <c>false</c> means
    /// the window's length is unknown, which is not the same as a window of zero length.
    /// </param>
    /// <param name="guardSeconds">The configured protection window, in seconds.</param>
    /// <param name="plantTimeUtc">When the crop was planted.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>
    /// <see cref="CommonFarmExpiryDecision.GuardTimeNotConfigured"/> when the window length is
    /// unknown, <see cref="CommonFarmExpiryDecision.Expire"/> once the window has run out, and
    /// <see cref="CommonFarmExpiryDecision.Keep"/> for a crop that is still inside it.
    /// </returns>
    public static CommonFarmExpiryDecision Evaluate(
        bool guardTimeConfigured, uint guardSeconds, DateTime plantTimeUtc, DateTime nowUtc)
    {
        // Asked before the clock, because there is nothing to compare the clock against. Answering
        // "expired" here would retire the crop on the very next pass instead of protecting it.
        if (!guardTimeConfigured)
            return CommonFarmExpiryDecision.GuardTimeNotConfigured;

        // A crop with no planting time has no age, so it cannot be shown to be past its window.
        // Treating the unset value as "planted long ago" expires it on the first pass after a load
        // that left the column empty.
        if (plantTimeUtc == default)
            return CommonFarmExpiryDecision.Keep;

        var protectedUntil = plantTimeUtc.AddSeconds(guardSeconds);

        // The window has run out strictly after its end, so a crop planted exactly at the boundary
        // is still protected for that instant.
        return nowUtc > protectedUntil
            ? CommonFarmExpiryDecision.Expire
            : CommonFarmExpiryDecision.Keep;
    }
}
