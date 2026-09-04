using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneBuffCreateRelayTests
{
    [Test]
    public async Task Decide_CreatesWhenTheZoneHasNoRecord()
    {
        await Assert.That(ZoneBuffCreateRelay.Decide(null, 1)).IsEqualTo(ZoneBuffCreateAction.Create);
        await Assert.That(ZoneBuffCreateRelay.Decide(null, 60)).IsEqualTo(ZoneBuffCreateAction.Create);
        await Assert.That(ZoneBuffCreateRelay.Decide(null, null)).IsEqualTo(ZoneBuffCreateAction.Create);
    }

    [Test]
    public async Task Decide_ReplacesWhenTheIncomingStackChanged()
    {
        // First Create left the zone at one application. The next Start rebuild carries 2, then 60.
        // Replace (Remove + Create) is what refolds attributes; a second Create would stack entries.
        await Assert.That(ZoneBuffCreateRelay.Decide(1, 2)).IsEqualTo(ZoneBuffCreateAction.Replace);
        await Assert.That(ZoneBuffCreateRelay.Decide(2, 60)).IsEqualTo(ZoneBuffCreateAction.Replace);
        await Assert.That(ZoneBuffCreateRelay.Decide(59, 60)).IsEqualTo(ZoneBuffCreateAction.Replace);
    }

    [Test]
    public async Task Decide_SkipsASameStackRebuild()
    {
        // Overwrite / duration refresh re-enters Start with the same count. Sending another Create
        // would either duplicate the entry or churn Remove+Create on every combat refresh.
        await Assert.That(ZoneBuffCreateRelay.Decide(1, 1)).IsEqualTo(ZoneBuffCreateAction.Skip);
        await Assert.That(ZoneBuffCreateRelay.Decide(60, 60)).IsEqualTo(ZoneBuffCreateAction.Skip);
        await Assert.That(ZoneBuffCreateRelay.Decide(1, null)).IsEqualTo(ZoneBuffCreateAction.Skip);
    }

    [Test]
    public async Task MarkCreated_StoresTheStackUsedForTheNextDecide()
    {
        var zone = (uint)Interlocked.Increment(ref _zoneCounter);
        ZoneBuffRegistry.MarkCreated(zone, 0, 88, 3, stack: 1);
        await Assert.That(ZoneBuffRegistry.TryGetRecordedStack(zone, 0, 88, 3, out var first)).IsTrue();
        await Assert.That(first).IsEqualTo(1u);
        await Assert.That(ZoneBuffCreateRelay.Decide(first, 2)).IsEqualTo(ZoneBuffCreateAction.Replace);

        ZoneBuffRegistry.MarkCreated(zone, 0, 88, 3, stack: 2);
        await Assert.That(ZoneBuffRegistry.TryGetRecordedStack(zone, 0, 88, 3, out var second)).IsTrue();
        await Assert.That(second).IsEqualTo(2u);
        await Assert.That(ZoneBuffCreateRelay.Decide(second, 2)).IsEqualTo(ZoneBuffCreateAction.Skip);
    }

    private static int _zoneCounter = 5200;
}
