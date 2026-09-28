using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The parts of the "grants exactly once" guarantee that do not need a database.
/// <para>
/// The durable half of the guarantee lives in the MySQL claim ledger and is covered by the opt-in
/// integration tests in <c>AAEmu.IntegrationTests/IndunRewardDeliveryIntegrationTests</c>, which
/// cannot run without a live server. These cases cover the two halves that are pure: how the roster
/// becomes claim subjects, and how the selected content becomes the exact set of grant rows a claim
/// may carry.
/// </para>
/// <para>
/// Both cases are written so that removing the guard makes them fail rather than silently pass. A
/// world roster is keyed by object id, so a relog race really can hold one character twice; that is
/// the fixture for the first case, not a synthetic shape.
/// </para>
/// </summary>
public class IndunRewardExactlyOnceTests
{
    private static Character Roster(uint id, uint objId, string name) =>
        new(new UnitCustomModelParams()) { Id = id, ObjId = objId, Name = name };

    private static InstanceReward Reward(uint id, int start, int end) =>
        new(id, 900, 6, start, end, 1, false, 100, InstanceRewardTargetType.Item, false, false);

    private static InstanceRewardBonusCount Bonus(uint id, uint rewardId, uint buffId, int count) =>
        new(id, rewardId, buffId, count);

    /// <summary>
    /// One claim subject per character id, even when the world holds that character under two object
    /// ids. Two subjects for one id would be two delivery attempts under one claim key, so this is
    /// where a double grant would start.
    /// </summary>
    [Test]
    public async Task ResolveRecipients_CollapsesTwoObjectIdsForOneCharacterIntoOneClaimSubject()
    {
        // Same character id 42 under two object ids: the state a relog race leaves behind.
        var roster = new[] { Roster(42, 0x2A, "Twice"), Roster(42, 0x2B, "Twice"), Roster(7, 0x07, "Other") };

        var recipients = IndunRewardDeliveryService.ResolveRecipients(roster);

        await Assert.That(recipients.Count).IsEqualTo(2);
        await Assert.That(recipients[0].Id).IsEqualTo(7u);
        await Assert.That(recipients[1].Id).IsEqualTo(42u);
        await Assert.That(recipients.Count(recipient => recipient.Id == 42u)).IsEqualTo(1);
    }

    /// <summary>
    /// The subject order is the claim order, so two attempts at the same run walk the same subjects in
    /// the same order and the per-attempt keys are reproducible.
    /// </summary>
    [Test]
    public async Task ResolveRecipients_OrdersSubjectsByCharacterId()
    {
        var roster = new[] { Roster(300, 0x03, "C"), Roster(100, 0x01, "A"), Roster(200, 0x02, "B") };

        var recipients = IndunRewardDeliveryService.ResolveRecipients(roster);

        await Assert.That(recipients[0].Id).IsEqualTo(100u);
        await Assert.That(recipients[1].Id).IsEqualTo(200u);
        await Assert.That(recipients[2].Id).IsEqualTo(300u);
    }

    [Test]
    public async Task ResolveRecipients_DropsNullRosterEntriesAndNeverInventsASubject()
    {
        var roster = new Character[] { null, Roster(5, 0x05, "Solo"), null };

        var recipients = IndunRewardDeliveryService.ResolveRecipients(roster);

        await Assert.That(recipients.Count).IsEqualTo(1);
        await Assert.That(recipients[0].Id).IsEqualTo(5u);
        await Assert.That(recipients[0].Name).IsEqualTo("Solo");
    }

