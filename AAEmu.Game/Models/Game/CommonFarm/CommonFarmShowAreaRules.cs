namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// Pure rules for the public-farm show-area response element list (SC 0x220).
/// </summary>
/// <remarks>
/// The wire carries a <b>signed</b> 32-bit count followed by that many quantized positions. The
/// count is signed on the wire, so a negative value is not a large unsigned loop — it means "no
/// elements". A writer that treated it as unsigned, or that ignored it and wrote a fixed body,
/// would put a different number of bytes on the wire than the client reads.
/// </remarks>
public static class CommonFarmShowAreaRules
{
    /// <summary>
    /// Upper bound on positions honoured in one response. It matches the writer's own clamp, so a
    /// well-behaved caller never reaches it and a hostile count is bounded instead of looping.
    /// </summary>
    public const int MaxPositionCount = 128;

    /// <summary>
    /// Resolves the element count that may actually be written.
    /// </summary>
    /// <param name="requestedCount">Signed count the caller intends to send.</param>
    /// <param name="availableCount">Number of positions the caller actually has.</param>
    /// <param name="boundedCount">The count that may be written, never negative and never past
    /// <paramref name="availableCount"/>.</param>
    /// <returns>
    /// <c>false</c> when <paramref name="requestedCount"/> is negative — the caller must refuse
    /// rather than loop, because a negative count is a malformed request, not an empty one.
    /// </returns>
    public static bool TryResolveCount(int requestedCount, int availableCount, out int boundedCount)
    {
        boundedCount = 0;

        // Signed count: a negative value is refused outright. Treating it as "no elements" would
        // silently accept a malformed caller, and treating it as unsigned would be a huge loop.
        if (requestedCount < 0)
            return false;

        var clamped = Math.Min(requestedCount, MaxPositionCount);

        // Never claim more elements than exist, or the client would read past the end of the body.
        boundedCount = Math.Min(clamped, Math.Max(availableCount, 0));
        return true;
    }
}
