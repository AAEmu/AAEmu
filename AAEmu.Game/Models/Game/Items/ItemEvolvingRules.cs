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
}