    /// <summary>
    /// The grant set a claim may carry is a pure, ordered function of the selected content, so a retry
    /// of one run re-derives byte-identical grant keys. Drop the ordering and the keys stop being
    /// reproducible, which is what lets a second attempt land beside the first instead of colliding
    /// with it.
    /// </summary>
    [Test]
    public async Task SelectBonusCounts_IsReproducibleForTheSameSelectionSoARetryCollides()
    {
        var selected = new[] { Reward(1, 1, 4), Reward(2, 1, 4) };
        var authored = new[]
        {
            Bonus(20, 2, 5152, 1),
            Bonus(10, 1, 7149, 1),
            Bonus(11, 1, 21519, 3)
        };

        var first = IndunRewardSelectionRules.SelectBonusCounts(selected, authored);
        var second = IndunRewardSelectionRules.SelectBonusCounts(selected, authored);

        await Assert.That(first.Count).IsEqualTo(3);
        await Assert.That(second.Count).IsEqualTo(first.Count);
        // Same rows, same order, same counts: a repeat attempt produces the same insert keys.
        await Assert.That(second.Select(bonus => (bonus.InstanceRewardId, bonus.BuffId, bonus.Count)))
            .IsEquivalentTo(first.Select(bonus => (bonus.InstanceRewardId, bonus.BuffId, bonus.Count)));
        await Assert.That(first[0].InstanceRewardId).IsEqualTo(1u);
        await Assert.That(first[0].BuffId).IsEqualTo(7149u);
        await Assert.That(first[2].InstanceRewardId).IsEqualTo(2u);
    }

