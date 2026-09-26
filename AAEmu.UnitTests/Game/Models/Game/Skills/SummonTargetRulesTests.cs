using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Which of the caster's own summons a pet (10), my_slave (26) or child_slave (21) cast lands on. The client
/// resolves the first two from its own state (the first pet of the pet list, the
/// announced my-slave) and the third from the unit the packet names.
/// </summary>
public class SummonTargetRulesTests
{
    private const uint Mount = 501;
    private const uint BattlePet = 502;
    private const uint Sail = 601;
    private const uint Stranger = 900;

    [Test]
    public async Task Pick_NothingOwned_IsNothing()
    {
        // Answers NO_TARGET for an empty pet list.
        await Assert.That(SummonTargetRules.Pick([], 0)).IsEqualTo(0u);
        await Assert.That(SummonTargetRules.Pick([], Mount)).IsEqualTo(0u);
        await Assert.That(SummonTargetRules.Pick(null, Mount)).IsEqualTo(0u);
    }

    [Test]
    public async Task Pick_TheOnlySummon_WhateverThePacketNamed()
    {
        // A pet emote (44775 /돌아) pointed at a stranger still lands on the caster's own pet.
        await Assert.That(SummonTargetRules.Pick([Mount], 0)).IsEqualTo(Mount);
        await Assert.That(SummonTargetRules.Pick([Mount], Mount)).IsEqualTo(Mount);
        await Assert.That(SummonTargetRules.Pick([Mount], Stranger)).IsEqualTo(Mount);
    }

    [Test]
    public async Task Pick_TheNamedOneAmongSeveral()
    {
        await Assert.That(SummonTargetRules.Pick([Mount, BattlePet], BattlePet)).IsEqualTo(BattlePet);
        await Assert.That(SummonTargetRules.Pick([Mount, BattlePet], Mount)).IsEqualTo(Mount);
    }

    [Test]
    public async Task Pick_TheFirstWhenNoneOfThemIsNamed()
    {
        // Returns the first live entry of the pet collection.
        await Assert.That(SummonTargetRules.Pick([Mount, BattlePet], 0)).IsEqualTo(Mount);
        await Assert.That(SummonTargetRules.Pick([Mount, BattlePet], Stranger)).IsEqualTo(Mount);
    }

    [Test]
    public async Task PickNamed_OnlyAnOwnedPart()
    {
        // child_slave has no branch of its own in, so the named unit is used and has to be ours.
        await Assert.That(SummonTargetRules.PickNamed([Sail], Sail)).IsEqualTo(Sail);
        await Assert.That(SummonTargetRules.PickNamed([Sail], Stranger)).IsEqualTo(0u);
        await Assert.That(SummonTargetRules.PickNamed([Sail], 0)).IsEqualTo(0u);
        await Assert.That(SummonTargetRules.PickNamed([], Sail)).IsEqualTo(0u);
        await Assert.That(SummonTargetRules.PickNamed(null, Sail)).IsEqualTo(0u);
    }
}
