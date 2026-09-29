using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// What happens to a mailed reopen-box reward once the row has committed, or has not.
/// </summary>
/// <remarks>
/// A commit alone is not a delivery. <c>CreateUnpersisted</c> keeps its item out of the world save and
/// <c>TryDeliverOn</c> keeps the letter out of the mailbox until someone publishes each, so a
/// transaction that commits without publishing both leaves a reward that exists in the database and
/// nowhere else until a restart. The abandoned case is the mirror: an unpersisted item holds a
/// reserved id and a row, so it has to be released or both leak.
/// <para>
/// These were previously asymmetric - the commit branch had no publish at all - and nothing tested
/// either branch, so the lifecycle is now one method both cases go through and can be held to the
/// same shape.
/// </para>
/// </remarks>
public class ReopenBoxDeliveryLifecycleTests
{
    private readonly List<string> _order = [];

    private static IItemManager RecordingItems(List<string> order)
    {
        var items = Mock.Of<IItemManager>();
        items.PublishPersistedItems(Any<IEnumerable<Item>>())
            .Callback((IEnumerable<Item> _) => order.Add("publish-item"));
        items.DiscardUnpersistedItems(Any<IEnumerable<Item>>())
            .Callback((IEnumerable<Item> _) => order.Add("discard-item"));
        return items.Object;
    }

    private static IMailManager RecordingMail(List<string> order)
    {
        var mail = Mock.Of<IMailManager>();
        mail.PublishDelivered(Any<BaseMail>())
            .Callback((BaseMail _) => order.Add("publish-letter"));
        mail.DiscardUnpersisted(Any<BaseMail>())
            .Callback((BaseMail _) => order.Add("discard-letter"));
        return mail.Object;
    }

    /// <summary>
    /// A committed delivery publishes both. Dropping either one is the defect: the row says delivered
    /// while the player has nothing.
    /// </summary>
    [Test]
    public async Task ACommittedDeliveryPublishesTheItemAndTheLetter()
    {
        var items = RecordingItems(_order);
        var mail = RecordingMail(_order);

        ReopenBoxItemRules.FinishDelivery(items, mail, new Item(), Mock.Of<BaseMail>(), true);

        await Assert.That(_order).IsEquivalentTo(new[] { "publish-item", "publish-letter" });
    }

    /// <summary>
    /// The order matters as much as the calls: the item becomes live and then the letter joins the
    /// mailbox. Publishing the letter first would let a client see a mail whose attachment is not yet
    /// real.
    /// </summary>
    [Test]
    public async Task ACommittedDeliveryPublishesTheItemBeforeTheLetter()
    {
        var items = RecordingItems(_order);
        var mail = RecordingMail(_order);

        ReopenBoxItemRules.FinishDelivery(items, mail, new Item(), Mock.Of<BaseMail>(), true);

        await Assert.That(_order.Count).IsEqualTo(2);
        await Assert.That(_order[0]).IsEqualTo("publish-item");
        await Assert.That(_order[1]).IsEqualTo("publish-letter");
    }

    /// <summary>
    /// An abandoned attempt releases both and publishes neither. This is the branch that was missing
    /// entirely, and it is what stops a failed delivery leaking a reserved id and an orphan item row.
    /// </summary>
    [Test]
    public async Task AnAbandonedDeliveryDiscardsBothAndPublishesNeither()
    {
        var items = RecordingItems(_order);
        var mail = RecordingMail(_order);

        ReopenBoxItemRules.FinishDelivery(items, mail, new Item(), Mock.Of<BaseMail>(), false);

        await Assert.That(_order).IsEquivalentTo(new[] { "discard-item", "discard-letter" });
    }

    /// <summary>
    /// The two branches are the same shape with opposite calls, and a rewrite that made one look like
    /// the other would be the regression. Pinned as sets, not as sequences, because the order within a
    /// branch is not the property being asserted.
    /// </summary>
    [Test]
    public async Task TheTwoBranchesCallTheOppositePairAndNothingElse()
    {
        var committed = new List<string>();
        ReopenBoxItemRules.FinishDelivery(
            RecordingItems(committed), RecordingMail(committed), new Item(), Mock.Of<BaseMail>(), true);

        var abandoned = new List<string>();
        ReopenBoxItemRules.FinishDelivery(
            RecordingItems(abandoned), RecordingMail(abandoned), new Item(), Mock.Of<BaseMail>(), false);

        await Assert.That(committed.Contains("discard-item")).IsFalse();
        await Assert.That(committed.Contains("discard-letter")).IsFalse();
        await Assert.That(abandoned.Contains("publish-item")).IsFalse();
        await Assert.That(abandoned.Contains("publish-letter")).IsFalse();
    }

    /// <summary>
    /// A failure in one call must not escape, because the caller's own outcome is the thing being
    /// reported - a throw here would mask the real failure with a secondary one.
    /// </summary>
    [Test]
    public async Task AThrowingPublishDoesNotEscapeTheLifecycle()
    {
        var items = Mock.Of<IItemManager>();
        items.PublishPersistedItems(Any<IEnumerable<Item>>())
            .Throws(new InvalidOperationException("save unavailable"));

        // Reaching the assertion is the test: no exception left the method.
        ReopenBoxItemRules.FinishDelivery(items.Object, Mock.Of<IMailManager>().Object, new Item(), Mock.Of<BaseMail>(), true);

        await Assert.That(_order).IsEmpty();
    }
}
