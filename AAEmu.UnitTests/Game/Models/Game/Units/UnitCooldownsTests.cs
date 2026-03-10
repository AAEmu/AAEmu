
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
        await Assert.That(cooldowns.Cooldowns.ContainsKey(skillId)).IsTrue();
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
        await Assert.That(cooldowns.Cooldowns).HasSingleItem();
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

        // Manually add a cooldown with a future end time
        cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMilliseconds(duration));

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

        // Manually add an expired cooldown
        cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddSeconds(-1));

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        await Assert.That(result).IsFalse();
        await Assert.That(cooldowns.Cooldowns.ContainsKey(skillId)).IsFalse();
    }

    [Test]
    public async Task RemoveCooldown_ShouldRemoveSkill_WhenExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMinutes(1));

        // Act
        cooldowns.RemoveCooldown(skillId);

        // Assert
        await Assert.That(cooldowns.Cooldowns.ContainsKey(skillId)).IsFalse();
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
        await Assert.That(cooldowns.Cooldowns.ContainsKey(skillId)).IsTrue();
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

        if (duration > 250) // Only add if it would be considered "active"
        {
            cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMilliseconds(duration));
            var result = cooldowns.CheckCooldown(skillId);
            await Assert.That(result).IsTrue();
        }
        else
        {
            cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMilliseconds(duration));
            var result = cooldowns.CheckCooldown(skillId);
            await Assert.That(result).IsFalse();
        }
    }
}