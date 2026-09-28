using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// The two periodic lifespan columns: a weekday-and-minute the item runs out on, and a base date a
/// repeating period is measured from.
/// </summary>
public class ItemPeriodLifespanTests
{
    // 2026-03-01 is a Sunday, so the weekday arithmetic below can be read off the date.
    private static readonly DateTime Sunday = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Thursday = new(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Item NewItem(ItemTemplate template, DateTime createdAt) =>
        new(1, template, 1) { Id = 1, TemplateId = template.Id, CreateTime = createdAt };

    // --- weekday + minute -------------------------------------------------------------------------

    [Test]
    public async Task AWeekdayItemRunsOutAtTheNextOccurrenceOfThatMinute()
    {
        // enum_day_of_weeks id 5 is Thursday; 360 minutes past midnight is 06:00.
        var template = new ItemTemplate { Id = 1, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };

        await Assert.That(ItemLifetimeRules.NextWeekdayOccurrence(5, 360, Sunday))
            .IsEqualTo(Thursday.AddHours(6));
    }

    [Test]
    public async Task AWeekdayItemCreatedJustAfterItsMomentWaitsAWeek()
    {
        // Made at 06:30 on a Thursday, the 06:00 of that day has already gone.
        var created = Thursday.AddHours(6).AddMinutes(30);
        var template = new ItemTemplate { Id = 2, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };
        var item = NewItem(template, created);

        ItemLifetimeRules.ApplyNewItemLifespan(item, created);

        await Assert.That(item.ExpirationTime).IsEqualTo(Thursday.AddDays(7).AddHours(6));
        // Not born already expired, and not given the moment it missed either.
        await Assert.That(item.ExpirationTime > created).IsTrue();
    }

    [Test]
    public async Task AWeekdayItemCreatedOnItsOwnMomentWaitsAWeek()
    {
        var created = Thursday.AddHours(6);
        var template = new ItemTemplate { Id = 3, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };

        // The occurrence has to be strictly after the creation, or an item made at exactly the
        // moment its life runs out would be born already expired and swept on the next tick.
        await Assert.That(ItemLifetimeRules.NextWeekdayOccurrence(5, 360, created))
            .IsEqualTo(Thursday.AddDays(7).AddHours(6));
    }

    [Test]
    public async Task TheTablesOwnNoDayMarkerLeavesTheItemWithoutAWeekday()
    {
        // id 8 is enum_day_of_weeks' own "no day" row and is what 50989 of the shipped items carry.
        await Assert.That(ItemLifetimeRules.NextWeekdayOccurrence(8, 0, Sunday)).IsNull();
        await Assert.That(ItemLifetimeRules.NextWeekdayOccurrence(0, 360, Sunday)).IsNull();
    }

    [Test]
    public async Task AMinuteBeyondTheDayIsReadAsTheMinuteInsideIt()
    {
        // 1500 minutes is 25 hours, so it is 01:00 of the following day. The shipped table holds
        // 360, 660 and 900, so this is a guard rather than a path the content walks.
        var template = new ItemTemplate { Id = 4, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 1500 };

        await Assert.That(ItemLifetimeRules.NextWeekdayOccurrence(5, 1500, Sunday))
            .IsEqualTo(Thursday.AddHours(1));
    }

    [Test]
    public async Task AWeekdayItemGetsAnEndEvenWithNoOtherLifespanColumn()
    {
        // The shipped weekday rows all leave exp_date NULL and exp_abs_lifetime 0, so the weekday is
        // the only statement they make and it has to produce a timer on its own.
        var template = new ItemTemplate { Id = 5, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };
        var item = NewItem(template, Sunday);

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Sunday);

        await Assert.That(armed).IsTrue();
        await Assert.That(item.ExpirationTime).IsEqualTo(Thursday.AddHours(6));
    }

    // --- period base date ------------------------------------------------------------------------

