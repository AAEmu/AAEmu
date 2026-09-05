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
    public async Task ChangePhaseWalk_EachTimerHop_MayRevisitFlyAway()
    {
        var visited = new List<uint> { Sit };

        foreach (var phase in new[] { Sit, FlyAway, Empty, Land, Sit })
        {
            var accepted = DoodadPhaseWalk.Run(visited, () => DoodadPhaseWalk.TryVisit(visited, phase));
            await Assert.That(accepted).IsTrue();
            await Assert.That(visited.Count).IsEqualTo(0);
        }

        var again = DoodadPhaseWalk.Run(visited, () => DoodadPhaseWalk.TryVisit(visited, FlyAway));
        await Assert.That(again).IsTrue();
        await Assert.That(visited.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangePhaseWalk_ClearsEvenWhenTheHopThrows()
    {
        var visited = new List<uint> { Sit };
        try
        {
            DoodadPhaseWalk.Run<int>(visited, () => throw new InvalidOperationException("hop"));
            throw new Exception("expected hop throw");
        }
        catch (InvalidOperationException)
        {
        }

        await Assert.That(visited.Count).IsEqualTo(0);
        var again = DoodadPhaseWalk.Run(visited, () => DoodadPhaseWalk.TryVisit(visited, FlyAway));
        await Assert.That(again).IsTrue();
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
