using AAEmu.Game.Models.Game.Models;

namespace AAEmu.UnitTests.Game.Models.Game.Models;

/// <summary>
/// Which models the dedicate has to simulate off the ground. Getting this wrong is visible: a model
/// treated as grounded is snapped to the terrain under it at spawn and never gets a flying state, so a
/// shark walks the sea floor instead of swimming and a hawk is dropped out of the air.
/// </summary>
public class ActorModelRulesTests
{
    [Test]
    public async Task MovementIdTwo_BirdsAndFish_IsOffGround()
    {
        await Assert.That(ActorModelRules.SimulatesOffGround(new ActorModel { MovementId = 2 })).IsTrue();
    }

    [Test]
    public async Task FlyMode_FliesWithMovementIdZero_IsOffGround()
    {
        // Kestrels, watchers, wraiths, wisps and ghost ships carry fly_mode and MovementId 0.
        await Assert.That(ActorModelRules.SimulatesOffGround(
            new ActorModel { MovementId = 0, FlyMode = true })).IsTrue();
    }

    [Test]
    public async Task UnderwaterCreature_IsOffGroundAndIsASwimmer()
    {
        // Sharks, jellyfish, kraken and seafolk ship movement_id 1 or 3, so the flag is the only thing
        // that marks them: without it they are ground walkers on the sea floor.
        var shark = new ActorModel { MovementId = 1, UnderwaterCreature = true };
        var sunkenShark = new ActorModel { MovementId = 3, UnderwaterCreature = true };

        await Assert.That(ActorModelRules.SimulatesOffGround(shark)).IsTrue();
        await Assert.That(ActorModelRules.SwimsUnderwater(shark)).IsTrue();
        await Assert.That(ActorModelRules.SimulatesOffGround(sunkenShark)).IsTrue();
        await Assert.That(ActorModelRules.SwimsUnderwater(sunkenShark)).IsTrue();
    }

    [Test]
    public async Task GroundWalkers_StayOnTheGround()
    {
        // movement_id 1 is the mounts, 3 the models sunk into the ground - those belong on the floor.
        await Assert.That(ActorModelRules.SimulatesOffGround(new ActorModel { MovementId = 1 })).IsFalse();
        await Assert.That(ActorModelRules.SimulatesOffGround(new ActorModel { MovementId = 3 })).IsFalse();
        await Assert.That(ActorModelRules.SimulatesOffGround(new ActorModel { MovementId = 0 })).IsFalse();
        await Assert.That(ActorModelRules.SwimsUnderwater(new ActorModel { MovementId = 1 })).IsFalse();
    }

    [Test]
    public async Task AFlyerIsNotASwimmer()
    {
        // The stance is what separates them: a flier takes the flight stance, a swimmer the swim one,
        // and Npc.CurrentGameStance branches on CanFly alone.
        await Assert.That(ActorModelRules.SwimsUnderwater(
            new ActorModel { MovementId = 2, FlyMode = true })).IsFalse();
        await Assert.That(ActorModelRules.HoldsAltitude(
            new ActorModel { UnderwaterCreature = true })).IsFalse();
    }

    [Test]
    public async Task APrefabDoesNotWalk()
    {
        // A siege place, portal, chest or wall has no actor model of its own: the fallback speed for a
        // model that is simply missing must not turn it into something that can walk.
        await Assert.That(ActorModelRules.MoveSpeedFor(isPrefabModel: true, actorMoveSpeed: 1.8f)).IsEqualTo(0f);
        await Assert.That(ActorModelRules.MoveSpeedFor(isPrefabModel: false, actorMoveSpeed: 1.8f)).IsEqualTo(1.8f);
    }

    [Test]
    public async Task SpawnFlags_AreSwimmerOnlyForASwimmerNeverBoth()
    {
        // The regression this guards: answering both flags with the off-ground union gives a swimmer
        // CanFly, and the stance branch reads CanFly first, so a shark takes the flight pose.
        var swimmer = ActorModelRules.SpawnFlags(new ActorModel { MovementId = 1, UnderwaterCreature = true });
        var flyer = ActorModelRules.SpawnFlags(new ActorModel { MovementId = 2 });
        var flyModeOnly = ActorModelRules.SpawnFlags(new ActorModel { MovementId = 0, FlyMode = true });
        var walker = ActorModelRules.SpawnFlags(new ActorModel { MovementId = 1 });

        await Assert.That(swimmer.CanFly).IsFalse();
        await Assert.That(swimmer.IsSwimmer).IsTrue();
        await Assert.That(flyer.CanFly).IsTrue();
        await Assert.That(flyer.IsSwimmer).IsFalse();
        await Assert.That(flyModeOnly.CanFly).IsTrue();
        await Assert.That(flyModeOnly.IsSwimmer).IsFalse();
        await Assert.That(walker.CanFly).IsFalse();
        await Assert.That(walker.IsSwimmer).IsFalse();
    }
}
