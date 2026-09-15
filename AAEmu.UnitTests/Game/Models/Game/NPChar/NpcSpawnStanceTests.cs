using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// The stance a spawned unit takes. A swimmer must not be given the flight pose: CanFly is what this
/// branch reads first, so a model flagged as both flies, and the client plays the flight cycle for a
/// shark that is swimming.
/// </summary>
public class NpcSpawnStanceTests
{
    [Test]
    public async Task ASwimmerTakesTheSwimStanceAndNotTheFlightOne()
    {
        var npc = CreateNpc();
        npc.IsSwimmer = true;
        npc.IsUnderWater = true;

        npc.CurrentGameStance = GameStanceType.Relaxed;
        await Assert.That(npc.CurrentGameStance).IsEqualTo(GameStanceType.Swim);

        npc.CurrentGameStance = GameStanceType.Combat;
        await Assert.That(npc.CurrentGameStance).IsEqualTo(GameStanceType.CoSwim);
    }

    [Test]
    public async Task AFlyerTakesTheFlightStance()
    {
        var npc = CreateNpc();
        npc.CanFly = true;

        npc.CurrentGameStance = GameStanceType.Relaxed;

        await Assert.That(npc.CurrentGameStance).IsEqualTo(GameStanceType.Fly);
    }

    [Test]
    public async Task AGroundWalkerKeepsTheStanceItWasGiven()
    {
        var npc = CreateNpc();

        npc.CurrentGameStance = GameStanceType.Relaxed;

        await Assert.That(npc.CurrentGameStance).IsEqualTo(GameStanceType.Relaxed);
    }

    private static Npc CreateNpc() => new() { Template = new NpcTemplate { Id = 1 } };
}
