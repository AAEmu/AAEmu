using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ButlerHarvestCompletionScopeTests
{
    [Test]
    public async Task ButlerMail_UsesNativeTypeAndLocalizationArguments()
    {
        var now = new DateTime(2026, 9, 12, 12, 30, 0, DateTimeKind.Utc);

        var mail = ButlerHarvestCompletionService.CreateMail(
            42, "Farmer", 1234, 7, true, [], now);

        await Assert.That((byte)mail.MailType).IsEqualTo((byte)49);
        await Assert.That(mail.Header.SenderName).IsEqualTo(".butlerHarvest");
        await Assert.That(mail.Header.ReceiverId).IsEqualTo((uint)42);
        await Assert.That(mail.ReceiverName).IsEqualTo("Farmer");
        await Assert.That(mail.Title).IsEqualTo("title");
        await Assert.That(mail.Body.Text).IsEqualTo("body(1234, 7, true)");
        await Assert.That(mail.Body.SendDate).IsEqualTo(now);
        await Assert.That(mail.Body.RecvDate).IsEqualTo(now);
    }

    [Test]
    public async Task PreCommitDispose_DiscardsStagedMailAndReleasesOnlyRemainingFreshIds()
    {
        var attached = new Item { Id = 10 };
        var unattached = new Item { Id = 11 };
        var live = new Dictionary<ulong, Item> { [10] = attached, [11] = unattached };
        var released = new List<ulong>();
        var discarded = 0;
        var mail = new BaseMail();
        mail.Body.Attachments.Add(attached);

        using (var scope = new ButlerHarvestCompletionService.FreshHarvestRewardScope(
                   staged =>
                   {
                       discarded++;
                       foreach (var item in staged.Body.Attachments)
                           live.Remove(item.Id);
                   },
                   id => live.GetValueOrDefault(id),
                   id => { released.Add(id); live.Remove(id); },
                   [attached, unattached]))
        {
            scope.MarkStaged(mail);
        }

        await Assert.That(discarded).IsEqualTo(1);
        await Assert.That(released).IsEquivalentTo([11UL]);
        await Assert.That(live.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CommittedDispose_PreservesMailAndFreshIds()
    {
        var item = new Item { Id = 10 };
        var live = new Dictionary<ulong, Item> { [10] = item };
        var discarded = 0;
        var released = 0;

        using (var scope = new ButlerHarvestCompletionService.FreshHarvestRewardScope(
                   _ => discarded++,
                   id => live.GetValueOrDefault(id),
                   _ => released++,
                   [item]))
        {
            scope.MarkStaged(new BaseMail());
            scope.MarkCommitted();
        }

        await Assert.That(discarded).IsEqualTo(0);
        await Assert.That(released).IsEqualTo(0);
        await Assert.That(live[10]).IsSameReferenceAs(item);
    }

    [Test]
    public async Task PreCommitDispose_ContinuesOwnedCleanupWhenMailDiscardThrows()
    {
        var item = new Item { Id = 10 };
        var live = new Dictionary<ulong, Item> { [10] = item };
        var released = new List<ulong>();

        using (var scope = new ButlerHarvestCompletionService.FreshHarvestRewardScope(
                   _ => throw new InvalidOperationException("discard failed"),
                   id => live.GetValueOrDefault(id),
                   id => { released.Add(id); live.Remove(id); },
                   [item]))
        {
            scope.MarkStaged(new BaseMail());
        }

        await Assert.That(released).IsEquivalentTo([10UL]);
        await Assert.That(live.Count).IsEqualTo(0);
    }
}
