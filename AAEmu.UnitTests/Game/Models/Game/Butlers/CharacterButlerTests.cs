using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public class CharacterButlerTests
{
    [Test]
    public async Task ApplyLoadedState_CopiesPermanentDataAndHarvestJobs()
    {
        var permanentDatas = new Dictionary<sbyte, ulong> { [2] = 3, [7] = 1 };
        var jobs = new List<ButlerHarvestJob>
        {
            new(41, 7, 12, 3, 60, 1_700_000_000)
        };
        var butler = new CharacterButler(10);

        butler.ApplyLoadedState(new CharacterButlerStateRecord(
            new CharacterButlerRecord(10, 20, "Mira", 900, 5, 700, 1_700_000_000),
            permanentDatas,
            jobs));
        permanentDatas[2] = 99;
        jobs.Clear();

        await Assert.That(butler.HouseId).IsEqualTo((uint)20);
        await Assert.That(butler.PermanentDatas[2]).IsEqualTo((ulong)3);
        await Assert.That(butler.HarvestJobs[41].StaticHarvestId).IsEqualTo((uint)7);
        await Assert.That(butler.HarvestJobs[41].UpdateTime).IsEqualTo(1_700_000_000L);
        await Assert.That(butler.LpChargeResetTime).IsEqualTo(1_700_000_000L);
    }

    [Test]
    public async Task Snapshots_AreDetachedFromLaterAggregateChanges()
    {
        var butler = new CharacterButler(10);
        butler.ApplyPermanentData(2, 1);
        butler.ApplyHarvestJob(new ButlerHarvestJob(41, 7, 12, 3, 60, 1_700_000_000));

        var permanentDatas = butler.SnapshotPermanentDatas();
        var jobs = butler.SnapshotHarvestJobs();
        butler.ApplyPermanentData(2, 2);
        butler.RemoveHarvestJob(41);

        await Assert.That(permanentDatas[2]).IsEqualTo((ulong)1);
        await Assert.That(jobs).HasCount().EqualTo(1);
        await Assert.That(jobs[0].JobId).IsEqualTo(41L);
    }

    [Test]
    public async Task StoredGardens_AreKeyedByPersistentItemIdentity()
    {
        var butler = new CharacterButler(10);

        butler.ApplyStoredItem(new ButlerStoredItem(2, 101));
        butler.ApplyStoredItem(new ButlerStoredItem(2, 102));

        await Assert.That(butler.StoredItems).HasCount().EqualTo(2);
        await Assert.That(butler.StoredItems[101].ItemId).IsEqualTo((ulong)101);
        await Assert.That(butler.StoredItems[102].ItemId).IsEqualTo((ulong)102);
    }
}
