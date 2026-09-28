using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.UnitTests.Game.Models.Game.CommonFarm;

/// <summary>
/// The record count one farm-list response may carry.
/// </summary>
/// <remarks>
/// The reader stops at <see cref="CommonFarmListRules.MaxRecordCount"/> records however large the
/// count says, so a server that writes more desynchronises the client instead of showing a longer
/// list. The bound has to be applied where the body is written, and the case that reaches it has to
/// be reported rather than passed off as a short list.
/// </remarks>
public class CommonFarmListRulesTests
{
    [Test]
    public async Task AListInsideTheBoundIsWrittenWholeAndIsNotReportedAsTruncated()
    {
        var ok = CommonFarmListRules.TryResolveCount(3, 3, out var count, out var truncated);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(3);
        // The truncation flag is what makes the shortfall visible. A bound that silently shortens
        // the list is the failure this whole rule exists to prevent.
        await Assert.That(truncated).IsFalse();
    }

    [Test]
    public async Task AListAtTheBoundIsStillWrittenWhole()
    {
        // Exactly at the reader's limit is not yet a truncation; one record past it is.
        var ok = CommonFarmListRules.TryResolveCount(
            CommonFarmListRules.MaxRecordCount, CommonFarmListRules.MaxRecordCount,
            out var count, out var truncated);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(CommonFarmListRules.MaxRecordCount);
        await Assert.That(truncated).IsFalse();
    }

    [Test]
    public async Task OneRecordPastTheBoundIsCutAndReported()
    {
        var available = CommonFarmListRules.MaxRecordCount + 1;

        var ok = CommonFarmListRules.TryResolveCount(available, available, out var count, out var truncated);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(CommonFarmListRules.MaxRecordCount);
        await Assert.That(truncated).IsTrue();
    }

    [Test]
    public async Task AHostileCountIsCutToTheBoundEvenWhenTheCallerClaimsMoreThanItHas()
    {
        // Both bounds apply, and the smaller one wins. Clamping to the reader's limit alone would let
        // a caller claim more records than it holds and fill the rest with whatever followed.
        var ok = CommonFarmListRules.TryResolveCount(100_000, 2, out var count, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task ANegativeCountIsRefusedRatherThanTreatedAsAnEmptyList()
    {
        // The count is signed on the wire. A negative value is a malformed response, not an empty one,
        // and refusing it is what stops a caller looping on a very large unsigned reading.
        var ok = CommonFarmListRules.TryResolveCount(-1, 5, out var count, out _);

        await Assert.That(ok).IsFalse();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task AnEmptyListWritesNoRecordsAndIsNotATruncation()
    {
        var ok = CommonFarmListRules.TryResolveCount(0, 0, out var count, out var truncated);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(truncated).IsFalse();
    }

    [Test]
    public async Task AnUnavailableCountOfZeroCannotBecomeAnUnboundedWrite()
    {
        // A caller that reports "nothing available" must not be able to turn that into a body full of
        // records: the available count bounds the write even when the requested count is huge.
        var ok = CommonFarmListRules.TryResolveCount(1000, 0, out var count, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(count).IsEqualTo(0);
    }
}
