using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Dominions;

namespace AAEmu.UnitTests.Game.Models.Game.Dominions;

public class DominionClaimRulesTests
{
    [Test]
    public async Task IsLodestoneTemplate_SettingOrDeclareStep()
    {
        await Assert.That(DominionClaimRules.IsLodestoneTemplate(2, false)).IsTrue();
        await Assert.That(DominionClaimRules.IsLodestoneTemplate(0, true)).IsTrue();
        await Assert.That(DominionClaimRules.IsLodestoneTemplate(0, false)).IsFalse();
    }

    [Test]
    public async Task FindLodestoneInZone_UsesLiveHouseZoneNotAStaticMap()
    {
        var houses = new (uint TemplateId, ushort ZoneGroup)[]
        {
            (184, 33),
            (187, 34)
        };

        var found = DominionClaimRules.FindLodestoneInZone(
            houses, 34, h => h.ZoneGroup, h => h.TemplateId is 184 or 187);

        await Assert.That(found.TemplateId).IsEqualTo(187u);

        var missing = DominionClaimRules.FindLodestoneInZone(
            houses, 54, h => h.ZoneGroup, h => h.TemplateId is 184 or 187);
        await Assert.That(missing.TemplateId).IsEqualTo(0u);
    }

    [Test]
    public async Task IsTaxRateAllowed_UsesConfiguredBoundsOnly()
    {
        await Assert.That(DominionClaimRules.IsTaxRateAllowed(10, 10, 30)).IsTrue();
        await Assert.That(DominionClaimRules.IsTaxRateAllowed(30, 10, 30)).IsTrue();
        await Assert.That(DominionClaimRules.IsTaxRateAllowed(9, 10, 30)).IsFalse();
        await Assert.That(DominionClaimRules.IsTaxRateAllowed(31, 10, 30)).IsFalse();
        await Assert.That(DominionClaimRules.IsTaxRateAllowed(0, 0, -1)).IsFalse();
    }

    [Test]
    public async Task InitialTaxRate_IsTheConfiguredMinimum()
    {
        await Assert.That(DominionClaimRules.InitialTaxRate(true, 10)).IsEqualTo(10);
        await Assert.That(DominionClaimRules.InitialTaxRate(false, 10)).IsEqualTo(0);
    }

    [Test]
    public async Task ShouldPayOnSiegeWeek_RequiresACurrentWeekAndAPriorPay()
    {
        var week = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await Assert.That(DominionClaimRules.ShouldPayOnSiegeWeek(week.AddDays(-1), week)).IsTrue();
        await Assert.That(DominionClaimRules.ShouldPayOnSiegeWeek(week, week)).IsFalse();
        await Assert.That(DominionClaimRules.ShouldPayOnSiegeWeek(week.AddDays(-1), null)).IsFalse();
    }

