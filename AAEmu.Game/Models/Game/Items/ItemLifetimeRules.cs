using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Arms the lifespan a freshly created item inherits from its template row.
/// <para>
/// <c>items</c> carries six independent lifespan columns, and every one of them is evaluated here
/// so a new stack never leaves the factory with an unarmed timer:
/// <list type="bullet">
///   <item><c>exp_abs_lifetime</c> - minutes counted from the moment the item is created.</item>
///   <item><c>exp_date</c> - an absolute wall-clock end.</item>
///   <item><c>exp_day_of_week_id</c> / <c>exp_day_of_week_min</c> - a weekday and a minute past
///   midnight, so the item runs out at the next occurrence of that moment.</item>
///   <item><c>period_base_date</c> - the anchor a repeating period is measured from.</item>
///   <item><c>exp_online_lifetime</c> - minutes that only tick down while the owner is
///   online, tracked separately from the absolute end.</item>
/// </list>
/// </para>
/// <para>
/// The four that name an end are four different ways of saying the same thing, so none of them
/// outranks another: the item ends at the <b>earliest</b> instant any of them names. That is also
/// the only reading that needs no policy about which column outranks which - 44 shipped rows set both
/// <c>exp_abs_lifetime</c> and <c>exp_date</c>, and "the absolute date wins" would have made every
/// one of them born already expired.
/// </para>
/// <para>
/// The one exception is a pair that is a single statement rather than two: <c>period_base_date</c>
/// together with <c>exp_date</c> says "this item repeats on a cycle", and the date is only that
/// cycle's first end, so the repeating end replaces it. All 23 shipped rows that carry a base date
/// also carry a date.
/// </para>
/// <para>
/// A row whose statement has already passed is honoured as written: the item is armed with that end
/// and the ordinary expiry sweep takes it on its next pass, logging how many items expired. Every
/// <c>exp_date</c> in the shipped table is in the past - the newest is 2024-05-30 - so on a running
/// server that is a real and observable number of items, and the column is content that has to be
/// refreshed rather than a rule to work around.
/// </para>
/// <para>
/// This runs inside the item factory, so a stack is armed no matter which delivery path produced it
/// (loot, mail, cash shop, crafting, housing, indun, auction payout). A path that derives a stack
/// from an existing one copies the source's remaining lifetime over the armed one afterwards, which
/// is what a split or a transfer has to do: the new stack cannot outlive the stack it came from.
/// </para>
/// </summary>
public static class ItemLifetimeRules
{
    /// <summary>
    /// The weekday ids of <c>enum_day_of_weeks</c>: 1 is Sunday through 7 Saturday. 8 is the row the
    /// table itself carries for "this item has no weekday", and it is what all but 21 of the shipped
    /// item rows hold.
    /// </summary>
    private const int FirstDayOfWeekId = 1;

    /// <summary>The last weekday id that names a day; the value after it is the table's "no day" marker.</summary>
    private const int LastDayOfWeekId = 7;

    /// <summary>Minutes in a day, the modulus a minute-past-midnight is normalised against.</summary>
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Applies the template's lifespan columns to a newly created item.
    /// </summary>
    /// <param name="item">The freshly created item. Its <see cref="Item.Template"/> drives the values.</param>
    /// <param name="utcNow">The creation instant the relative columns count from.</param>
    /// <returns><c>true</c> when at least one lifespan column produced a running timer.</returns>
    public static bool ApplyNewItemLifespan(Item item, DateTime utcNow)
    {
        if (item?.Template is not { } template)
            return false;

        var armed = false;

        // Online-only lifetime counts down separately and is not derived from the absolute end.
        if (template.ExpOnlineLifetime > 0)
        {
            item.ExpirationOnlineMinutesLeft = template.ExpOnlineLifetime;
            armed = true;
        }

        var expiration = ResolveExpiration(template, utcNow);
        if (expiration.HasValue)
        {
            item.ExpirationTime = expiration.Value;
            armed = true;
        }

        return armed;
    }

