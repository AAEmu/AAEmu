using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class ExpertLimitRulesTests
{
    [Test]
    public async Task IndexShownRow_SplitsLanguageAndSkipsHidden()
    {
        var production = new Dictionary<int, ExpertLimit>();
        var language = new Dictionary<int, ExpertLimit>();

        ExpertLimitRules.IndexShownRow(Row(1, 10000, show: true), production, language);
        ExpertLimitRules.IndexShownRow(Row(2, 20000, show: true, slots: 7), production, language);
        ExpertLimitRules.IndexShownRow(Row(32, 20000, show: true, language: true), production, language);
        ExpertLimitRules.IndexShownRow(Row(3, 30000, show: true, slots: 6), production, language);
        ExpertLimitRules.IndexShownRow(Row(13, 260000, show: false), production, language);
        ExpertLimitRules.IndexShownRow(Row(12, 230000, show: true, intensified: true), production, language);

        await Assert.That(production.Count).IsEqualTo(4);
        await Assert.That(language.Count).IsEqualTo(1);
        await Assert.That(production[0].Id).IsEqualTo(1u);
        await Assert.That(production[1].Id).IsEqualTo(2u);
        await Assert.That(production[2].Id).IsEqualTo(3u);
        await Assert.That(production[3].Id).IsEqualTo(12u);
        await Assert.That(production[3].UpLimit).IsEqualTo(230000);
        await Assert.That(language[0].Id).IsEqualTo(32u);
        await Assert.That(language[0].UpLimit).IsEqualTo(20000);
    }

    [Test]
    public async Task UpgradeError_NeedsCurrentCapBeforeRankUp()
    {
        var current = Row(1, 10000, show: true);
        var next = Row(2, 20000, show: true, slots: 7);

        await Assert.That(ExpertLimitRules.UpgradeError(current, next, 9999, true))
            .IsEqualTo(ErrorMessageType.ActabilityNotEnoughPoint);
        await Assert.That(ExpertLimitRules.UpgradeError(current, next, 10000, true)).IsNull();
    }

    [Test]
    public async Task UpgradeError_LastShownRankHasNoNext()
    {
        var current = Row(12, 230000, show: true, intensified: true);

        await Assert.That(ExpertLimitRules.UpgradeError(current, null, 230000, true))
            .IsEqualTo(ErrorMessageType.ActabilityCanUpgradeAnyMore);
    }

    [Test]
    public async Task UpgradeError_FullSlotsBlock()
    {
        var current = Row(1, 10000, show: true);
        var next = Row(2, 20000, show: true, slots: 7);

        await Assert.That(ExpertLimitRules.UpgradeError(current, next, 10000, false))
            .IsEqualTo(ErrorMessageType.ActabilityCanUpgradeSelectionCountLimit);
    }

    [Test]
    public async Task DowngradeTicketError_FamedIsFree_AuthorityNeedsCertificate()
    {
        var famed = Row(11, 180000, show: true);
        var authority = Row(12, 230000, show: true, intensified: true);

        await Assert.That(ExpertLimitRules.RequiresIntensifiedDowngradeTicket(famed)).IsFalse();
        await Assert.That(ExpertLimitRules.DowngradeTicketError(famed, 49001, false)).IsNull();

        await Assert.That(ExpertLimitRules.RequiresIntensifiedDowngradeTicket(authority)).IsTrue();
        await Assert.That(ExpertLimitRules.DowngradeTicketError(authority, 49001, false))
            .IsEqualTo(ErrorMessageType.NotEnoughItem);
        await Assert.That(ExpertLimitRules.DowngradeTicketError(authority, 49001, true)).IsNull();
        await Assert.That(ExpertLimitRules.DowngradeTicketError(authority, 0, true))
            .IsEqualTo(ErrorMessageType.Invalid);
    }

    [Test]
    public async Task DowngradeError_StepZeroIsTheFloor()
    {
        var current = Row(1, 10000, show: true);
        await Assert.That(ExpertLimitRules.DowngradeError(0, current, null))
            .IsEqualTo(ErrorMessageType.ActabilityCanDowngradeAnyMore);
        await Assert.That(ExpertLimitRules.DowngradeError(1, current, Row(1, 10000, show: true))).IsNull();
    }

    [Test]
    public async Task ExpandError_MatchesMissingRowLivingPointAndItem()
    {
        await Assert.That(ExpertLimitRules.ExpandError(null, 0, true))
            .IsEqualTo(ErrorMessageType.ActabilityCanUpgradeAnyMore);

        var next = new ExpandExpertLimit { ExpandCount = 1, LifePoint = 50, ItemId = 29656, ItemCount = 1 };
        await Assert.That(ExpertLimitRules.ExpandError(next, 10, true))
            .IsEqualTo(ErrorMessageType.NotEnoughLivingPoint);
        await Assert.That(ExpertLimitRules.ExpandError(next, 50, false))
            .IsEqualTo(ErrorMessageType.NotEnoughItem);
        await Assert.That(ExpertLimitRules.ExpandError(next, 50, true)).IsNull();
    }

    [Test]
    public async Task HasSelectionSlot_ApprenticeCapIsSevenPlusExpanded()
    {
        var apprentice = Row(2, 20000, show: true, slots: 7);
        var seven = Occupied(7, step: 1);

        await Assert.That(ExpertLimitRules.HasSelectionSlot(seven, apprentice, 1, 0, 1)).IsFalse();
        await Assert.That(ExpertLimitRules.HasSelectionSlot(seven, apprentice, 1, 1, 1)).IsTrue();
        await Assert.That(ExpertLimitRules.HasSelectionSlot(Occupied(6, step: 1), apprentice, 1, 0, 1)).IsTrue();
    }

    [Test]
    public async Task HasSelectionSlot_LanguageDoesNotConsumeProductionSlots()
    {
        var apprentice = Row(2, 20000, show: true, slots: 7);
        var filled = Occupied(7, step: 1);
        filled.Add(Make((uint)ActabilityType.NuianLanguage, 1, viewGroup: 4));

        await Assert.That(ExpertLimitRules.HasSelectionSlot(filled, apprentice, 1, 0, 1)).IsFalse();
        await Assert.That(ExpertLimitRules.UsesLanguageLadder((uint)ActabilityType.NuianLanguage)).IsTrue();
        await Assert.That(ExpertLimitRules.CountsTowardProductionSlots((uint)ActabilityType.Fishing, true)).IsTrue();
        await Assert.That(ExpertLimitRules.CountsTowardProductionSlots((uint)ActabilityType.NuianLanguage, true))
            .IsFalse();
    }

    [Test]
    public async Task HasSelectionSlot_ZeroExpertLimitIsUnlimited()
    {
        var novice = Row(1, 10000, show: true, slots: 0);
        await Assert.That(ExpertLimitRules.HasSelectionSlot(Occupied(20, step: 0), novice, 0, 0, 1)).IsTrue();
    }

    [Test]
    public async Task HasSelectionSlot_IntensifiedUsesViewGroupCapWithoutExpand()
    {
        var master = Row(12, 230000, show: true, intensified: true);
        master.IntensifiedViewGroupLimits[1] = 2;
        var one = new List<Actability> { Make((uint)ActabilityType.Fishing, 11, viewGroup: 1) };

        await Assert.That(ExpertLimitRules.HasSelectionSlot(one, master, 11, 14, 1)).IsTrue();
        one.Add(Make((uint)ActabilityType.Farming, 11, viewGroup: 1));
        await Assert.That(ExpertLimitRules.HasSelectionSlot(one, master, 11, 14, 1)).IsFalse();
        await Assert.That(ExpertLimitRules.HasSelectionSlot(one, master, 11, 0, 2)).IsFalse();
    }

    [Test]
    public async Task ClampPoints_UsesTheRankCap()
    {
        var expert = Row(5, 50000, show: true, slots: 4);
        await Assert.That(ExpertLimitRules.ClampPoints(expert, 60000)).IsEqualTo(50000);
        await Assert.That(ExpertLimitRules.ClampPoints(expert, -1)).IsEqualTo(0);
        await Assert.That(ExpertLimitRules.ClampPoints(null, 10)).IsEqualTo(10);
    }

    [Test]
    public async Task AddEarnedPoints_DoesNotWipeBankedTotal()
    {
        await Assert.That(ExpertLimitRules.AddEarnedPoints(50000, 100, 10000)).IsEqualTo(50000);
        await Assert.That(ExpertLimitRules.AddEarnedPoints(9000, 2000, 10000)).IsEqualTo(10000);
        await Assert.That(ExpertLimitRules.AddEarnedPoints(5000, 100, 10000)).IsEqualTo(5100);
        await Assert.That(ExpertLimitRules.AddEarnedPoints(50000, -100, 10000)).IsEqualTo(49900);
    }

    [Test]
    public async Task UpgradeError_BankedPointsCanClimbFromAmateur()
    {
        var amateur = Row(1, 10000, show: true);
        var novice = Row(2, 20000, show: true, slots: 7);
        var veteran = Row(3, 30000, show: true, slots: 6);

        await Assert.That(ExpertLimitRules.UpgradeError(amateur, novice, 10000, true)).IsNull();
        await Assert.That(ExpertLimitRules.UpgradeError(amateur, novice, 50000, true)).IsNull();
        await Assert.That(ExpertLimitRules.UpgradeError(novice, veteran, 50000, true)).IsNull();
        await Assert.That(ExpertLimitRules.UpgradeError(veteran, Row(4, 40000, show: true, slots: 5), 19999, true))
            .IsEqualTo(ErrorMessageType.ActabilityNotEnoughPoint);
    }

    private static ExpertLimit Row(
        uint id,
        int upLimit,
        bool show,
        byte slots = 0,
        bool language = false,
        bool intensified = false) =>
        new()
        {
            Id = id,
            UpLimit = upLimit,
            ExpertLimitCount = slots,
            Show = show,
            UseLanguageType = language,
            UseIntensified = intensified
        };

    private static List<Actability> Occupied(int count, byte step)
    {
        var list = new List<Actability>(count);
        for (var i = 0; i < count; i++)
            list.Add(Make((uint)(i + 1), step, viewGroup: 1));
        return list;
    }

    private static Actability Make(uint id, byte step, uint viewGroup) =>
        new(new ActabilityTemplate
        {
            Id = id,
            ViewGroupId = viewGroup,
            CountsTowardExpertLimit = true
        })
        {
            Point = 0,
            Step = step
        };
}
