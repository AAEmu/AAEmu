using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TimeManagerClockTests
{
    private const uint DefaultWorldId = 0;

    [Test]
    public async Task UsesSharedGameDay_DefaultOpenWorld_True()
    {
        var world = new WorldTemplate
        {
            Id = DefaultWorldId,
            XmlWorld = new XmlWorld { IsInstance = 0 }
        };

        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsTrue();
    }

    [Test]
    public async Task UsesSharedGameDay_InstanceWorld_FalseEvenIfIdsMatch()
    {
        var world = new WorldTemplate
        {
            Id = DefaultWorldId,
            XmlWorld = new XmlWorld { IsInstance = 1 }
        };

        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsFalse();
    }

    [Test]
    public async Task UsesSharedGameDay_OtherWorldTemplate_False()
    {
        var world = new WorldTemplate
        {
            Id = 7,
            XmlWorld = new XmlWorld { IsInstance = 0 }
        };

        await Assert.That(TimeManager.UsesSharedGameDay(world, DefaultWorldId)).IsFalse();
    }

    [Test]
    public async Task UsesSharedGameDay_UnknownWorld_FailsClosed()
    {
        await Assert.That(TimeManager.UsesSharedGameDay(null, DefaultWorldId)).IsFalse();
    }

    [Test]
    public async Task IsLargeGameHourJump_ExactThreshold_IsNotLarge()
    {
        var from = 1.0f;
        var to = from + TimeManager.MaxWorldEffectJumpHours;
        await Assert.That(TimeManager.IsLargeGameHourJump(from, to)).IsFalse();
    }

    [Test]
    public async Task IsLargeGameHourJump_JustAboveThreshold_IsLarge()
    {
        var from = 1.0f;
        var to = from + TimeManager.MaxWorldEffectJumpHours + 0.01f;
        await Assert.That(TimeManager.IsLargeGameHourJump(from, to)).IsTrue();
    }

    [Test]
    public async Task IsLargeGameHourJump_WrapAroundMidnight_UsesForwardDelta()
    {
        await Assert.That(TimeManager.IsLargeGameHourJump(23.9f, 0.05f)).IsFalse();
        await Assert.That(TimeManager.IsLargeGameHourJump(22.5f, 11.75f)).IsTrue();
    }
}