    /// <summary>
    /// Works out when a template's rows say a new instance of it runs out: the earliest end any of
    /// its lifespan columns names, or <c>null</c> when none of them names one.
    /// </summary>
    /// <param name="template">The template the new item is made from.</param>
    /// <param name="createdAt">The instant the new item was created.</param>
    public static DateTime? ResolveExpiration(ItemTemplate template, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(template);

        var ends = new List<DateTime>(3);

        // exp_abs_lifetime: minutes from creation. A negative value is content noise rather than an
        // "already expired" intent, so only a positive one arms a timer.
        if (template.ExpAbsLifetime > 0)
            ends.Add(createdAt.AddMinutes(template.ExpAbsLifetime));

        // The weekly moment is one more statement about the end, taken on its own terms. No shipped
        // row sets a real weekday together with either of the other two - the 21 weekday rows leave
        // exp_date NULL and exp_abs_lifetime 0 - so in practice it never competes.
        var weekly = NextWeekdayOccurrence(template.ExpDayOfWeekId, template.ExpDayOfWeekMin, createdAt);
        if (weekly.HasValue)
            ends.Add(weekly.Value);

        // A period replaces the row's own date rather than competing with it. The two columns are one
        // statement: the base date is the anchor and exp_date is where the row's first period ended,
        // so the gap between them is the period and exp_date is only that one cycle's end. An item
        // made after it has to roll into the next cycle, which is the whole reason the anchor sits
        // next to the date.
        var period = NextPeriodBoundary(template, createdAt);
        if (period.HasValue)
            ends.Add(period.Value);
        else if (template.ExpDate > DateTime.MinValue)
            ends.Add(template.ExpDate);

        return ends.Count == 0 ? null : ends.Min();
    }

    /// <summary>
    /// The next time the named weekday reaches the named minute, counting from
    /// <paramref name="createdAt"/>. The occurrence is always strictly after the creation, so an item
    /// made at the very moment its life runs out waits for the following week rather than being born
    /// already expired.
    /// </summary>
    /// <returns><c>null</c> when the template names no weekday.</returns>
    public static DateTime? NextWeekdayOccurrence(int dayOfWeekId, int minuteOfDay, DateTime createdAt)
    {
        if (dayOfWeekId is < FirstDayOfWeekId or > LastDayOfWeekId)
            return null;

        var target = (DayOfWeek)(dayOfWeekId - FirstDayOfWeekId);
        var minutes = NormalizeMinuteOfDay(minuteOfDay);

        var dayStart = new DateTime(createdAt.Year, createdAt.Month, createdAt.Day, 0, 0, 0, createdAt.Kind);
        var daysAhead = ((int)target - (int)createdAt.DayOfWeek + 7) % 7;
        var candidate = dayStart.AddDays(daysAhead).AddMinutes(minutes);

        // The weekday column is a moment that repeats weekly, so an item created just after it has
        // to wait for the next one rather than being born already expired.
        return candidate <= createdAt ? candidate.AddDays(7) : candidate;
    }

    /// <summary>
    /// The first period boundary at or after <paramref name="createdAt"/> for a template anchored on
    /// a <c>period_base_date</c>. The period length is the gap the row states between its base date
    /// and its <c>exp_date</c>.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the template names no base date, or names one without the end of a period to
    /// measure against - the length is content, so there is nothing to fall back to and the item is
    /// left without a timer rather than given an invented period.
    /// </returns>
    public static DateTime? NextPeriodBoundary(ItemTemplate template, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.PeriodBaseDate <= DateTime.MinValue || template.ExpDate <= DateTime.MinValue)
            return null;

        var period = template.ExpDate - template.PeriodBaseDate;
        if (period <= TimeSpan.Zero)
            return null;

        var baseDate = template.PeriodBaseDate;
        if (createdAt <= baseDate)
            return baseDate;

        var elapsed = createdAt - baseDate;
        var periods = (long)Math.Ceiling(elapsed.Ticks / (double)period.Ticks);
        return baseDate.AddTicks(period.Ticks * periods);
    }

    /// <summary>
    /// Builds the client sync packets that mirror the lifespan already armed on an item.
    /// The timers themselves are set by <see cref="ApplyNewItemLifespan"/>; this only reports
    /// them, so the owning bag can push the same values the item will be persisted with.
    /// </summary>
    /// <param name="item">An item whose lifespan has already been applied.</param>
    /// <param name="utcNow">Instant used to project a remaining online budget onto the wire.</param>
    public static IReadOnlyList<GamePacket> BuildLifespanSyncPackets(Item item, DateTime utcNow)
    {
        var packets = new List<GamePacket>(2);
        if (item == null)
            return packets;

        if (item.ExpirationTime > DateTime.MinValue)
            packets.Add(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, item.ExpirationTime));

        if (item.ExpirationOnlineMinutesLeft > 0.0)
            packets.Add(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId,
                utcNow.AddMinutes(item.ExpirationOnlineMinutesLeft)));

        return packets;
    }

    private static int NormalizeMinuteOfDay(int minuteOfDay)
    {
        var minutes = minuteOfDay % MinutesPerDay;
        return minutes < 0 ? minutes + MinutesPerDay : minutes;
    }
}