    [Test]
    public async Task CapTax_UsesConfiguredLimitOnly()
    {
        await Assert.That(DominionClaimRules.CapTax(250000, 200000)).IsEqualTo(200000);
        await Assert.That(DominionClaimRules.CapTax(50000, 200000)).IsEqualTo(50000);
        await Assert.That(() => DominionClaimRules.CapTax(50000, 0)).Throws<InvalidOperationException>();
        await Assert.That(() => DominionClaimRules.CapTax(50000, -1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AfterTaxPayout_KeepsTheUnpaidRemainder()
    {
        var capped = DominionClaimRules.AfterTaxPayout(250000, 0, 0, 200000);
        await Assert.That(capped.House).IsEqualTo(50000);
        await Assert.That(capped.Hunt).IsEqualTo(0);
        await Assert.That(capped.Peace).IsEqualTo(0);

        var mixed = DominionClaimRules.AfterTaxPayout(80000, 40000, 30000, 100000);
        await Assert.That(mixed.House).IsEqualTo(0);
        await Assert.That(mixed.Hunt).IsEqualTo(20000);
        await Assert.That(mixed.Peace).IsEqualTo(30000);

        var full = DominionClaimRules.AfterTaxPayout(100, 20, 5, 125);
        await Assert.That(full.House + full.Hunt + full.Peace).IsEqualTo(0);
    }

    [Test]
    public async Task TaxDue_IsZeroUntilTheSiegeWeekRollsThenAppliesTheCap()
    {
        var week = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await Assert.That(DominionClaimRules.TaxDue(week, week, 250000, 200000)).IsEqualTo(0);
        await Assert.That(DominionClaimRules.TaxDue(week.AddDays(-1), week, 250000, 200000)).IsEqualTo(200000);
        await Assert.That(DominionClaimRules.TaxDue(week.AddDays(-1), null, 250000, 200000)).IsEqualTo(0);
    }

    [Test]
    public async Task TaxDue_FailsLoudlyWhenTheDueWeekHasNoLimit()
    {
        var week = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await Assert.That(() => DominionClaimRules.TaxDue(week.AddDays(-1), week, 50000, 0))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MayPlaceUniqueDominionDesign_OnlyBlocksASecondCopy()
    {
        await Assert.That(DominionClaimRules.MayPlaceUniqueDominionDesign(true, false)).IsTrue();
        await Assert.That(DominionClaimRules.MayPlaceUniqueDominionDesign(true, true)).IsFalse();
        await Assert.That(DominionClaimRules.MayPlaceUniqueDominionDesign(false, true)).IsTrue();
    }

    [Test]
    public async Task HasDesignInZone_UsesTheLiveHouseZone()
    {
        var houses = new (uint Design, ushort Zone)[] { (756, 33), (744, 34) };
        await Assert.That(DominionClaimRules.HasDesignInZone(houses, 756, 33, h => h.Design, h => h.Zone)).IsTrue();
        await Assert.That(DominionClaimRules.HasDesignInZone(houses, 756, 34, h => h.Design, h => h.Zone)).IsFalse();
    }

    [Test]
    public async Task GetDeclareRefuse_FactionWindowAndLodestone()
    {
        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, true, true, false, true, false, true)).IsEqualTo(DominionDeclareRefuse.None);

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            true, true, true, true, false, true, false, true)).IsEqualTo(DominionDeclareRefuse.Locked);
        await Assert.That(DominionClaimRules.MessageFor(DominionDeclareRefuse.Locked))
            .IsEqualTo(ErrorMessageType.Invalid);

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, false, true, false, true, false, true)).IsEqualTo(DominionDeclareRefuse.NotHero);

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, true, true, false, false, false, true)).IsEqualTo(DominionDeclareRefuse.WindowClosed);
        await Assert.That(DominionClaimRules.MessageFor(DominionDeclareRefuse.WindowClosed))
            .IsEqualTo(ErrorMessageType.DominionNotDeclareTime);

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, true, true, false, true, true, true)).IsEqualTo(DominionDeclareRefuse.AlreadyClaimed);
        await Assert.That(DominionClaimRules.MessageFor(DominionDeclareRefuse.AlreadyClaimed))
            .IsEqualTo(ErrorMessageType.DominionAlreadyDedclared);

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, true, true, false, true, false, false)).IsEqualTo(DominionDeclareRefuse.NotALodestone);
        await Assert.That(DominionClaimRules.MessageFor(DominionDeclareRefuse.NotALodestone)).IsNull();
    }

    [Test]
    public async Task GetDeclareRefuse_GuildNeedsAnExpeditionAndStaysSilentWithoutOne()
    {
        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, false, false, false, false, true, false, true)).IsEqualTo(DominionDeclareRefuse.NoExpedition);
        await Assert.That(DominionClaimRules.MessageFor(DominionDeclareRefuse.NoExpedition)).IsNull();

        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, false, false, false, true, true, false, true)).IsEqualTo(DominionDeclareRefuse.None);
    }

    [Test]
    public async Task GetDeclareRefuse_GuildIgnoresTheFactionSiegeWindow()
    {
        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, false, false, false, true, false, false, true)).IsEqualTo(DominionDeclareRefuse.None);
        await Assert.That(DominionClaimRules.GetDeclareRefuse(
            false, true, true, true, false, false, false, true)).IsEqualTo(DominionDeclareRefuse.WindowClosed);
    }

    [Test]
    public async Task AfterTaxMail_RestoresThePoolWhenSendFails()
    {
        var before = new DominionClaimRules.TaxPool(80000, 40000, 30000, DateTime.UnixEpoch);
        var settled = DominionClaimRules.SettleTaxPool(before, 100000, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));

        await Assert.That(settled.House).IsEqualTo(0);
        await Assert.That(settled.Hunt).IsEqualTo(20000);
        await Assert.That(settled.Peace).IsEqualTo(30000);
        await Assert.That(DominionClaimRules.AfterTaxMail(settled, before, true)).IsEqualTo(settled);
        await Assert.That(DominionClaimRules.AfterTaxMail(settled, before, false)).IsEqualTo(before);
    }
}
