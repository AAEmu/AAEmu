using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// What a farm-area request resolved to, and therefore what the response may say.
/// </summary>
public enum CommonFarmShowAreaOutcome
{
    /// <summary>
    /// The position is farm land of the requested tab. The response names that tab and carries its
    /// planting positions.
    /// </summary>
    Answer = 0,

    /// <summary>
    /// The request is about a farm tab, but there is nothing to show for it here. The response
    /// carries a zero count, which is what makes the reader drop the positions it was holding.
    /// </summary>
    Clear,

    /// <summary>
    /// The position is not farm land, or the request names a tab that does not exist. Nothing is
    /// sent: a response here would describe an area the world does not have.
    /// </summary>
    Refuse
}

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
    /// Decides what a farm-area request may be answered with.
    /// </summary>
    /// <param name="requestedType">The farm tab the request named.</param>
    /// <param name="resolvedType">The farm tab the position actually sits on.</param>
    /// <param name="responseType">
    /// The tab to put in the response. It is the tab the <i>land</i> carries, never the one the
    /// request asked for: a response that echoed the request would file one tab's positions under
    /// another tab's name.
    /// </param>
    /// <returns>The outcome the caller must act on.</returns>
    /// <remarks>
    /// The order of the three answers is the whole rule and each one falls on a different side of
    /// its line:
    /// <list type="number">
    /// <item><description>
    /// A position that is not farm land, or a request naming a tab that is not a farm tab at all,
    /// is <b>refused</b>. Both mean the world has no such area, and the response would be inventing
    /// one. This is asked first because it is the only answer that sends nothing: answering either
    /// one would put an area on the wire that does not exist.
    /// </description></item>
    /// <item><description>
    /// Farm land carrying a <b>different tab than the one requested</b> is <b>cleared</b>, not
    /// answered. The positions that exist belong to another tab, and handing them back would file
    /// them under the requested tab.
    /// </description></item>
    /// <item><description>
    /// Farm land carrying the requested tab is <b>answered</b>. Whether positions actually accompany
    /// it is the count's business: an area with nothing planted writes a zero count, which the
    /// reader treats the same way — drop what you hold. That is the correct answer for an empty
    /// farm, so it needs no separate case here.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static CommonFarmShowAreaOutcome Evaluate(
        int requestedType, int resolvedType, out int responseType)
    {
        responseType = 0;

        // Refused side: nothing to describe. An area that does not exist must not be answered, and
        // the tab that must appear in a response is the one the land carries, so it is resolved
        // before anything else can read it.
        if (!IsFarmTab(resolvedType) || !IsFarmTab(requestedType))
            return CommonFarmShowAreaOutcome.Refuse;

        responseType = resolvedType;

        // Cleared side: the land is a farm, but of another tab. The response still names the tab the
        // land carries — with a zero count, which is what makes the reader drop what it holds.
        if (resolvedType != requestedType)
            return CommonFarmShowAreaOutcome.Clear;

        return CommonFarmShowAreaOutcome.Answer;
    }

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

    /// <summary>
    /// Whether a value names a farm tab at all. <see cref="FarmType.Invalid"/> is the absence of a
    /// tab, and a value outside the enum is not a tab either — both are checked here so a caller
    /// cannot pass an unset field through and have it treated as a real one.
    /// </summary>
    private static bool IsFarmTab(int value)
        => value != (int)FarmType.Invalid && Enum.IsDefined(typeof(FarmType), (FarmType)value);
}
