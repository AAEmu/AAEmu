using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadPhaseWalkTests
{
    // Town mailbox doodad_almighties 320 (7496 is the same loop on 20492/20495/20498/20497).
    private const uint Sit = 107;
    private const uint FlyAway = 10991;
    private const uint Empty = 10993;
    private const uint Land = 10992;

    [Test]
    public async Task MailboxOwl_EachTimerHop_MayRevisitFlyAway()
    {
        var visited = new List<uint>();

        foreach (var phase in new[] { Sit, FlyAway, Empty, Land, Sit })
        {
            DoodadPhaseWalk.Begin(visited);
            await Assert.That(DoodadPhaseWalk.TryVisit(visited, phase)).IsTrue();
        }

        DoodadPhaseWalk.Begin(visited);
        await Assert.That(DoodadPhaseWalk.TryVisit(visited, FlyAway)).IsTrue();
    }

    [Test]
    public async Task MailboxOwl_PersistedVisitSet_BlocksSecondTakeoff()
    {
        var visited = new List<uint>();
        DoodadPhaseWalk.Begin(visited);
        await Assert.That(DoodadPhaseWalk.TryVisit(visited, Sit)).IsTrue();
        DoodadPhaseWalk.Begin(visited);

        foreach (var phase in new[] { FlyAway, Empty, Land, Sit })
            await Assert.That(DoodadPhaseWalk.TryVisit(visited, phase)).IsTrue();

        await Assert.That(DoodadPhaseWalk.TryVisit(visited, FlyAway)).IsFalse();
    }

    [Test]
    public async Task SameWalk_RevisitAbortsAndClears()
    {
        var visited = new List<uint> { Sit };
        await Assert.That(DoodadPhaseWalk.TryVisit(visited, Sit)).IsFalse();
        await Assert.That(visited.Count).IsEqualTo(0);
    }
}
