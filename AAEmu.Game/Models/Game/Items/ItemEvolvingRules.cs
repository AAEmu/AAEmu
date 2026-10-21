namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How much of a synthesis feed the ladder can still buy, and what one grade step costs.
/// </summary>
public static class ItemEvolvingRules
{
    /// <summary>
    /// Overflow past <paramref name="room"/> is what the window prints beside the bar.
    /// It must not be billed or written onto the piece. A full ladder buys nothing.
    /// </summary>
    public static bool TryPurchase(uint offered, uint room, out uint purchased)
    {
        purchased = Math.Min(offered, room);
        return purchased > 0;
    }

    /// <summary>
    /// What filling the current grade's bar costs to step into <c>next</c>.
    /// </summary>
    /// <remarks>
    /// The client draws the bar against the grade the piece is <em>in</em>
    /// (<c>grade_exp[current]</c>). The step therefore spends that same amount, not the next
    /// grade's cost - otherwise a typical weapon pool (~1.7× between rungs) sits at 100% and
    /// keeps climbing to ~170% before the grade moves. An unpriced next rung is only enterable
    /// when the pool's ceiling allows that free step, still paid by filling the grade below.
    /// </remarks>
    public static bool TryGetStepCost(
        uint currentGradeExp,
        uint nextGradeExp,
        bool allowsFreeStepIntoNext,
        out uint needed)
    {
        needed = 0;

        if (nextGradeExp == 0 && !allowsFreeStepIntoNext)
            return false;

        if (currentGradeExp == 0)
            return false;

        needed = currentGradeExp;
        return true;
    }

    /// <summary>
    /// Experience a feed can still buy from the current grade up to and including the ladder top.
    /// </summary>
    /// <remarks>
    /// The top grade's own <c>grade_exp</c> is part of the room: Eternal (and any other ceiling)
    /// still accepts synthesis for its bar and for the stats that fill grants, but the bar never
    /// tips into a higher grade. Past that ceiling is overflow the window reports and the feed
    /// must not purchase.
    /// </remarks>
    public static uint ExpRoomToLadderCap(byte grade, byte ladderTop, uint sectionExp, Func<byte, uint> gradeExp)
    {
        if (gradeExp == null || grade > ladderTop)
            return 0;

        var remaining = 0u;
        for (var g = grade; g <= ladderTop && g < byte.MaxValue; g++)
            remaining += gradeExp((byte)g);

        return remaining > sectionExp ? remaining - sectionExp : 0;
    }

    /// <summary>
    /// Walks slot order and keeps only the infusions that still buy room.
    /// A later slot whose whole offer would be overflow is left in the bag.
    /// The last kept slot may still overflow; that one is consumed because it bought the rest of the bar.
    /// </summary>
    public static bool TryTakeFeed(IReadOnlyList<uint> offeredEach, uint room, out uint purchased, out int takeCount)
    {
        purchased = 0;
        takeCount = 0;
        if (offeredEach == null || room == 0)
            return false;

        var remaining = room;
        foreach (var offered in offeredEach)
        {
            if (remaining == 0)
                break;
            takeCount++;
            var take = Math.Min(offered, remaining);
            purchased += take;
            remaining -= take;
        }

        return purchased > 0;
    }
}
