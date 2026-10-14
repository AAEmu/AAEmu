using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// Player companions follow only when the spawn row sets use_summoner_faction; hostile quest
/// summons and doodad pop-outs do not. Leaving a copy retires only World-authored NPCs that
/// character owns.
/// </summary>
public class SummonCompanionRulesTests
{
    [Test]
    public async Task CharacterAggressiveWithSummonerFaction_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            true, MateState.Aggressive, useSummonerFaction: true)).IsTrue();
    }

    [Test]
    public async Task CharacterProtectiveWithSummonerFaction_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            true, MateState.Protective, useSummonerFaction: true)).IsTrue();
    }

    [Test]
    public async Task CharacterPassiveWithSummonerFaction_Follows()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            true, MateState.Passive, useSummonerFaction: true)).IsTrue();
    }

    [Test]
    public async Task CharacterAggressiveWithoutSummonerFaction_DoesNotFollow()
    {
        // Every spawn_effects row carries stance 1–3; without use_summoner_faction the rule
        // would make hostile quest summons and doodad chests follow the player.
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            true, MateState.Aggressive, useSummonerFaction: false)).IsFalse();
    }

    [Test]
    public async Task CharacterUnsetStance_DoesNotFollow()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            true, default, useSummonerFaction: true)).IsFalse();
    }

    [Test]
    public async Task NpcCasterAggressive_DoesNotFollow()
    {
        await Assert.That(SummonCompanionRules.ShouldFollowSummoner(
            false, MateState.Aggressive, useSummonerFaction: true)).IsFalse();
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
