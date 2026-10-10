using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// Player-summoned companions follow the summoner; event-army spawns and unset stance rows do not.
/// Leaving a copy retires only the World-authored NPCs that character owns.
/// </summary>
public class SummonCompanionRulesTests
{
    [Test]
    public async Task CharacterAggressive_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(true, MateState.Aggressive)).IsTrue();
    }

    [Test]
    public async Task CharacterProtective_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(true, MateState.Protective)).IsTrue();
    }

    [Test]
    public async Task CharacterPassive_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(true, MateState.Passive)).IsTrue();
    }

    [Test]
    public async Task CharacterUnsetStance_DoesNotFollow()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(true, default)).IsFalse();
    }

    [Test]
    public async Task NpcCasterAggressive_DoesNotFollow()
    {
        // Event-army SpawnEffect rows carry the same stance. They must not follow the player.
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(false, MateState.Aggressive)).IsFalse();
    }

    [Test]
    public async Task WorldAuthoredOwnedByTheLeaver_IsRetired()
    {
        await Assert.That(SummonCompanionRules.IsPlayerSummonedCompanion(
            isWorldAuthored: true, ownerId: 8, characterId: 8)).IsTrue();
    }

    [Test]
    public async Task ZoneMirror_IsNotRetired()
    {
        await Assert.That(SummonCompanionRules.IsPlayerSummonedCompanion(
            isWorldAuthored: false, ownerId: 8, characterId: 8)).IsFalse();
    }

    [Test]
    public async Task SomeoneElsesSummon_IsNotRetired()
    {
        await Assert.That(SummonCompanionRules.IsPlayerSummonedCompanion(
            isWorldAuthored: true, ownerId: 39, characterId: 8)).IsFalse();
    }

    [Test]
    public async Task UnownedWorldNpc_IsNotRetired()
    {
        await Assert.That(SummonCompanionRules.IsPlayerSummonedCompanion(
            isWorldAuthored: true, ownerId: 0, characterId: 8)).IsFalse();
    }

    [Test]
    public async Task MissingCharacter_IsNotRetired()
    {
        await Assert.That(SummonCompanionRules.IsPlayerSummonedCompanion(
            isWorldAuthored: true, ownerId: 8, characterId: 0)).IsFalse();
    }
}
