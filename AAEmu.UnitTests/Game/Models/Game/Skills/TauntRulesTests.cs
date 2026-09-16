using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.taunt</c> (199 rows) and <c>buffs.taunt_with_top_aggro</c> (133 rows): forcing an NPC's target
/// onto the unit that applied the buff, and releasing it again.
/// </summary>
public class TauntRulesTests
{
    // 171 도발 — "도발되어 위협 수치와 상관없이 시전자 공격".
    private const bool TauntOnly = true;
    private const bool NoTopAggro = false;

    // 502 강력한 도발 — "대상에게 최고 위협 수치".
    private const bool TauntAndTopAggro = true;

    [Test]
    public async Task ForcesTarget_BothColumnsForce()
    {
        await Assert.That(TauntRules.ForcesTarget(TauntOnly, NoTopAggro)).IsTrue();
        await Assert.That(TauntRules.ForcesTarget(TauntAndTopAggro, TauntAndTopAggro)).IsTrue();

        // No shipped row sets taunt_with_top_aggro without taunt, but the pair must not force on its own.
        await Assert.That(TauntRules.ForcesTarget(false, false)).IsFalse();
    }

    [Test]
    public async Task GrantsTopAggro_NeedsBothColumns()
    {
        await Assert.That(TauntRules.GrantsTopAggro(TauntAndTopAggro, TauntAndTopAggro)).IsTrue();
        await Assert.That(TauntRules.GrantsTopAggro(TauntOnly, NoTopAggro)).IsFalse();
        await Assert.That(TauntRules.GrantsTopAggro(false, TauntAndTopAggro)).IsFalse();
    }

    [Test]
    public async Task TopAggroValue_BeatsWhateverTheTableHolds()
    {
        await Assert.That(TauntRules.TopAggroValue(0, 0)).IsEqualTo(1);
        await Assert.That(TauntRules.TopAggroValue(1_500, 200)).IsEqualTo(1_501);
        await Assert.That(TauntRules.TopAggroValue(1_500, 9_000)).IsEqualTo(9_001);
        await Assert.That(TauntRules.TopAggroValue(-5, -5)).IsEqualTo(1);
    }

    [Test]
    public async Task PublishableAggro_ClampsRatherThanWraps()
    {
        await Assert.That(TauntRules.PublishableAggro(1)).IsEqualTo(1u);
        await Assert.That(TauntRules.PublishableAggro(9_001)).IsEqualTo(9_001u);
        await Assert.That(TauntRules.PublishableAggro(-5)).IsEqualTo(0u);

        // Three saturated int components are the only way to reach this, and the wire field is a u32.
        await Assert.That(TauntRules.PublishableAggro(6_500_000_000L)).IsEqualTo(uint.MaxValue);
    }

    [Test]
    public async Task ReleaseTarget_WithTheNpcStillOnTheTaunter_HandsItBackToTheTopAbuser()
    {
        // npc 300 was forced onto 10; 20 now holds top threat.
        await Assert.That(TauntRules.ReleaseTarget(10u, 10u, 20u)).IsEqualTo(20u);
    }

    [Test]
    public async Task ReleaseTarget_WithNoThreatEntryAtAll_PublishesTheClearSentinel()
    {
        // World holds no aggro entry for the mob; 0 is the native clear-target sentinel.
        await Assert.That(TauntRules.ReleaseTarget(10u, 10u, 0u)).IsEqualTo(0u);
    }

    [Test]
    public async Task ReleaseTarget_WithNothingToDo_PublishesNothing()
    {
        // The taunter is still the top abuser: the zone's own pick is already right.
        await Assert.That(TauntRules.ReleaseTarget(10u, 10u, 10u)).IsNull();

        // The NPC has already moved on by itself.
        await Assert.That(TauntRules.ReleaseTarget(10u, 20u, 20u)).IsNull();
        await Assert.That(TauntRules.ReleaseTarget(10u, 0u, 0u)).IsNull();

        // No caster to release.
        await Assert.That(TauntRules.ReleaseTarget(0u, 0u, 0u)).IsNull();
    }
}
