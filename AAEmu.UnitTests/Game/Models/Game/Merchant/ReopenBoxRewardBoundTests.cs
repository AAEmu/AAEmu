using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// The bound on a rolled reopen-box reward: it is delivered as exactly one item, so its count has to be
/// a quantity one stack can hold.
/// </summary>
/// <remarks>
/// Both claim paths - the counter claim and the expiry mail - create a single item from this count, so
/// they have to agree on what is deliverable. A count above <c>items.max_stack_size</c> is refused
/// rather than rounded down: handing out less than the roll promised is worse than refusing it, and
/// the row is left in place rather than consumed.
/// </remarks>
public class ReopenBoxRewardBoundTests
{
    private const uint Item = 4242;
    private const int Stack = 100;

    private static ReopenBoxItemRules.ReopenRewardRefusal Check(int count, int maxStackSize = Stack) =>
        ReopenBoxItemRules.CheckRewardCount(Item, count, maxStackSize);

    /// <summary>
    /// A negative count, swept rather than a single value. A roll is persisted as an int, so a row that
    /// was edited or written by an older build can carry one, and "less than or equal to zero" is the
    /// check that has to hold for all of them.
    /// </summary>
    [Test]
    public async Task ANegativeCountIsRefusedAtEveryNegativeValue()
    {
        foreach (var count in new[] { -1, -2, -100, int.MinValue })
        {
            await Assert.That(Check(count))
                .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NotPositive);
        }
    }

    [Test]
    public async Task ACountOfZeroIsRefusedToo()
    {
        await Assert.That(Check(0))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NotPositive);
    }

    /// <summary>
    /// The oversized case, against both sides of the authored bound. The shipped catalogue carries
    /// <c>max_stack_size</c> of 1, 10, 99, 100 and 1000, so the boundary is worth pinning at values
    /// content actually uses rather than at arbitrary ones.
    /// </summary>
    [Test]
    public async Task ACountAboveTheAuthoredStackSizeIsRefusedAndTheBoundItselfIsNot()
    {
        await Assert.That(Check(101))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.OverStackSize);
        await Assert.That(Check(int.MaxValue))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.OverStackSize);

        // The boundary itself is deliverable: refusing it would refuse a roll that fits exactly.
        await Assert.That(Check(Stack)).IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.None);
        await Assert.That(Check(1)).IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.None);
    }

    /// <summary>Swept over the stack sizes content actually ships, so no single value is special.</summary>
    [Test]
    public async Task EveryShippedStackSizeAcceptsExactlyItsOwnCount()
    {
        foreach (var maxCount in new[] { 1, 10, 99, 100, 1000 })
        {
            await Assert.That(Check(maxCount, maxCount))
                .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.None);
            await Assert.That(Check(maxCount + 1, maxCount))
                .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.OverStackSize);
        }
    }

    /// <summary>
    /// No item rolled is one refusal, and an unknown bound is a different one. An unknown bound is not
    /// a satisfied bound: treating it as unlimited is how an oversized roll would get through.
    /// </summary>
    [Test]
    public async Task NoItemAndAnUnknownBoundAreRefusedSeparately()
    {
        await Assert.That(ReopenBoxItemRules.CheckRewardCount(0, 1, Stack))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NoRewardItem);

        foreach (var unknown in new[] { 0, -1 })
        {
            await Assert.That(Check(1, unknown))
                .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NoTemplate);
        }
    }

    /// <summary>
    /// The non-positive checks come before the bound, so a count that is wrong in both ways reports the
    /// count. Otherwise a negative count against an unknown bound would be reported as a missing
    /// template, which is the less useful of the two answers.
    /// </summary>
    [Test]
    public async Task ANonPositiveCountIsReportedAsSuchEvenWhenTheBoundIsUnknown()
    {
        await Assert.That(Check(-1, 0))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NotPositive);
        await Assert.That(Check(0, -1))
            .IsEqualTo(ReopenBoxItemRules.ReopenRewardRefusal.NotPositive);
    }
}
