using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// Which dimensions the picker lists: the copies that are running and that a host is actually serving.
/// A row nothing serves is a dimension the player cannot enter, so it must not be offered.
/// </summary>
public class SysIndunChannelRulesTests
{
    private const int Capacity = 50;

    private static SysIndunChannelCopy Hosted(int channel, uint instanceId, int current = 0, bool hosted = true) =>
        new(new SysIndunChannel(channel, instanceId, current, 0), hosted);

    [Test]
    public async Task BuildOfferable_ListsCopiesInChannelOrder()
    {
        var rows = SysIndunChannelRules.BuildOfferable(
        [
            Hosted(3, 900, current: 7),
            Hosted(1, 700, current: 2)
        ], Capacity);

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].ChannelId).IsEqualTo(1);
        await Assert.That(rows[0].InstanceId).IsEqualTo(700u);
        await Assert.That(rows[0].Current).IsEqualTo(2);
        await Assert.That(rows[1].ChannelId).IsEqualTo(3);
        await Assert.That(rows[1].InstanceId).IsEqualTo(900u);
    }

    [Test]
    public async Task BuildOfferable_TakesCapacityFromTheZone()
    {
        // The client's badge divides current by restrict, so restrict is the instance's own capacity.
        var rows = SysIndunChannelRules.BuildOfferable([Hosted(0, 700, current: 5)], Capacity);

        await Assert.That(rows[0].Restrict).IsEqualTo(Capacity);
    }

    [Test]
    public async Task BuildOfferable_KeepsEveryCopyIdApart()
    {
        // The row's id is what the client hands back when it picks, so it has to name the copy.
        var rows = SysIndunChannelRules.BuildOfferable([Hosted(0, 700), Hosted(1, 701)], Capacity);

        await Assert.That(rows.Select(row => row.InstanceId)).IsEquivalentTo(new[] { 700u, 701u });
    }

    [Test]
    public async Task BuildOfferable_IgnoresDuplicateChannels()
    {
        // Two copies sharing a channel index would draw two rows the client cannot tell apart.
        var rows = SysIndunChannelRules.BuildOfferable(
        [
            Hosted(1, 700, current: 4),
            Hosted(1, 701, current: 9)
        ], Capacity);

        await Assert.That(rows.Count(row => row.ChannelId == 1)).IsEqualTo(1);
        await Assert.That(rows.Single(row => row.ChannelId == 1).InstanceId).IsEqualTo(700u);
    }

    [Test]
    public async Task BuildOfferable_StopsAtTheClientsCap()
    {
        var copies = Enumerable.Range(0, SysIndunChannelRules.MaxChannels + 5)
            .Select(i => Hosted(i, (uint)(700 + i)));

        var rows = SysIndunChannelRules.BuildOfferable(copies, Capacity);

        await Assert.That(rows.Count).IsEqualTo(SysIndunChannelRules.MaxChannels);
    }

    [Test]
    public async Task BuildOfferable_ToleratesNoCopies()
    {
        var rows = SysIndunChannelRules.BuildOfferable(null, Capacity);

        await Assert.That(rows.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuildOfferable_SkipsCopiesNoHostIsServing()
    {
        // A copy that exists as a row but has no host cannot be entered: picking it would strand the player.
        var rows = SysIndunChannelRules.BuildOfferable(
        [
            Hosted(0, 700),
            Hosted(1, 701, hosted: false),
            Hosted(2, 702)
        ], Capacity);

        await Assert.That(rows.Select(row => row.ChannelId)).IsEquivalentTo(new[] { 0, 2 });
    }

    [Test]
    public async Task BuildOfferable_SkipsRowsWithoutACopyId()
    {
        // The client hands the id back on pick and fails on 0, so such a row is not a choice.
        var rows = SysIndunChannelRules.BuildOfferable([Hosted(0, 0), Hosted(1, 701)], Capacity);

        await Assert.That(rows.Select(row => row.ChannelId)).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task BuildOfferable_OffersNothingWhenNoCopyIsHosted()
    {
        // Not a list to pad out: an empty result is what tells the caller the instance is not running.
        var rows = SysIndunChannelRules.BuildOfferable(
        [
            Hosted(0, 700, hosted: false),
            Hosted(1, 701, hosted: false)
        ], Capacity);

        await Assert.That(rows).IsEmpty();
    }

    [Test]
    public async Task FindCopyForPick_TakesTheCopyTheClientNamed()
    {
        // The row's id travels back with the pick, so it — not the channel — is the exact answer.
        var copies = new[] { Hosted(0, 700), Hosted(1, 701) };

        await Assert.That(SysIndunChannelRules.FindCopyForPick(copies, pickedWorldId: 701, pickedChannel: 0))
            .IsEqualTo(701u);
    }

    [Test]
    public async Task FindCopyForPick_FallsBackToTheChannel()
    {
        // A copy that has been rebuilt since the list was sent still has its channel.
        var copies = new[] { Hosted(0, 700), Hosted(1, 701) };

        await Assert.That(SysIndunChannelRules.FindCopyForPick(copies, pickedWorldId: 999, pickedChannel: 1))
            .IsEqualTo(701u);
    }

    [Test]
    public async Task FindCopyForPick_IgnoresCopiesNoHostIsServing()
    {
        // Landing in a copy nothing serves is impossible, so a pick that only names one is not honoured.
        var copies = new[] { Hosted(0, 700), Hosted(1, 701, hosted: false) };

        await Assert.That(SysIndunChannelRules.FindCopyForPick(copies, pickedWorldId: 701, pickedChannel: 1))
            .IsNull();
    }

    [Test]
    public async Task FindCopyForPick_ReturnsNothingWhenNothingWasPicked()
    {
        var copies = new[] { Hosted(0, 700), Hosted(1, 701) };

        await Assert.That(SysIndunChannelRules.FindCopyForPick(copies, null, null)).IsNull();
    }

    [Test]
    public async Task HonourPick_OnlyWhenTheInstanceSelectsAChannelAndACopyWasNamed()
    {
        await Assert.That(SysIndunChannelRules.HonourPick(selectChannel: true, pickedCopyId: 701)).IsTrue();
        await Assert.That(SysIndunChannelRules.HonourPick(selectChannel: false, pickedCopyId: 701)).IsFalse();
        await Assert.That(SysIndunChannelRules.HonourPick(selectChannel: true, pickedCopyId: 0)).IsFalse();
    }

    [Test]
    public async Task CopyIsHosted_MissingProbeAssumesHosted()
    {
        await Assert.That(SysIndunChannelRules.CopyIsHosted(null, 243, 1)).IsTrue();
        await Assert.That(SysIndunChannelRules.CopyIsHosted((_, _) => false, 243, 1)).IsFalse();
        await Assert.That(SysIndunChannelRules.CopyIsHosted((_, _) => true, 243, 1)).IsTrue();
    }
}
