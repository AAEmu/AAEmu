using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.UnitTests.Game.Models.Game.Residents;

/// <summary>
/// The resident-map announce diff: joins announce Add once, repeats announce nothing, leaves
/// announce Remove once. The client's resident map only ever changes through these packets.
/// </summary>
public class ResidentMapAnnounceTests
{
    [Test]
    public async Task FirstPush_AnnouncesEveryCurrentGroupExactlyOnce()
    {
        var announced = new HashSet<uint>();

        var first = ResidentMapAnnounce.Push([3, 1], announced);
        await Assert.That(first.Adds).IsEquivalentTo(new List<uint> { 1, 3 });
        await Assert.That(first.Removes).IsEmpty();

        // A second push with unchanged membership announces nothing: exactly once.
        var repeat = ResidentMapAnnounce.Push([1, 3], announced);
        await Assert.That(repeat.Adds).IsEmpty();
        await Assert.That(repeat.Removes).IsEmpty();
    }

    [Test]
    public async Task LeavingAndJoining_AnnouncesEachChangeOnce()
    {
        var announced = new HashSet<uint>();
        ResidentMapAnnounce.Push([1, 2], announced);

        var diff = ResidentMapAnnounce.Push([2, 3], announced);
        await Assert.That(diff.Adds).IsEquivalentTo(new List<uint> { 3 });
        await Assert.That(diff.Removes).IsEquivalentTo(new List<uint> { 1 });

        // Replaying the same membership is a no-op: the Remove was delivered exactly once.
        var repeat = ResidentMapAnnounce.Push([2, 3], announced);
        await Assert.That(repeat.Adds).IsEmpty();
        await Assert.That(repeat.Removes).IsEmpty();
        await Assert.That(announced).IsEquivalentTo(new HashSet<uint> { 2, 3 });
    }

    [Test]
    public async Task LosingEveryGroup_RemovesEachOneOnce()
    {
        var announced = new HashSet<uint>();
        ResidentMapAnnounce.Push([1, 2], announced);

        var diff = ResidentMapAnnounce.Push([], announced);
        await Assert.That(diff.Adds).IsEmpty();
        await Assert.That(diff.Removes).IsEquivalentTo(new List<uint> { 1, 2 });
        await Assert.That(announced).IsEmpty();
    }
}
