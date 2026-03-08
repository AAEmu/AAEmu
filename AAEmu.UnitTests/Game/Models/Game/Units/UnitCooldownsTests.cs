using Xunit;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitCooldownsTests
{
    [Fact]
    public void AddCooldown_ShouldAddCooldown_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        var duration = 5000u;

        // Act
        cooldowns.AddCooldown(skillId, duration);

        // Assert
        Assert.True(cooldowns.Cooldowns.ContainsKey(skillId));
    }

    [Fact]
    public void AddCooldown_ShouldNotDuplicate_WhenSkillAlreadyExists()
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
        Assert.Single(cooldowns.Cooldowns);
    }

    [Fact]
    public void CheckCooldown_ShouldReturnFalse_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CheckCooldown_ShouldReturnTrue_WhenCooldownIsActive()
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
        Assert.True(result);
    }

    [Fact]
    public void CheckCooldown_ShouldReturnFalseAndRemove_WhenCooldownExpired()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        // Manually add an expired cooldown
        cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddSeconds(-1));

        // Act
        var result = cooldowns.CheckCooldown(skillId);

        // Assert
        Assert.False(result);
        Assert.False(cooldowns.Cooldowns.ContainsKey(skillId));
    }

    [Fact]
    public void RemoveCooldown_ShouldRemoveSkill_WhenExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;
        cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMinutes(1));

        // Act
        cooldowns.RemoveCooldown(skillId);

        // Assert
        Assert.False(cooldowns.Cooldowns.ContainsKey(skillId));
    }

    [Fact]
    public void RemoveCooldown_ShouldNotThrow_WhenSkillNotExists()
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        // Act & Assert - should not throw
        cooldowns.RemoveCooldown(skillId);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(100u)]
    [InlineData(999999u)]
    public void AddCooldown_ShouldAcceptVariousSkillIds(uint skillId)
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var duration = 5000u;

        // Act
        cooldowns.AddCooldown(skillId, duration);

        // Assert
        Assert.True(cooldowns.Cooldowns.ContainsKey(skillId));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(100u)]
    [InlineData(60000u)]
    [InlineData(uint.MaxValue)]
    public void CheckCooldown_ShouldHandleVariousDurations(uint duration)
    {
        // Arrange
        var cooldowns = new UnitCooldowns();
        var skillId = 100u;

        if (duration > 250) // Only add if it would be considered "active"
        {
            cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMilliseconds(duration));
            var result = cooldowns.CheckCooldown(skillId);
            Assert.True(result);
        }
        else
        {
            cooldowns.Cooldowns.TryAdd(skillId, DateTime.UtcNow.AddMilliseconds(duration));
            var result = cooldowns.CheckCooldown(skillId);
            Assert.False(result);
        }
    }
}
