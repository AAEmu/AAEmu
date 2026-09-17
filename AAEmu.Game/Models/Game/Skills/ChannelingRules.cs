namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Channel bookkeeping: how many times a channel ticks, what each tick costs, and when a channel gives
/// way to a newly started cast.
/// </summary>
/// <remarks>
/// 353 skills declare <c>channeling_time</c>, and 202 of those also declare a <c>channeling_tick</c>
/// (1,000 ms on all but a handful, 200 ms on 별똥별: 파도). The tick count is deliberately the number of
/// whole intervals inside <c>channeling_time</c>: 보호의 날개 (10714) is 12,000 ms on a 1,000 ms tick and
/// drains twelve times, and a channel shorter than its own tick drains not at all.
///
/// The tick cost is <c>channeling_mana</c> (10 skills, 4 to 20 per tick on the ones that channel at
/// all). The first tick lands one interval after the cast, not at the cast: <c>Cast</c> already charged
/// the skill's own mana cost, and charging a tick on the same frame would double it.
/// </remarks>
public static class ChannelingRules
{
    /// <summary>
    /// How many times the channel costs mana. Zero when the skill declares no interval or no per-tick
    /// cost, which is the case for every channel but the ten with a <c>channeling_mana</c> row.
    /// </summary>
    public static int TickCount(int channelingTime, int channelingTick, int channelingMana)
    {
        if (channelingTime <= 0 || channelingTick <= 0 || channelingMana <= 0)
            return 0;

        return channelingTime / channelingTick;
    }

    /// <summary>What one tick takes, never more than the unit actually holds.</summary>
    public static int ManaPerTick(int channelingMana, int currentMp)
        => Math.Clamp(channelingMana, 0, Math.Max(0, currentMp));

    /// <summary>
    /// Whether a channel that is running has to end because the unit started another cast. Only the
    /// seven non-plot channel skills can be in this position at all: a plot_only channel is owned by its
    /// plot, which cancels itself (PlotTree / PlotState cancellation) rather than through this flag.
    /// </summary>
    public static bool StopsChannelOnNewCast(bool stopChannelingOnStartSkill, bool hasRunningChannel)
        => stopChannelingOnStartSkill && hasRunningChannel;

    /// <summary>
    /// Whether the channel's effects land when it runs to completion. A channel that was stopped early
    /// applies nothing — that is what CSStopCastingPacket and the interrupt paths ask for.
    /// </summary>
    public static bool AppliesEffectsOnEnd(bool completedNaturally) => completedNaturally;
}
