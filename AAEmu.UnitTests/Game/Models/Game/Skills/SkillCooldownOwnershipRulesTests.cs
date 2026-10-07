using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCooldownOwnershipRulesTests
{
    /// <summary>One <c>mount_skills.skill_id</c> granted to 56 different mounts in the shipped compact.</summary>
    private const uint SharedMountSkill = 17092;

    private static Character Rider(uint objId) =>
        new(new UnitCustomModelParams()) { Id = 1, ObjId = objId };

    [Test]
    public async Task PlayerFiredMateSkill_UsesTheRidersTable()
    {
        var rider = Rider(100);
        var mate = new Mate { OwnerObjId = rider.ObjId };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(mate, true, _ => rider))
            .IsSameReferenceAs(rider);
    }

    [Test]
    public async Task PlayerFiredMateWithNoOwnerId_KeepsItsOwnTable()
    {
        var mate = new Mate { OwnerObjId = 0 };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(mate, true, _ => Rider(100)))
            .IsSameReferenceAs(mate);
    }

    [Test]
    public async Task PlayerFiredMateWhoseOwnerIsNotInWorld_KeepsItsOwnTable()
    {
        var mate = new Mate { OwnerObjId = 100 };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(mate, true, _ => null))
            .IsSameReferenceAs(mate);
    }

    [Test]
    public async Task PlayerFiredSlaveSkill_UsesTheSummonersTable()
    {
        var rider = Rider(100);
        var hull = new Slave { Summoner = rider };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(hull, true, _ => null))
            .IsSameReferenceAs(rider);
    }

    [Test]
    public async Task PlayerFiredSlaveWithoutASummoner_KeepsItsOwnTable()
    {
        var hull = new Slave { Summoner = null };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(hull, true, _ => Rider(100)))
            .IsSameReferenceAs(hull);
    }

    [Test]
    public async Task PlayerFiredSkillOnTheRider_UsesTheRider()
    {
        var rider = Rider(100);

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(rider, true, _ => null))
            .IsSameReferenceAs(rider);
    }

    [Test]
    public async Task SimulationCastOnAMate_KeepsTheMatesOwnTable()
    {
        var rider = Rider(100);
        var mate = new Mate { OwnerObjId = rider.ObjId };

        await Assert.That(SkillCooldownOwnershipRules.CooldownOwner(mate, false, _ => rider))
            .IsSameReferenceAs(mate);
    }

    [Test]
    public async Task TwoMountsOnOneRider_ShareTheSameSkillCooldown()
    {
        var rider = Rider(100);
        var kirin = new Mate { OwnerObjId = rider.ObjId };
        var reindeer = new Mate { OwnerObjId = rider.ObjId };

        SkillCooldownOwnershipRules.CooldownOwner(kirin, true, _ => rider)
            .Cooldowns.AddCooldown(SharedMountSkill, 10_000);

        var reindeerOwner = SkillCooldownOwnershipRules.CooldownOwner(reindeer, true, _ => rider);

        await Assert.That(reindeerOwner.Cooldowns.CheckCooldown(SharedMountSkill)).IsTrue();
        // The mounts themselves never held it - that is exactly what used to make respawning reset it.
        await Assert.That(kirin.Cooldowns.CheckCooldown(SharedMountSkill)).IsFalse();
        await Assert.That(reindeer.Cooldowns.CheckCooldown(SharedMountSkill)).IsFalse();
    }

    [Test]
    public async Task DespawningAndResummoningTheMount_KeepsTheCooldown()
    {
        var rider = Rider(100);
        var firstSummon = new Mate { OwnerObjId = rider.ObjId };
        SkillCooldownOwnershipRules.CooldownOwner(firstSummon, true, _ => rider)
            .Cooldowns.AddCooldown(SharedMountSkill, 10_000);

        // The mount object is gone; its replacement is a brand-new Mate with an empty table.
        var secondSummon = new Mate { OwnerObjId = rider.ObjId };
        await Assert.That(secondSummon.Cooldowns.CheckCooldown(SharedMountSkill)).IsFalse();

        var secondOwner = SkillCooldownOwnershipRules.CooldownOwner(secondSummon, true, _ => rider);
        await Assert.That(secondOwner.Cooldowns.CheckCooldown(SharedMountSkill)).IsTrue();
    }

    [Test]
    public async Task DifferentRiders_DoNotShareTheSameSkillCooldown()
    {
        var alice = Rider(100);
        var bob = Rider(200);
        Unit Resolve(uint objId) => objId == alice.ObjId ? alice : bob;

        SkillCooldownOwnershipRules.CooldownOwner(new Mate { OwnerObjId = alice.ObjId }, true, Resolve)
            .Cooldowns.AddCooldown(SharedMountSkill, 10_000);

        var bobOwner = SkillCooldownOwnershipRules.CooldownOwner(
            new Mate { OwnerObjId = bob.ObjId }, true, Resolve);

        await Assert.That(bobOwner.Cooldowns.CheckCooldown(SharedMountSkill)).IsFalse();
    }
}
