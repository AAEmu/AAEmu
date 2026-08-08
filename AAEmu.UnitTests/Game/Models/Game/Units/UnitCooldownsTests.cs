
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitCooldownsTests
{
    [Test]
    public async Task AddCooldown_ShouldAddCooldown_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        var duration = 5000u;

        // Act
        cooldowns.AddCooldown(skillId, duration);

        // Assert
        await Assert.That(cooldowns.CheckCooldown(skillId)).IsTrue();
    }

    [Test]
    public async Task AddCooldown_ShouldNotDuplicate_WhenSkillAlreadyExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        var duration1 = 5000u;
        var duration2 = 10000u;

        // Act
        cooldowns.AddCooldown(skillId, duration1);
        cooldowns.AddCooldown(skillId, duration2);

        // Assert
        var snapshots = cooldowns.GetActiveSnapshots(10);
        await Assert.That(snapshots).HasSingleItem();
        await Assert.That(snapshots[0].Duration).IsEqualTo(duration2);
    }

    [Test]
    public async Task CheckCooldown_ShouldReturnFalse_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckCooldown_ShouldReturnTrue_WhenCooldownIsActive()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        var duration = 60000u; // 60 seconds

        cooldowns.AddCooldown(skillId, duration);

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CheckCooldown_ShouldReturnFalseAndRemove_WhenCooldownExpired()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        cooldowns.AddCooldown(skillId, 0);

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(cooldowns.GetActiveSnapshots(1)).IsEmpty();
    }

    [Test]
    public async Task RemoveCooldown_ShouldRemoveSkill_WhenExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        cooldowns.AddCooldown(skillId, 60000);

        // Act
        cooldowns.RemoveCooldown(skillId);

        // Assert
        await Assert.That(cooldowns.CheckCooldown(skillId)).IsFalse();
    }

    [Test]
    public void RemoveCooldown_ShouldNotThrow_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        // Act & Assert - should not throw
        cooldowns.RemoveCooldown(skillId);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(1u)]
    [Arguments(100u)]
    [Arguments(999999u)]
    public async Task AddCooldown_ShouldAcceptVariousSkillIds(uint skillId)
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var duration = 5000u;

        // Act
        cooldowns.AddCooldown(skillId, duration);

        // Assert
        var snapshots = cooldowns.GetActiveSnapshots(1);
        await Assert.That(snapshots).HasSingleItem();
        await Assert.That(snapshots[0].SkillId).IsEqualTo(skillId);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(100u)]
    [Arguments(60000u)]
    [Arguments(uint.MaxValue)]
    public async Task CheckCooldown_ShouldHandleVariousDurations(uint duration)
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        cooldowns.AddCooldown(skillId, duration);
        if (duration > 250)
        {
            var result = cooldowns.CheckCooldown(skillId);
            await Assert.That(result).IsTrue();
        }
        else
        {
            var result = cooldowns.CheckCooldown(skillId);
            await Assert.That(result).IsFalse();
        }
    }
}
