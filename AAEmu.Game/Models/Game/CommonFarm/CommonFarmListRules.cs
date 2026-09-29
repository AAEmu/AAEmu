namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// Pure rules for the public-farm list response (the answer to a farm-list request).
/// </summary>
/// <remarks>
/// <para>
/// The body is <c>u32 maxCount</c>, then a <b>signed</b> <c>s32 count</c>, then that many crop
/// records. The count is signed on the wire, so a negative or zero value carries no records at all
/// and is not a very large loop.
/// </para>
/// <para>
/// The reader stops at <see cref="MaxRecordCount"/> records however large the count says, so a
/// server that writes more desynchronises the client instead of showing a longer list. The bound
/// belongs on the write side, and a list that hits it is reported rather than quietly truncated:
/// a player who has planted more crops than one response can carry should be visible in the log,
/// not silently shown a shorter list than they own.
/// </para>
/// </remarks>
public static class CommonFarmListRules
{
    /// <summary>
    /// Largest number of crop records one response may carry. It matches the bound the reader
    /// applies, so a well-behaved server never reaches it and an oversized list is cut rather than
    /// allowed to run the client past the end of the body.
    /// </summary>
    public const int MaxRecordCount = 64;

    /// <summary>
    /// Resolves the record count that may actually be written.
    /// </summary>
    /// <param name="requestedCount">Signed record count the caller intends to send.</param>
    /// <param name="availableCount">Number of records the caller actually has.</param>
    /// <param name="boundedCount">
    /// The count that may be written: never negative, never past <paramref name="availableCount"/>,
    /// and never past <see cref="MaxRecordCount"/>.
    /// </param>
    /// <param name="truncated">
    /// <c>true</c> when records were dropped because the response bound was reached, so the caller
    /// can say so instead of presenting a shortened list as the whole one.
    /// </param>
    /// <returns>
    /// <c>false</c> when <paramref name="requestedCount"/> is negative. The caller must refuse rather
    /// than loop, because a negative count is a malformed response, not an empty one.
    /// </returns>
    public static bool TryResolveCount(
        int requestedCount, int availableCount, out int boundedCount, out bool truncated)
    {
        boundedCount = 0;
        truncated = false;

        // Signed count: a negative value is refused outright. Treating it as "no records" would
        // silently accept a malformed caller, and treating it as unsigned would be a huge loop.
        if (requestedCount < 0)
            return false;

        var available = Math.Max(availableCount, 0);

        // Bounded twice, and in this order. Clamping to the bound alone would let a caller claim
        // more records than it holds and fill the remainder with whatever followed the list; taking
        // the smaller of the two is what keeps the body self-consistent.
        boundedCount = Math.Min(requestedCount, Math.Min(available, MaxRecordCount));
        truncated = boundedCount < available;
        return true;
    }
}