    [Test]
    public async Task APeriodicItemCreatedInsideItsFirstPeriodRunsOutAtTheRowDate()
    {
        // The shipped growth jewels read base 2023-01-01 06:00 and exp_date 2023-06-29 06:00: a
        // 179-day period whose first end is the row's own date.
        var template = new ItemTemplate
        {
            Id = 6,
            PeriodBaseDate = new DateTime(2023, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            ExpDate = new DateTime(2023, 6, 29, 6, 0, 0, DateTimeKind.Utc)
        };
        var created = new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var item = NewItem(template, created);

        ItemLifetimeRules.ApplyNewItemLifespan(item, created);

        await Assert.That(item.ExpirationTime).IsEqualTo(new DateTime(2023, 6, 29, 6, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task APeriodicItemCreatedAfterItsRowDateRollsIntoTheNextPeriod()
    {
        // This is what the base date is for: without it the row's own exp_date is a moment already
        // in the past and the item would be born expired.
        var template = new ItemTemplate
        {
            Id = 7,
            PeriodBaseDate = new DateTime(2023, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            ExpDate = new DateTime(2023, 6, 29, 6, 0, 0, DateTimeKind.Utc)
        };
        var created = new DateTime(2023, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var item = NewItem(template, created);

        // The period boundary and the row's own exp_date are one repeating statement, so the
        // boundary is the one the item takes, and the recorded date is only its first cycle's end.
        await Assert.That(ItemLifetimeRules.ResolveExpiration(template, created))
            .IsEqualTo(new DateTime(2023, 12, 25, 6, 0, 0, DateTimeKind.Utc));

        // Inside the first cycle they agree, because the row's date is that cycle's end.
        await Assert.That(ItemLifetimeRules.ResolveExpiration(template, new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc)))
            .IsEqualTo(new DateTime(2023, 6, 29, 6, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task APeriodicItemCreatedBeforeItsBaseRunsOutAtTheBase()
    {
        var template = new ItemTemplate
        {
            Id = 8,
            PeriodBaseDate = new DateTime(2023, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            ExpDate = new DateTime(2023, 6, 29, 6, 0, 0, DateTimeKind.Utc)
        };

        await Assert.That(ItemLifetimeRules.NextPeriodBoundary(template, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
            .IsEqualTo(new DateTime(2023, 1, 1, 6, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task ABaseDateWithNothingToMeasureAPeriodAgainstIsNotTurnedIntoATimer()
    {
        // The period length is content: it is the gap the row states between its own base and its own
        // end. A row with only a base date states no length, so there is nothing to compute and the
        // item is left alone rather than given an invented period.
        var baseOnly = new ItemTemplate
        {
            Id = 9,
            PeriodBaseDate = new DateTime(2023, 1, 1, 6, 0, 0, DateTimeKind.Utc)
        };
        var item = NewItem(baseOnly, Sunday);

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Sunday);

        await Assert.That(armed).IsFalse();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task ABaseDateThatIsNotBeforeItsOwnEndStatesNoPeriodSoTheDateStands()
    {
        var inverted = new ItemTemplate
        {
            Id = 10,
            PeriodBaseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpDate = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        await Assert.That(ItemLifetimeRules.NextPeriodBoundary(inverted, Sunday)).IsNull();
        // With no period to measure, the date is the only end the row states and it is used as it is.
        await Assert.That(ItemLifetimeRules.ResolveExpiration(inverted, Sunday))
            .IsEqualTo(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // --- which statement wins --------------------------------------------------------------------

    [Test]
    public async Task TheEarliestStatementIsTheOneThatEndsTheItem()
    {
        // Four ways of saying when an item runs out, and none of them outranks another: the end is
        // whichever one is soonest. 44 shipped rows set both exp_abs_lifetime and exp_date, so the
        // choice is not academic - with exp_date always winning, every one of them would be born
        // already expired.
        var both = new ItemTemplate { Id = 11, ExpAbsLifetime = 1440, ExpDate = Sunday.AddHours(2) };
        await Assert.That(ItemLifetimeRules.ResolveExpiration(both, Sunday)).IsEqualTo(Sunday.AddHours(2));

        var relativeSooner = new ItemTemplate { Id = 12, ExpAbsLifetime = 60, ExpDate = Sunday.AddDays(40) };
        await Assert.That(ItemLifetimeRules.ResolveExpiration(relativeSooner, Sunday)).IsEqualTo(Sunday.AddMinutes(60));

        var overRelative = new ItemTemplate { Id = 13, ExpAbsLifetime = 1440, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };
        // A day from creation beats the Thursday, which is still four days out.
        await Assert.That(ItemLifetimeRules.ResolveExpiration(overRelative, Sunday)).IsEqualTo(Sunday.AddMinutes(1440));

        var overRelativeLong = new ItemTemplate { Id = 17, ExpAbsLifetime = 43200, ExpDayOfWeekId = 5, ExpDayOfWeekMin = 360 };
        await Assert.That(ItemLifetimeRules.ResolveExpiration(overRelativeLong, Sunday)).IsEqualTo(Thursday.AddHours(6));

        var periodSooner = new ItemTemplate
        {
            Id = 14,
            PeriodBaseDate = Sunday,
            ExpDate = Sunday.AddDays(100)
        };
        // A base date and a date are one repeating statement, so the repeating end replaces the
        // row's own first-cycle end rather than competing with it: 2026-06-09 has passed and the
        // item created on day 250 is due at the end of its third cycle, 300 days after the base.
        await Assert.That(ItemLifetimeRules.ResolveExpiration(periodSooner, Sunday.AddDays(250)))
            .IsEqualTo(Sunday.AddDays(300));

        // The same row without the anchor keeps the date it was written with.
        var dateOnly = new ItemTemplate { Id = 18, ExpDate = Sunday.AddDays(100) };
        await Assert.That(ItemLifetimeRules.ResolveExpiration(dateOnly, Sunday.AddDays(250)))
            .IsEqualTo(Sunday.AddDays(100));

        // A relative term that runs out before the cycle does still ends the item when it does.
        var relativeBeforeCycle = new ItemTemplate
        {
            Id = 19,
            ExpAbsLifetime = 43200,
            PeriodBaseDate = Sunday,
            ExpDate = Sunday.AddDays(100)
        };
        await Assert.That(ItemLifetimeRules.ResolveExpiration(relativeBeforeCycle, Sunday.AddDays(250)))
            .IsEqualTo(Sunday.AddDays(280));
    }

    [Test]
    public async Task ARowWhoseOnlyEndHasAlreadyPassedIsBornExpired()
    {
        // Every exp_date in the shipped table is in the past (the newest is 2024-05-30), so this is
        // the ordinary case for a dated item on a running server, not an edge case. The content says
        // the item is gone; the item is armed with that end and the ordinary sweep takes it.
        var stale = new ItemTemplate { Id = 15, ExpDate = Sunday.AddDays(-400) };

        var resolved = ItemLifetimeRules.ResolveExpiration(stale, Sunday);

        await Assert.That(resolved).IsEqualTo(Sunday.AddDays(-400));
        await Assert.That(resolved <= Sunday).IsTrue();
    }

    [Test]
    public async Task AnItemWithNoLifespanColumnAtAllNeverExpires()
    {
        var template = new ItemTemplate { Id = 16, ExpDayOfWeekId = 8, ExpDayOfWeekMin = 0 };

        await Assert.That(ItemLifetimeRules.ResolveExpiration(template, Sunday)).IsNull();
    }
}
