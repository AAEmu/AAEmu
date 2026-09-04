namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How much of a synthesis feed the ladder can still buy.
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
