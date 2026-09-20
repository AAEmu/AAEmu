using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Filling a posted order: who may do it, how the product and labor scale, and how the cast names
/// the row.
/// </summary>
public class CraftOrderProcessRulesTests
{
    private static CraftOrder Order(uint ownerId = 8, uint actability = 5_000) => new()
    {
        Id = 1,
        OwnerId = ownerId,
        CraftId = 5591,
        ItemId = 27902,
        Count = 1,
        Fee = 100_009,
        ActabilityGroupId = 33,
        ActabilityPoint = actability
    };

    [Test]
    public async Task OwnOrder_CannotBeFilledByThePoster()
    {
        var order = Order(ownerId: 8);

        await Assert.That(CraftOrderProcessRules.IsOwnOrder(order, 8)).IsTrue();
        await Assert.That(CraftOrderProcessRules.IsOwnOrder(order, 39)).IsFalse();
        await Assert.That(CraftOrderProcessRules.CanProcess(order, 8, 99_000)).IsFalse();
    }

    [Test]
    public async Task Process_NeedsTheActabilityTheOrderAsksFor()
    {
        var order = Order(actability: 5_000);

        await Assert.That(CraftOrderProcessRules.CanProcess(order, 39, 4_999)).IsFalse();
        await Assert.That(CraftOrderProcessRules.CanProcess(order, 39, 5_000)).IsTrue();
        await Assert.That(CraftOrderProcessRules.CanProcess(null, 39, 5_000)).IsFalse();
    }

    [Test]
    public async Task ProductCount_ScalesWithTheRequestedRuns()
    {
        // Music Paper produces 2 per craft; a count-1 order mails two sheets.
        await Assert.That(CraftOrderProcessRules.ProductCount(2, 1)).IsEqualTo(2);
        await Assert.That(CraftOrderProcessRules.ProductCount(2, 3)).IsEqualTo(6);
        await Assert.That(CraftOrderProcessRules.ProductCount(0, 4)).IsEqualTo(0);
        await Assert.That(CraftOrderProcessRules.ProductCount(-1, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task ProductCount_DoesNotWrapOnAnAbsurdBill()
    {
        await Assert.That(CraftOrderProcessRules.ProductCount(int.MaxValue, 2)).IsEqualTo(0);
    }

    [Test]
    public async Task LaborCost_ScalesWithTheRequestedRuns()
    {
        // Artistry skill 27087 is 5 labor for Music Paper.
        await Assert.That(CraftOrderProcessRules.LaborCost(5, 1)).IsEqualTo(5);
        await Assert.That(CraftOrderProcessRules.LaborCost(5, 3)).IsEqualTo(15);
        await Assert.That(CraftOrderProcessRules.LaborCost(0, 4)).IsEqualTo(0);
        await Assert.That(CraftOrderProcessRules.LaborCost(-2, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task LaborCost_DoesNotWrapPastAnInt()
    {
        await Assert.That(CraftOrderProcessRules.LaborCost(int.MaxValue, 3)).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task OrderId_ReadsTheTwoExtrasAsA64BitId()
    {
        await Assert.That(CraftOrderProcessRules.TryReadOrderId([1, 0], 2, out var one)).IsTrue();
        await Assert.That(one).IsEqualTo(1ul);

        await Assert.That(CraftOrderProcessRules.TryReadOrderId([1, 1], 2, out var wide)).IsTrue();
        await Assert.That(wide).IsEqualTo(1ul | (1ul << 32));

        await Assert.That(CraftOrderProcessRules.TryReadOrderId([7], 1, out var shortId)).IsTrue();
        await Assert.That(shortId).IsEqualTo(7ul);

        await Assert.That(CraftOrderProcessRules.TryReadOrderId([0, 0], 2, out _)).IsFalse();
        await Assert.That(CraftOrderProcessRules.TryReadOrderId([], 0, out _)).IsFalse();
    }

    [Test]
    public async Task MailKeys_AreTheLocaleHelperSendersAndTheCraftArgument()
    {
        await Assert.That(CraftOrderProcessRules.MailArgument(5591)).IsEqualTo("title(5591)");
        await Assert.That(CraftOrderProcessRules.MailBodyArgument(5591)).IsEqualTo("body(5591)");
        await Assert.That(CraftOrderProcessRules.CompletedMailSender.Contains("craftOrderHandle", StringComparison.Ordinal)).IsTrue();
        await Assert.That(CraftOrderProcessRules.FeeMailSender.Contains("craftOrderFee", StringComparison.Ordinal)).IsTrue();
        await Assert.That(CraftOrderProcessRules.ExpiredMailSender.Contains("craftOrderExpired", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ChargeCut_IsTheListedFeeTimesPermille()
    {
        await Assert.That(CraftOrderProcessRules.ChargeCut(20_000_000, 480)).IsEqualTo(9_600_000ul);
        await Assert.That(CraftOrderProcessRules.ChargeCut(10_000_009, 480)).IsEqualTo(4_800_004ul);
        await Assert.That(CraftOrderProcessRules.ChargeCut(9, 480)).IsEqualTo(4ul);
        await Assert.That(CraftOrderProcessRules.ChargeCut(1, 480)).IsEqualTo(0ul);
        await Assert.That(CraftOrderProcessRules.ChargeCut(20_000_000, 0)).IsEqualTo(0ul);
        await Assert.That(CraftOrderProcessRules.ChargeCut(20_000_000, -1)).IsEqualTo(0ul);
    }

    [Test]
    public async Task CrafterPayout_IsTheListedFeeMinusThePermilleCut()
    {
        // 2000g listed, 480 permille (48 %) → 1040g, the split the process window shows.
        await Assert.That(CraftOrderProcessRules.CrafterPayout(20_000_000, 480)).IsEqualTo(10_400_000ul);
        await Assert.That(CraftOrderProcessRules.CrafterPayout(10_000_009, 480)).IsEqualTo(5_200_005ul);
        await Assert.That(CraftOrderProcessRules.CrafterPayout(0, 480)).IsEqualTo(0ul);
        await Assert.That(CraftOrderProcessRules.CrafterPayout(9, 480)).IsEqualTo(5ul);
    }

    [Test]
    public async Task Instant_IsOwnerOnly()
    {
        var order = Order(ownerId: 8);

        await Assert.That(CraftOrderProcessRules.CanInstant(order, 8)).IsTrue();
        await Assert.That(CraftOrderProcessRules.CanInstant(order, 39)).IsFalse();
        await Assert.That(CraftOrderProcessRules.CanInstant(null, 8)).IsFalse();
    }

    [Test]
    public async Task InstantOwnerId_IsTheCharacterIdNotAWorldTaggedComposite()
    {
        await Assert.That(CraftOrderProcessRules.InstantOwnerId(8)).IsEqualTo(8ul);
        await Assert.That(CraftOrderProcessRules.InstantOwnerId(8)).IsNotEqualTo(8ul | (1ul << 32));
        await Assert.That(new CraftOrder { OwnerId = 8, OwnerWorldCharKey = 8ul | (1ul << 32) }
            .ToWireEntry().Unnamed1).IsEqualTo(8ul);
    }

    [Test]
    public async Task ProcessActionKind_IsThePcFillKind()
    {
        // Kind 0 is the fill the process window listens for. Kind 1 is another board action and
        // leaves the window open; kind 5 is the coupon / instant fill of the same event.
        await Assert.That(CraftOrderProcessRules.ProcessActionKind).IsEqualTo((byte)0);
        await Assert.That(CraftOrderProcessRules.InstantActionKind).IsEqualTo((byte)5);
    }
}
