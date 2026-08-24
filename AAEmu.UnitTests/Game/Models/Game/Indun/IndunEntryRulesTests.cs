using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunEntryRulesTests
{
    [Test]
    public async Task DailyWindowStartUtc_IsMidnightOfThatUtcDay()
    {
        var now = new DateTime(2026, 8, 21, 15, 30, 0, DateTimeKind.Utc);
        await Assert.That(IndunEntryRules.DailyWindowStartUtc(now))
            .IsEqualTo(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task CountEntriesInDailyWindow_IgnoresYesterday()
    {
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new DateTime(2026, 8, 20, 23, 59, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 21, 11, 0, 0, DateTimeKind.Utc)
        };
        await Assert.That(IndunEntryRules.CountEntriesInDailyWindow(entries, now)).IsEqualTo(2);
    }

    [Test]
    public async Task IsCreateOnCooldown_RespectsRestoreItemTime()
    {
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var last = now.AddSeconds(-3599);
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(last, now, 3600)).IsTrue();
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(now.AddSeconds(-3600), now, 3600)).IsFalse();
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(null, now, 3600)).IsFalse();
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(last, now, 0)).IsFalse();
    }

    [Test]
    public async Task IsCreateOnCooldown_NullLastCreate_MeansNoCooldown()
    {
        // Same as ClearCreateCooldown after portal reset — next create is allowed.
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(null, now, 3600)).IsFalse();
    }

    [Test]
    public async Task ResolveEnterCount_PrefersInstancesTable()
    {
        await Assert.That(IndunEntryRules.ResolveEnterCount(3, selectChannel: false, zoneGroupId: 51)).IsEqualTo(3u);
        await Assert.That(IndunEntryRules.ResolveEnterCount(null, selectChannel: true, zoneGroupId: 99)).IsEqualTo(1000u);
        await Assert.That(IndunEntryRules.ResolveEnterCount(null, selectChannel: false, zoneGroupId: 49)).IsEqualTo(1000u);
        await Assert.That(IndunEntryRules.ResolveEnterCount(null, selectChannel: false, zoneGroupId: 51)).IsEqualTo(3u);
    }

    [Test]
    public async Task ResetTicketCost_ScalesByPurchaseCount()
    {
        await Assert.That(IndunEntryRules.ResetTicketCost(0, 1)).IsEqualTo(1);
        await Assert.That(IndunEntryRules.ResetTicketCost(2, 1)).IsEqualTo(3);
        await Assert.That(IndunEntryRules.ResetTicketCost(1, 2)).IsEqualTo(4);
        await Assert.That(IndunEntryRules.ResetTicketCost(0, 0)).IsEqualTo(1);
    }

    [Test]
    public async Task CanBuyReset_RespectsLimit()
    {
        await Assert.That(IndunEntryRules.CanBuyReset(0, 0)).IsTrue();
        await Assert.That(IndunEntryRules.CanBuyReset(2, 3)).IsTrue();
        await Assert.That(IndunEntryRules.CanBuyReset(3, 3)).IsFalse();
    }

    [Test]
    public async Task EffectivePermittedCount_AddsPermitBonus()
    {
        await Assert.That(IndunEntryRules.EffectivePermittedCount(3, 0)).IsEqualTo(3);
        await Assert.That(IndunEntryRules.EffectivePermittedCount(3, 2)).IsEqualTo(5);
    }

    [Test]
    public async Task PortalReset_ShouldAllowImmediateCreate_AfterClearingCooldown()
    {
        // Documented contract: restore_item_time blocks create until cleared by 초기화.
        var now = new DateTime(2026, 8, 22, 3, 17, 0, DateTimeKind.Utc);
        var lastCreate = now.AddMinutes(-5);
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(lastCreate, now, 3600)).IsTrue();
        // After ClearCreateCooldown, lastCreate is null → not on cooldown.
        await Assert.That(IndunEntryRules.IsCreateOnCooldown(null, now, 3600)).IsFalse();
    }
}
