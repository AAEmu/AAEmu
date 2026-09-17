using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// Which dimensions the picker lists. These instances exist to spread players over several copies of one
/// place, so the list is the copies that are running — and there have to be copies to choose between.
/// </summary>
public class SysIndunChannelRulesTests
{
    private const int Capacity = 50;

    [Test]
    public async Task Build_ListsCopiesInChannelOrder()
    {
        var rows = SysIndunChannelRules.Build(
        [
            new SysIndunChannel(ChannelId: 3, InstanceId: 900, Current: 7, Restrict: 0),
            new SysIndunChannel(ChannelId: 1, InstanceId: 700, Current: 2, Restrict: 0)
        ], Capacity);

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].ChannelId).IsEqualTo(1);
        await Assert.That(rows[0].InstanceId).IsEqualTo(700u);
        await Assert.That(rows[0].Current).IsEqualTo(2);
        await Assert.That(rows[1].ChannelId).IsEqualTo(3);
        await Assert.That(rows[1].InstanceId).IsEqualTo(900u);
    }

    [Test]
    public async Task Build_TakesCapacityFromTheZone()
    {
        // The client's badge divides current by restrict, so restrict is the instance's own capacity.
        var rows = SysIndunChannelRules.Build([new SysIndunChannel(0, 700, 5, 0)], Capacity);

        await Assert.That(rows[0].Restrict).IsEqualTo(Capacity);
    }

    [Test]
    public async Task Build_KeepsEveryCopyIdApart()
    {
        // The row's id is what the client hands back when it picks, so it has to name the copy.
        var rows = SysIndunChannelRules.Build(
        [
            new SysIndunChannel(0, 700, 0, 0),
            new SysIndunChannel(1, 701, 0, 0)
        ], Capacity);

        await Assert.That(rows.Select(row => row.InstanceId)).IsEquivalentTo(new[] { 700u, 701u });
    }

    [Test]
    public async Task Build_IgnoresDuplicateChannels()
    {
        // Two copies sharing a channel index would draw two rows the client cannot tell apart.
        var rows = SysIndunChannelRules.Build(
        [
            new SysIndunChannel(1, 700, 4, 0),
            new SysIndunChannel(1, 701, 9, 0)
        ], Capacity);

        await Assert.That(rows.Count(row => row.ChannelId == 1)).IsEqualTo(1);
        await Assert.That(rows.Single(row => row.ChannelId == 1).InstanceId).IsEqualTo(700u);
    }

    [Test]
    public async Task Build_StopsAtTheClientsCap()
    {
        var existing = Enumerable.Range(0, SysIndunChannelRules.MaxChannels + 5)
            .Select(i => new SysIndunChannel(i, (uint)(700 + i), 0, 0));

        var rows = SysIndunChannelRules.Build(existing, Capacity);

        await Assert.That(rows.Count).IsEqualTo(SysIndunChannelRules.MaxChannels);
    }

    [Test]
    public async Task Build_ToleratesNoCopies()
    {
        var rows = SysIndunChannelRules.Build(null, Capacity);

        await Assert.That(rows.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChannelsToCreate_AsksForADimensionToMoveToWhenOneIsRunning()
    {
        // The point of the picker is moving to another dimension when the running one fills up.
        var missing = SysIndunChannelRules.ChannelsToCreate([0]);

        await Assert.That(missing).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task ChannelsToCreate_AsksForTheFirstDimensionsWhenNoneRun()
    {
        var missing = SysIndunChannelRules.ChannelsToCreate([]);

        await Assert.That(missing.Count).IsEqualTo(SysIndunChannelRules.MinimumOfferedChannels);
        await Assert.That(missing).IsEquivalentTo(new[] { 0, 1 });
    }

    [Test]
    public async Task ChannelsToCreate_AsksForNothingWhenEnoughRun()
    {
        await Assert.That(SysIndunChannelRules.ChannelsToCreate([0, 2])).IsEmpty();
        await Assert.That(SysIndunChannelRules.ChannelsToCreate([1, 2, 3])).IsEmpty();
    }

    [Test]
    public async Task ChannelsToCreate_FillsTheGapsFirst()
    {
        var missing = SysIndunChannelRules.ChannelsToCreate([2]);

        await Assert.That(missing).IsEquivalentTo(new[] { 0 });
    }
}
