using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ConflictZoneSpawnerRulesTests
{
    // Shipped shape for a group that carries both a war and a peace placement
    // (zone groups 63 and 147 do exactly this in conflict_zone_npc_spawners).
    private static ConflictZoneSpawnerEntry[] WarAndPeaceRows() =>
    [
        new ConflictZoneSpawnerEntry(9001, ConflictZoneStateKind.War, true, true),
        new ConflictZoneSpawnerEntry(9002, ConflictZoneStateKind.Peace, true, true)
    ];

    [Test]
    public async Task ResolveStateKind_MapsOnlyWarAndPeace()
    {
        // enum_conflict_zone_state_kinds only has none/peace/war; escalation states have no rows.
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.War))
            .IsEqualTo(ConflictZoneStateKind.War);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Peace))
            .IsEqualTo(ConflictZoneStateKind.Peace);

        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Tension))
            .IsEqualTo(ConflictZoneStateKind.None);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Danger))
            .IsEqualTo(ConflictZoneStateKind.None);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Dispute))
            .IsEqualTo(ConflictZoneStateKind.None);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Unrest))
            .IsEqualTo(ConflictZoneStateKind.None);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Crisis))
            .IsEqualTo(ConflictZoneStateKind.None);
        await Assert.That(ConflictZoneSpawnerRules.ResolveStateKind(ZoneConflictType.Conflict))
            .IsEqualTo(ConflictZoneStateKind.None);
    }

    [Test]
    public async Task ResolveActions_WarArmsOnlyTheWarRows()
    {
        var actions = ConflictZoneSpawnerRules.ResolveActions(63, ZoneConflictType.War, WarAndPeaceRows());

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].NpcSpawnerId).IsEqualTo(9001u);
        await Assert.That(actions[0].Activate).IsTrue();
        await Assert.That(actions[0].UseDespawn).IsTrue();
    }

    [Test]
    public async Task ResolveActions_PeaceArmsOnlyThePeaceRows()
    {
        var actions = ConflictZoneSpawnerRules.ResolveActions(63, ZoneConflictType.Peace, WarAndPeaceRows());

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].NpcSpawnerId).IsEqualTo(9002u);
    }

    [Test]
    public async Task ResolveActions_EscalationStateResolvesToNoActions()
    {
        // Tension..Conflict and Battle carry no dedicated spawner rows, so nothing is armed and
        // nothing is invented. The state still rides the wire for requirement checks.
        foreach (var state in new[]
                 {
                     ZoneConflictType.Tension, ZoneConflictType.Danger, ZoneConflictType.Dispute,
                     ZoneConflictType.Unrest, ZoneConflictType.Crisis, ZoneConflictType.Conflict
                 })
        {
            var actions = ConflictZoneSpawnerRules.ResolveActions(63, state, WarAndPeaceRows());
            await Assert.That(actions).IsEmpty();
        }
    }

    [Test]
    public async Task ResolveActions_SpawnActivateFalseBecomesAnExplicitDeactivate()
    {
        // Shipped zone group 139 has the single (peace, spawn_activate=false) row: the toggle must
        // return it with Activate=false so the relay deactivates that placement, not skip it.
        ConflictZoneSpawnerEntry[] rows = [new(1390, ConflictZoneStateKind.Peace, false, true)];

        var actions = ConflictZoneSpawnerRules.ResolveActions(139, ZoneConflictType.Peace, rows);

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].NpcSpawnerId).IsEqualTo(1390u);
        await Assert.That(actions[0].Activate).IsFalse();
        await Assert.That(actions[0].UseDespawn).IsTrue();
    }

    [Test]
    public async Task ResolveActions_KeepsUseDespawnFlagDistinctFromActivate()
    {
        // A row can be armed without despawn-on-retire; the two flags are independent.
        ConflictZoneSpawnerEntry[] rows = [new(5000, ConflictZoneStateKind.War, true, false)];

        var actions = ConflictZoneSpawnerRules.ResolveActions(1, ZoneConflictType.War, rows);

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].Activate).IsTrue();
        await Assert.That(actions[0].UseDespawn).IsFalse();
    }

    [Test]
    public async Task ResolveActions_IsSortedByPlacementIdForDeterministicRepublish()
    {
        // Repeated ZoneLoaded/reconnect publishes must be byte-identical, so the order is pinned.
        ConflictZoneSpawnerEntry[] rows =
        [
            new(3000, ConflictZoneStateKind.War, true, true),
            new(1000, ConflictZoneStateKind.War, true, true),
            new(2000, ConflictZoneStateKind.War, true, true)
        ];

        var actions = ConflictZoneSpawnerRules.ResolveActions(15, ZoneConflictType.War, rows);

        await Assert.That(actions.Count).IsEqualTo(3);
        await Assert.That(actions[0].NpcSpawnerId).IsEqualTo(1000u);
        await Assert.That(actions[1].NpcSpawnerId).IsEqualTo(2000u);
        await Assert.That(actions[2].NpcSpawnerId).IsEqualTo(3000u);
    }

    [Test]
    public async Task ResolveActions_EmptyRowsYieldNoActions()
    {
        var actions = ConflictZoneSpawnerRules.ResolveActions(
            999, ZoneConflictType.War, Array.Empty<ConflictZoneSpawnerEntry>());

        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task ResolveActions_NullRowsThrow()
    {
        // A missing content table must fail loudly, not silently arm nothing.
        await Assert.That(() => ConflictZoneSpawnerRules.ResolveActions(
                1, ZoneConflictType.War, (IReadOnlyList<ConflictZoneSpawnerEntry>)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ResolveStateKind_MatchesTheShippedStateBytes()
    {
        // enum_honor_point_war_states: war=6, peace=7 (same bytes as the WZ/SC hpws field).
        await Assert.That((byte)ZoneConflictType.War).IsEqualTo((byte)6);
        await Assert.That((byte)ZoneConflictType.Peace).IsEqualTo((byte)7);
    }

    [Test]
    public async Task BuildPlan_PartitionsArmAndRetireSetsFromTheShippedFlags()
    {
        // Mixed in one state: group 139's peace row with spawn_activate=false (retired) plus an
        // ordinary armed peace row. A war row exists to exercise the complementary retirement.
        ConflictZoneSpawnerEntry[] rows =
        [
            new(2000, ConflictZoneStateKind.Peace, true, true),
            new(1000, ConflictZoneStateKind.Peace, false, true),
            new(3000, ConflictZoneStateKind.War, true, true)
        ];

        var plan = ConflictZoneSpawnerRules.BuildPlan(139, ZoneConflictType.Peace, rows);

        // In-state: 2000 armed, 1000 explicitly retired. The war row (3000) is complementary and,
        // because it is armed under war, is retired when peace is entered.
        await Assert.That(plan.Arm.Count).IsEqualTo(1);
        await Assert.That(plan.Arm[0].NpcSpawnerId).IsEqualTo(2000u);
        await Assert.That(plan.Retire.Count).IsEqualTo(2);
        await Assert.That(plan.Retire[0].NpcSpawnerId).IsEqualTo(1000u);
        await Assert.That(plan.Retire[1].NpcSpawnerId).IsEqualTo(3000u);
    }

    [Test]
    public async Task BuildPlan_EnteringWarRetiresTheComplementaryPeaceRows()
    {
        // Group 63's shipped shape: two peace rows (armed) + one war row (armed). Entering war must
        // arm the war row and retire BOTH peace rows.
        ConflictZoneSpawnerEntry[] rows =
        [
            new(169343, ConflictZoneStateKind.Peace, true, true),
            new(169344, ConflictZoneStateKind.Peace, true, true),
            new(200747, ConflictZoneStateKind.War, true, true)
        ];

        var plan = ConflictZoneSpawnerRules.BuildPlan(63, ZoneConflictType.War, rows);

        await Assert.That(plan.Arm.Count).IsEqualTo(1);
        await Assert.That(plan.Arm[0].NpcSpawnerId).IsEqualTo(200747u);
        await Assert.That(plan.Retire.Count).IsEqualTo(2);
        await Assert.That(plan.Retire[0].NpcSpawnerId).IsEqualTo(169343u);
        await Assert.That(plan.Retire[1].NpcSpawnerId).IsEqualTo(169344u);
        foreach (var retire in plan.Retire)
            await Assert.That(retire.Activate).IsFalse();
    }

    [Test]
    public async Task BuildPlan_LeavingPeaceReArmsAComplementarySuppressedRow()
    {
        // Group 139's only row is a peace row with spawn_activate=false (suppressed during peace).
        // Entering war means that row no longer applies, so the placement reverts to armed.
        ConflictZoneSpawnerEntry[] rows = [new(213307, ConflictZoneStateKind.Peace, false, true)];

        var plan = ConflictZoneSpawnerRules.BuildPlan(139, ZoneConflictType.War, rows);

        await Assert.That(plan.Arm.Count).IsEqualTo(1);
        await Assert.That(plan.Arm[0].NpcSpawnerId).IsEqualTo(213307u);
        await Assert.That(plan.Arm[0].Activate).IsTrue();
        await Assert.That(plan.Retire).IsEmpty();
    }

    [Test]
    public async Task BuildPlan_EnteringPeaceRetiresTheComplementaryWarRows()
    {
        ConflictZoneSpawnerEntry[] rows =
        [
            new(238607, ConflictZoneStateKind.Peace, true, true),
            new(238568, ConflictZoneStateKind.War, true, true),
            new(238569, ConflictZoneStateKind.War, true, true)
        ];

        var plan = ConflictZoneSpawnerRules.BuildPlan(147, ZoneConflictType.Peace, rows);

        await Assert.That(plan.Arm.Count).IsEqualTo(1);
        await Assert.That(plan.Arm[0].NpcSpawnerId).IsEqualTo(238607u);
        await Assert.That(plan.Retire.Count).IsEqualTo(2);
        await Assert.That(plan.Retire[0].NpcSpawnerId).IsEqualTo(238568u);
        await Assert.That(plan.Retire[1].NpcSpawnerId).IsEqualTo(238569u);
    }

    [Test]
    public async Task BuildPlan_NeverPutsOnePlacementInBothArmAndRetire()
    {
        ConflictZoneSpawnerEntry[] rows =
        [
            new(1000, ConflictZoneStateKind.Peace, true, true),
            new(2000, ConflictZoneStateKind.War, true, true),
            new(3000, ConflictZoneStateKind.War, false, true)
        ];

        var plan = ConflictZoneSpawnerRules.BuildPlan(1, ZoneConflictType.War, rows);

        var armIds = plan.Arm.Select(a => a.NpcSpawnerId).ToHashSet();
        var retireIds = plan.Retire.Select(a => a.NpcSpawnerId).ToHashSet();
        await Assert.That(armIds.Overlaps(retireIds)).IsFalse();
    }

    [Test]
    public async Task BuildPlan_EscalationStateProducesEmptyArmAndRetire()
    {
        var plan = ConflictZoneSpawnerRules.BuildPlan(15, ZoneConflictType.Conflict, WarAndPeaceRows());

        await Assert.That(plan.Arm).IsEmpty();
        await Assert.That(plan.Retire).IsEmpty();
    }
}
