namespace AAEmu.Game.Models.Game.Expeditions;

internal static class GuildExpProgressionRules
{
    public static uint GetAcceptedAmount(uint requested, uint earnedToday, long dailyLimit)
    {
        if (requested == 0)
            return 0;
        if (dailyLimit <= 0)
            return requested;
        var remaining = Math.Max(0, dailyLimit - earnedToday);
        return (uint)Math.Min(requested, remaining);
    }
}
