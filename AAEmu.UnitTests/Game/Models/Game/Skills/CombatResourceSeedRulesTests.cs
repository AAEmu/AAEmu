using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class CombatResourceSeedRulesTests
{
    [Test]
    public async Task AddGroupResourceIds_TakesBothColumnsAndChangeColumns()
    {
        var owned = new HashSet<int>();
        CombatResourceSeedRules.AddGroupResourceIds(owned, 5, 0, 6, 0);
        CombatResourceSeedRules.AddGroupResourceIds(owned, 26, 27, 0, 0);

        await Assert.That(owned.SetEquals([5, 6, 26, 27])).IsTrue();
    }

    [Test]
    public async Task AddGroupResourceIds_SkipsZeroAndNullOwner()
    {
        CombatResourceSeedRules.AddGroupResourceIds(null, 26, 27, 0, 0);

        var owned = new HashSet<int>();
        CombatResourceSeedRules.AddGroupResourceIds(owned, 0, 0, 0, 0);
        await Assert.That(owned.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ShouldSeed_OnlyWhenTheAbilityOwnsTheResource()
    {
        var fight = new HashSet<int> { 1 };
        var death = new HashSet<int> { 5, 6 };
        var pleasure = new HashSet<int> { 26, 27 };

        await Assert.That(CombatResourceSeedRules.ShouldSeed(26, fight)).IsFalse();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(27, fight)).IsFalse();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(6, fight)).IsFalse();

        await Assert.That(CombatResourceSeedRules.ShouldSeed(6, death)).IsTrue();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(26, death)).IsFalse();

        await Assert.That(CombatResourceSeedRules.ShouldSeed(26, pleasure)).IsTrue();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(27, pleasure)).IsTrue();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(6, pleasure)).IsFalse();
        await Assert.That(CombatResourceSeedRules.ShouldSeed(26, null)).IsFalse();
    }
}
