using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveSummonSeedRulesTests
{
    [Test]
    public async Task PositionTarget_ReadsWorldSeedAndYaw()
    {
        var target = new SkillCastPositionTarget
        {
            PosX = 12950.25f,
            PosY = 9919.8f,
            PosZ = 100.4f,
            PosRot = 1.25f,
            ObjId1 = 700
        };

        await Assert.That(SlaveSummonSeedRules.TryReadWorldSeed(target, out var x, out var y, out var z, out var yaw))
            .IsTrue();
        await Assert.That(x).IsEqualTo(12950.25f);
        await Assert.That(y).IsEqualTo(9919.8f);
        await Assert.That(z).IsEqualTo(100.4f);
        await Assert.That(yaw).IsEqualTo(1.25f);
    }

    [Test]
    public async Task OriginOrMissingTarget_IsNotASeed()
    {
        await Assert.That(SlaveSummonSeedRules.TryReadWorldSeed(new SkillCastUnitTarget(), out _, out _, out _, out _))
            .IsFalse();
        await Assert.That(SlaveSummonSeedRules.TryReadWorldSeed(
                new SkillCastPositionTarget(), out _, out _, out _, out _))
            .IsFalse();
        await Assert.That(SlaveSummonSeedRules.HasWorldSeed(float.NaN, 1f, 1f)).IsFalse();
    }

    [Test]
    public async Task AlreadyPlanted_IsWithinTheSameStand()
    {
        await Assert.That(SlaveSummonSeedRules.IsAlreadyPlantedAtSeed(100f, 200f, 100.4f, 200.2f)).IsTrue();
        await Assert.That(SlaveSummonSeedRules.IsAlreadyPlantedAtSeed(100f, 200f, 110f, 200f)).IsFalse();
    }

    [Test]
    public async Task KeepExisting_WhenCsAlreadyPlantedThisItem()
    {
        await Assert.That(SlaveSummonSeedRules.ShouldKeepExistingPlant(true, hasSeed: false, alreadyAtSeed: false))
            .IsTrue();
        await Assert.That(SlaveSummonSeedRules.ShouldKeepExistingPlant(true, hasSeed: true, alreadyAtSeed: true))
            .IsTrue();
    }

    [Test]
    public async Task Replace_WhenNoHullOrResummonedToANewSeed()
    {
        await Assert.That(SlaveSummonSeedRules.ShouldKeepExistingPlant(false, hasSeed: true, alreadyAtSeed: false))
            .IsFalse();
        await Assert.That(SlaveSummonSeedRules.ShouldKeepExistingPlant(true, hasSeed: true, alreadyAtSeed: false))
            .IsFalse();
    }

    [Test]
    public async Task ApplySeed_SetsYawInsteadOfAddingIt()
    {
        var dest = new PositionAndRotation(10f, 20f, 30f, 0.1f, 0.2f, 0.3f);
        SlaveSummonSeedRules.ApplySeed(dest, 12950f, 9919f, 100f, 1.57f);

        await Assert.That(dest.Position.X).IsEqualTo(12950f);
        await Assert.That(dest.Position.Y).IsEqualTo(9919f);
        await Assert.That(dest.Position.Z).IsEqualTo(100f);
        await Assert.That(dest.Rotation.X).IsEqualTo(0f);
        await Assert.That(dest.Rotation.Y).IsEqualTo(0f);
        await Assert.That(dest.Rotation.Z).IsEqualTo(1.57f);
    }
}