    /// <summary>
    /// A bonus row attached to a reward the selection did not choose is refused. Without this the same
    /// claim could carry a different, wider grant set on a second attempt.
    /// </summary>
    [Test]
    public async Task SelectBonusCounts_RefusesAGrantThatBelongsToAnUnselectedReward()
    {
        var selected = new[] { Reward(1, 1, 4) };
        var authored = new[] { Bonus(10, 1, 7149, 1), Bonus(20, 2, 5152, 1) };

        await Assert.That(() => IndunRewardSelectionRules.SelectBonusCounts(selected, authored))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// Two bands of the same instance carry their own grant rows, and a selection only ever sees the
    /// rows attached to the rewards it chose. The two resulting grant sets are therefore disjoint, so a
    /// later band cannot be mistaken for a retry of the earlier one.
    /// </summary>
    [Test]
    public async Task SelectBonusCounts_DistinguishesTwoDifferentSelectionsOfTheSameInstance()
    {
        var lowBand = IndunRewardSelectionRules.SelectBonusCounts(
            [Reward(1, 1, 1)], [Bonus(10, 1, 7149, 1)]);
        var highBand = IndunRewardSelectionRules.SelectBonusCounts(
            [Reward(2, 5, 5)], [Bonus(20, 2, 5152, 2)]);

        await Assert.That(lowBand.Count).IsEqualTo(1);
        await Assert.That(lowBand[0].InstanceRewardId).IsEqualTo(1u);
        await Assert.That(lowBand[0].BuffId).IsEqualTo(7149u);
        await Assert.That(highBand.Count).IsEqualTo(1);
        await Assert.That(highBand[0].InstanceRewardId).IsEqualTo(2u);
        await Assert.That(highBand[0].BuffId).IsEqualTo(5152u);
        await Assert.That(highBand[0].BuffId).IsNotEqualTo(lowBand[0].BuffId);
    }

    /// <summary>
    /// A zero-amount selection is refused before any connection is opened, so no claim, mail or grant
    /// row can exist for it. The connection factory counts its calls: remove the guard and the count
    /// goes from zero to one, which is what makes this case fail instead of pass quietly.
    /// </summary>
    [Test]
    public async Task DeliverForRun_RefusesAZeroAmountSelectionBeforeAnythingIsClaimed()
    {
        var zeroAmount = new InstanceReward(
            1, 900, 6, 1, 4, 0, false, 100, InstanceRewardTargetType.Item, false, false);
        var mailText = new InstanceRewardMailText(11, 900, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);
        var recipients = new[] { new IndunRewardRecipient(1, "One") };

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-zero", 900, 6, 1, recipients, [zeroAmount], mailText,
            [new InstanceRewardBonusCount(10, 1, 7149, 1)]);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.Failed);
        await Assert.That(opened).IsEqualTo(0);
    }

    /// <summary>
    /// A reward whose attachment count exceeds what one mail can carry is refused on the same terms,
    /// so a claim is never written for a delivery that cannot be assembled.
    /// </summary>
    [Test]
    public async Task DeliverForRun_RefusesAnOverfullAttachmentSetBeforeAnythingIsClaimed()
    {
        var rewards = Enumerable.Range(1, MailBody.MaxMailAttachments + 1)
            .Select(index => new InstanceReward(
                (uint)index, 900, 6, 1, 1, 1, false, 100, InstanceRewardTargetType.Item, false, false))
            .ToArray();
        var mailText = new InstanceRewardMailText(11, 900, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);
        var recipients = new[] { new IndunRewardRecipient(1, "One") };

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-overfull", 900, 6, 1, recipients, rewards, mailText);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.Failed);
        await Assert.That(opened).IsEqualTo(0);
    }

    /// <summary>
    /// An empty roster claims nothing and opens nothing.
    /// </summary>
    [Test]
    public async Task DeliverForRun_WithNoRecipientsClaimsNothingAndOpensNothing()
    {
        var reward = new InstanceReward(
            1, 900, 6, 1, 4, 1, false, 100, InstanceRewardTargetType.Item, false, false);
        var mailText = new InstanceRewardMailText(11, 900, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-empty", 900, 6, 1, [], [reward], mailText);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.NoRecipients);
        await Assert.That(opened).IsEqualTo(0);
    }

    /// <summary>
    /// A selection value outside every authored range is refused before anything is claimed; a missing
    /// range must never fall through to an invented reward.
    /// </summary>
    [Test]
    public async Task DeliverForRun_RefusesASelectionValueNoAuthoredRangeCovers()
    {
        var reward = new InstanceReward(
            1, 900, 6, 1, 4, 1, false, 100, InstanceRewardTargetType.Item, false, false);
        var mailText = new InstanceRewardMailText(11, 900, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);
        var recipients = new[] { new IndunRewardRecipient(1, "One") };

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-gap", 900, 6, 99, recipients, [reward], mailText);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.SelectionUnavailable);
        await Assert.That(opened).IsEqualTo(0);
    }

    /// <summary>
    /// Mail copy that does not belong to the delivered instance is refused. Without the instance check
    /// a run could be answered with another instance's authored text.
    /// </summary>
    [Test]
    public async Task DeliverForRun_RefusesMailCopyBelongingToAnotherInstance()
    {
        var reward = new InstanceReward(
            1, 900, 6, 1, 4, 1, false, 100, InstanceRewardTargetType.Item, false, false);
        var foreignMailText = new InstanceRewardMailText(11, 901, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);
        var recipients = new[] { new IndunRewardRecipient(1, "One") };

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-foreign", 900, 6, 1, recipients, [reward], foreignMailText);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.SelectionUnavailable);
        await Assert.That(opened).IsEqualTo(0);
    }

    /// <summary>
    /// A bonus row that names a reward this run did not select is refused before the claim is written,
    /// so a claim can never carry a grant that belongs to a different band.
    /// </summary>
    [Test]
    public async Task DeliverForRun_RefusesABonusRowForARewardThisRunDidNotSelect()
    {
        var reward = new InstanceReward(
            1, 900, 6, 1, 4, 1, false, 100, InstanceRewardTargetType.Item, false, false);
        var mailText = new InstanceRewardMailText(11, 900, "sender", "title", "body", 1, InstanceRewardMailKind.Basic);
        var recipients = new[] { new IndunRewardRecipient(1, "One") };

        var opened = 0;
        var service = new IndunRewardDeliveryService(
            () => { opened++; return null!; },
            Mock.Of<IMailManager>().Object,
            Mock.Of<IItemManager>().Object);

        var result = service.DeliverForRun("run-stray", 900, 6, 1, recipients, [reward], mailText,
            [new InstanceRewardBonusCount(10, 2, 7149, 1)]);

        await Assert.That(result).IsEqualTo(IndunRewardDeliveryResult.SelectionUnavailable);
        await Assert.That(opened).IsEqualTo(0);
    }
}
