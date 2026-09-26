
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

    /// <summary>
    /// Nothing prunes an expired entry on a timer, so a real cooldown whose end time has passed can still be
    /// present in the dictionary while reading as finished. A negative reduction must not resurrect it.
    /// <para>
    /// The reduction is computed against the ORIGINAL duration, so this is not a small effect: a 1s cooldown
    /// that lapsed a moment ago comes back to life with a full second on the clock. The client is already
    /// showing the skill as ready, so the cast is then refused with no visible reason.
    /// </para>
    /// </summary>
    [Test]
    public async Task ApplyCooldownReduction_ExpiredEntryIsNotRearmed()
    {
        var cooldowns = new UnitCooldowns();
        const uint skillId = 4242u;

        // Arm a real 1s cooldown and let it lapse. The entry stays in the dictionary until something prunes it,
        // which is exactly the state this covers.
        cooldowns.AddCooldown(skillId, 1000u);
        await WaitUntilExpiredAsync(cooldowns, skillId);

        // A negative flat reduction is the authored "extend" value. Against this expired entry it would add its
        // magnitude to a zero remaining.
        cooldowns.ApplyCooldownReduction(skillId, -10000, 0);

        var remainingAfter = cooldowns.GetRemaining(skillId);
        var snapshots = cooldowns.GetActiveSnapshots(10);

        await Assert.That(remainingAfter).IsEqualTo(TimeSpan.Zero);
        await Assert.That(snapshots).IsEmpty();
    }

    /// <summary>
    /// The percent branch scales from the original duration, so an expired entry is re-armed by an even larger
    /// amount. This is the branch the ten shipped negative triggers use.
    /// </summary>
    [Test]
    public async Task ApplyCooldownReduction_ExpiredEntryIsNotRearmedByPercent()
    {
        var cooldowns = new UnitCooldowns();
        const uint skillId = 4243u;

        cooldowns.AddCooldown(skillId, 5000u);
        await WaitUntilExpiredAsync(cooldowns, skillId, TimeSpan.FromSeconds(4.5));

        // -100% of a 5s original: without the guard this re-arms the entry for a further 5s.
        cooldowns.ApplyCooldownReduction(skillId, 0, -100);

        var remainingAfter = cooldowns.GetRemaining(skillId);
        var snapshots = cooldowns.GetActiveSnapshots(10);

        await Assert.That(remainingAfter).IsEqualTo(TimeSpan.Zero);
        await Assert.That(snapshots).IsEmpty();
    }

    /// <summary>
    /// A positive reduction on a live cooldown must still shorten it — the expired-entry guard is not a
    /// blanket refusal.
    /// </summary>
    [Test]
    public async Task ApplyCooldownReduction_LiveEntryIsStillReduced()
    {
        var cooldowns = new UnitCooldowns();
        const uint skillId = 4244u;

        cooldowns.AddCooldown(skillId, 60000u);

        cooldowns.ApplyCooldownReduction(skillId, 30000, 0);

        var remaining = cooldowns.GetRemaining(skillId);
        var active = cooldowns.CheckCooldown(skillId);

        await Assert.That(active).IsTrue();
        await Assert.That(remaining).IsLessThan(TimeSpan.FromSeconds(50));
        await Assert.That(remaining).IsGreaterThan(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Waits until the skill's remaining time reaches zero. Polls <see cref="UnitCooldowns.GetRemaining"/>,
    /// which is the one read that does not prune the entry — <c>CheckCooldown</c> removes it, which would
    /// destroy the very state these tests are about. The wait polls the observable value instead of sleeping a
    /// fixed span, so it returns as soon as the clock allows and fails loudly rather than flaking.
    /// </summary>
    private static async Task WaitUntilExpiredAsync(UnitCooldowns cooldowns, uint skillId, TimeSpan? minimumWait = null)
    {
        var earliest = (minimumWait ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(400);
        var notBefore = DateTime.UtcNow + earliest;
        while (DateTime.UtcNow < notBefore)
            await Task.Delay(20);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (cooldowns.GetRemaining(skillId) <= TimeSpan.Zero)
                return;
            await Task.Delay(20);
        }

        throw new InvalidOperationException($"Cooldown {skillId} did not expire within the timeout.");
    }
}
