using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.UnitTests.Game.Models.Game.Trading;

public class SpecialtySaleCommitterTests
{
    [Test]
    public async Task Commit_Committed_PublishesWithoutDiscarding()
    {
        var store = new FakeSpecialtySaleStore { Result = SpecialtySaleCommitResult.Committed };
        var committer = new SpecialtySaleCommitter(store);
        var published = 0;
        var discarded = 0;
        var write = CreateWrite();

        var result = committer.Commit(write, () => published++, () => discarded++);

        await Assert.That(result).IsEqualTo(SpecialtySaleCommitResult.Committed);
        await Assert.That(store.ReceivedWrite).IsSameReferenceAs(write);
        await Assert.That(store.CommitCalls).IsEqualTo(1);
        await Assert.That(published).IsEqualTo(1);
        await Assert.That(discarded).IsEqualTo(0);
    }

    [Test]
    [Arguments(SpecialtySaleCommitResult.PackNotPersisted)]
    [Arguments(SpecialtySaleCommitResult.LaborConflict)]
    [Arguments(SpecialtySaleCommitResult.MarketConflict)]
    public async Task Commit_Noncommitted_DiscardsWithoutPublishing(SpecialtySaleCommitResult storeResult)
    {
        var store = new FakeSpecialtySaleStore { Result = storeResult };
        var committer = new SpecialtySaleCommitter(store);
        var published = 0;
        var discarded = 0;

        var result = committer.Commit(CreateWrite(), () => published++, () => discarded++);

        await Assert.That(result).IsEqualTo(storeResult);
        await Assert.That(published).IsEqualTo(0);
        await Assert.That(discarded).IsEqualTo(1);
    }

    [Test]
    public async Task Commit_StoreThrows_DiscardsAndRethrows()
    {
        var expected = new InvalidOperationException("store failed");
        var store = new FakeSpecialtySaleStore { Exception = expected };
        var committer = new SpecialtySaleCommitter(store);
        var published = 0;
        var discarded = 0;

        var actual = Assert.Throws<InvalidOperationException>(() =>
            committer.Commit(CreateWrite(), () => published++, () => discarded++));

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(published).IsEqualTo(0);
        await Assert.That(discarded).IsEqualTo(1);
    }

    [Test]
    public async Task Commit_PublicationThrows_DoesNotDiscardCommittedPreparation()
    {
        var store = new FakeSpecialtySaleStore { Result = SpecialtySaleCommitResult.Committed };
        var committer = new SpecialtySaleCommitter(store);
        var expected = new InvalidOperationException("publication failed");
        var discarded = 0;

        var actual = Assert.Throws<InvalidOperationException>(() =>
            committer.Commit(CreateWrite(), () => throw expected, () => discarded++));

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(discarded).IsEqualTo(0);
    }

    [Test]
    public async Task Commit_ConcurrentDuplicateClaim_PublishesOnlyOneAttempt()
    {
        var store = new SingleClaimSpecialtySaleStore();
        var committer = new SpecialtySaleCommitter(store);
        var published = 0;
        var discarded = 0;

        var attempts = await Task.WhenAll(
            Task.Run(() => committer.Commit(
                CreateWrite(),
                () => Interlocked.Increment(ref published),
                () => Interlocked.Increment(ref discarded))),
            Task.Run(() => committer.Commit(
                CreateWrite(),
                () => Interlocked.Increment(ref published),
                () => Interlocked.Increment(ref discarded))));

        await Assert.That(attempts).Contains(SpecialtySaleCommitResult.Committed);
        await Assert.That(attempts).Contains(SpecialtySaleCommitResult.PackNotPersisted);
        await Assert.That(published).IsEqualTo(1);
        await Assert.That(discarded).IsEqualTo(1);
    }

    private static SpecialtySaleWrite CreateWrite()
    {
        return new SpecialtySaleWrite(
            1,
            2,
            3,
            4,
            SlotType.Equipment,
            5,
            6,
            100,
            90,
            50,
            40,
            [],
            new SpecialtyMarketWrite(new SpecialtyMarketState(), new SpecialtyMarketState { Revision = 1 }));
    }

    private sealed class FakeSpecialtySaleStore : ISpecialtySaleStore
    {
        public SpecialtySaleCommitResult Result { get; init; }
        public Exception Exception { get; init; }
        public SpecialtySaleWrite ReceivedWrite { get; private set; }
        public int CommitCalls { get; private set; }

        public SpecialtySaleCommitResult Commit(SpecialtySaleWrite write)
        {
            CommitCalls++;
            ReceivedWrite = write;
            if (Exception != null)
                throw Exception;
            return Result;
        }
    }

    private sealed class SingleClaimSpecialtySaleStore : ISpecialtySaleStore
    {
        private int _claimed;

        public SpecialtySaleCommitResult Commit(SpecialtySaleWrite write)
        {
            return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0
                ? SpecialtySaleCommitResult.Committed
                : SpecialtySaleCommitResult.PackNotPersisted;
        }
    }
}
