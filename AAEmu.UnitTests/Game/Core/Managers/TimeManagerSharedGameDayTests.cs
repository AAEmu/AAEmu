using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Which zones own the shared game day. An instance does not: it ships its own static time-of-day and the
/// zone reports its own clock, so the shared hour must not be pushed into it — that is what left an
/// instance lit wrong.
/// </summary>
public class TimeManagerSharedGameDayTests
{
    private const uint DefaultWorldId = 0;

    [Test]
    public async Task OpenWorld_OwnsTheSharedGameDay()
    {
        var world = new WorldTemplate { Id = DefaultWorldId, Name = "main_world" };
        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsTrue();
    }

    [Test]
    public async Task Instance_DoesNotOwnTheSharedGameDay()
    {
        var world = new WorldTemplate { Id = DefaultWorldId, Name = "instance_eternity" };
        world.XmlWorld = new XmlWorld { IsInstance = 1 };

        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsFalse();
    }

    [Test]
    public async Task OtherWorldTemplate_DoesNotOwnTheSharedGameDay()
    {
        var world = new WorldTemplate { Id = 42, Name = "some_world" };
        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsFalse();
    }

    [Test]
    public async Task MissingWorld_FailsClosed()
    {
        await Assert.That(TimeManager.UsesSharedGameDay(null, DefaultWorldId)).IsFalse();
    }
}
