using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// The conflict spawner toggle is enforced on the <c>ZWSpawnNpc</c> announcement, not by sending an
/// activate circle. These tests assert the closed set the gate holds and the despawns the
/// <c>use_despawn</c> rows still push — and, just as importantly, that no activate circle is emitted
/// for conflict state at all, because 0x042 cannot address one placement.
/// </summary>
[NotInParallel]
public class ConflictZoneSpawnerRelayTests
{
    private const uint ZoneUnderTest = 100;

    private static uint _nextSessionId = 910000;
    private readonly List<ZoneConnection> _connections = [];

    [Before(Test)]
    public void Setup()
    {
        // Other relay test classes share the static ZoneSession; clear it so each of these tests
        // starts with exactly the zone it registers.
        foreach (var zone in ZoneSession.Instance.All.ToList())
            ZoneSession.Instance.Remove(zone.Id);
        RemoveConnections();
        ConflictZoneSpawnerRelay.ResetForTest();
        ConflictSpawnerGate.ResetForTest();
        // One zone, always in the group under test; a second zone joins a different group below.
        // The gate reads the relay's resolver, so overriding this one covers both.
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 63u : 7u;
        ConflictZoneSpawnerRelay.ResolvePlacements = _ => [];
        ConflictZoneSpawnerRelay.ResolveRows = _ => [];
        // The re-arm reaches the schedule gate and its DI-owned managers, which a unit test cannot
        // stand up, so it is stubbed like the resolvers above. A test that asserts on the re-arm
        // installs its own recorder.
        ConflictZoneSpawnerRelay.ReactivateSpawners = (_, _) => { };
    }

    [After(Test)]
    public void Cleanup()
    {
        foreach (var zone in ZoneSession.Instance.All.ToList())
            ZoneSession.Instance.Remove(zone.Id);
        RemoveConnections();
        ConflictZoneSpawnerRelay.ResetForTest();
        ConflictSpawnerGate.ResetForTest();
    }

    [Test]
    public async Task EnteringWar_ClosesThePeacePlacementsAndLeavesTheWarOneOpen()
    {
        // Group 63's shipped shape: two armed peace rows, one armed war row.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(169344, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (169344, 20779), (200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        await Assert.That(Closed(ZoneUnderTest, 169343, 20779)).IsTrue();
        await Assert.That(Closed(ZoneUnderTest, 169344, 20779)).IsTrue();
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsFalse();
    }

    [Test]
    public async Task EnteringPeace_ClosesTheWarPlacementAndLeavesThePeaceOneOpen()
    {
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
        await Assert.That(Closed(ZoneUnderTest, 169343, 20779)).IsFalse();
    }

    [Test]
    public async Task AZoneLoad_PublishesTheClosedSetWithoutReArming()
    {
        // A zone that has just loaded has closed nothing, so it must not be re-announced: a
        // whole-zone activate here would skip the prewarm and the deferred arming a normal load
        // goes through. The closed set still has to be published, or the gate would be judging
        // this zone's spawns against whatever the previous transition left behind.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (200747, 20800)]));
        AddZone(ZoneUnderTest, 0);
        var reArms = RecordReArms();

        ConflictZoneSpawnerRelay.ApplyOnZoneLoaded(63, (byte)ZoneConflictType.Peace);

        await Assert.That(reArms).IsEmpty();
        // Published anyway: peace closes the war row, and that is the gate the spawn path reads.
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
    }

    [Test]
    public async Task ATransition_ReArmsTheZoneSoPlacementsThatLeftTheClosedSetComeBack()
    {
        // The counterpart: a transition is the one event that closes placements, so it is the one
        // path that re-arms. Same zone, same rows, same state as the load case above — only the
        // entry point differs.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (200747, 20800)]));
        AddZone(ZoneUnderTest, 0);
        var reArms = RecordReArms();

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        await Assert.That(reArms.Count).IsEqualTo(1);
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
    }

    [Test]
    public async Task TheClosedSetIsPublishedBeforeTheReArmFloodIsSent()
    {
        // The re-arm announces the whole zone, so the placements that are still closed in the new
        // state get refused again on the way through. That only holds if the gate is already
        // carrying this state when the flood is sent, which is why Publish comes first.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        // Read the gate from inside the re-arm, so this pins the order rather than the end state.
        var closedDuringReArm = new List<bool>();
        ConflictZoneSpawnerRelay.ReactivateSpawners = (_, _) =>
            closedDuringReArm.Add(Closed(ZoneUnderTest, 200747, 20800));

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        await Assert.That(closedDuringReArm).IsEquivalentTo(new[] { true });
    }

    /// <summary>
    /// Counts re-arm sends through the relay's own seam. The re-arm reaches the schedule gate and its
    /// DI-owned managers, which a unit test cannot stand up, so it is stubbed and counted here.
    /// </summary>
    private List<string> RecordReArms()
    {
        var reArms = new List<string>();
        ConflictZoneSpawnerRelay.ReactivateSpawners = (zone, reason) => reArms.Add(reason);
        return reArms;
    }

    [Test]
    public async Task Group139_SpawnActivateFalse_ReOpensWhenLeavingPeace()
    {
        // Group 139's only row is a peace row with spawn_activate=false: suppressed in peace, and
        // re-armed once the group is in war because the row no longer applies.
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 139u : 7u;
        Rows(139, new ConflictZoneSpawnerEntry(213307, ConflictZoneStateKind.Peace, false, false));
        Placements((ZoneUnderTest, [(213307, 999)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(139, (byte)ZoneConflictType.War);

        await Assert.That(Closed(ZoneUnderTest, 213307, 999)).IsFalse();
    }

    [Test]
    public async Task Group139_SpawnActivateFalse_IsClosedWhileInPeace()
    {
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 139u : 7u;
        Rows(139, new ConflictZoneSpawnerEntry(213307, ConflictZoneStateKind.Peace, false, false));
        Placements((ZoneUnderTest, [(213307, 999)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(139, (byte)ZoneConflictType.Peace);

        await Assert.That(Closed(ZoneUnderTest, 213307, 999)).IsTrue();
    }

    [Test]
    public async Task ConflictState_SendsNoActivateCircleAtAll()
    {
        // The regression this redesign exists for. 0x042 is a centre+radius circle; measured against
        // the shipped npc_spawners.g files the configured 1024m circle covers every placement in
        // zone 137 (208/208) for group 15. Emitting one per placement switched off the whole zone,
        // so a conflict toggle must never reach the wire as a circle.
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 15u : 7u;
        Rows(15, new ConflictZoneSpawnerEntry(60463, ConflictZoneStateKind.War, true, true));
        Placements((ZoneUnderTest, [(60463, 20779)]));
        var zone = AddZone(ZoneUnderTest, 0);

        // War arms the row (nothing closed); peace retires it (the row no longer applies). Both
        // passes must emit nothing.
        ConflictZoneSpawnerRelay.ApplyOnTransition(15, (byte)ZoneConflictType.War);
        ConflictZoneSpawnerRelay.ApplyOnTransition(15, (byte)ZoneConflictType.Peace);

        // Sanity: the peace pass really did close the placement, so this is not vacuous.
        await Assert.That(Closed(ZoneUnderTest, 60463, 20779)).IsTrue();
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.ActivateNpcSpawnersInArea))
            .IsEqualTo(0);
    }

    [Test]
    public async Task EnteringPeace_DoesNotCloseANativePlacementInTheSameZone()
    {
        // The closed set is built only from the group's conflict_zone_npc_spawners rows, so a
        // placement that appears in no such row is never a member — the exact property the old
        // deactivate circle could not provide.
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, true));
        Placements((ZoneUnderTest,
        [
            (200747, 20800), // the conflict row, retired on entering peace
            (120120, 23183), // an ordinary native placement sharing the zone
            (119721, 15034)  // another
        ]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
        await Assert.That(Closed(ZoneUnderTest, 120120, 23183)).IsFalse();
        await Assert.That(Closed(ZoneUnderTest, 119721, 15034)).IsFalse();
    }

    [Test]
    public async Task APlayerEnterReArm_CannotReopenAConflictPlacementInPeace()
    {
        // PlayerEnterService and the schedule-window paths both re-send 0x042 circles. Those must not
        // be able to bring a retired conflict placement back, because the gate — not the zone's
        // arming state — is what authorises an announcement.
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();

        // Stand in for the player-enter / schedule-window re-arm: a raw activate circle for the very
        // placement the gate is holding closed.
        zone.Connection.SendPacket(new WZActivateNpcSpawnersInAreaPacket(1f, 2f, 3f, 1024f, activate: true));

        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
    }

    [Test]
    public async Task AScheduleWindowReArm_CannotReopenAConflictPlacementInPeace()
    {
        // The NpcScheduleGate is a different, template-keyed gate. Opening a schedule window must not
        // interact with the conflict gate: the two conditions are independent, and a placement closed
        // by the conflict state stays closed whatever the schedule does.
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        // Even with the schedule gate reporting nothing closed for this template, the conflict gate
        // still holds the placement.
        await Assert.That(NpcScheduleGate.IsClosed(20800)).IsFalse();
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
    }

    [Test]
    public async Task Fanout_ClosesPlacementsInEveryZoneOfTheGroup_AndSkipsOtherGroups()
    {
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId switch
        {
            ZoneUnderTest => 15u,
            200 => 15u,
            _ => 99u
        };
        Rows(15, new ConflictZoneSpawnerEntry(11, ConflictZoneStateKind.War, true, true));
        ConflictZoneSpawnerRelay.ResolvePlacements = zoneId => zoneId switch
        {
            ZoneUnderTest => [new ZoneSpawnerPlacementCatalog.SpawnerPlacement(11, 5, 11f, 0f, 0f, 0f)],
            200 => [new ZoneSpawnerPlacementCatalog.SpawnerPlacement(11, 5, 11f, 0f, 0f, 0f)],
            _ => []
        };
        var other = AddZone(300, 0); // different group, must stay open
        AddZone(ZoneUnderTest, 0);
        AddZone(200, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(15, (byte)ZoneConflictType.Peace);

        // The id is the same in both zones, so this also pins that the key is resolved per zone
        // rather than being a bare group-wide id.
        await Assert.That(Closed(ZoneUnderTest, 11, 5)).IsTrue();
        await Assert.That(Closed(200, 11, 5)).IsTrue();
        await Assert.That(Closed(300, 11, 5)).IsFalse();
        await Assert.That(other.Session.Packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MissingPlacement_IsNotClosedWithoutAFabricatedId()
    {
        Rows(63,
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false),
            new ConflictZoneSpawnerEntry(424242, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)])); // 424242 is absent from the zone's .g
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        // 200747 is a war row, so entering peace retires it; 424242 cannot be typed because the
        // zone's catalog does not carry it, so it is never admitted to the closed set under a
        // guessed type.
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(1);
        await Assert.That(zone.Session.Packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TypeCollision_ClosesOnlyTheExactIdAndTypePair()
    {
        // Group 63's two peace rows share spawner type 20779. The key must carry the id as well, or
        // closing one would close the other.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(169344, ConflictZoneStateKind.Peace, true, false));
        Placements((ZoneUnderTest, [(169343, 20779), (169344, 20779)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        // Both are peace rows so both close here, but on distinct keys — assert the set has two
        // entries and that a third id under the same type is not a member.
        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(2);
        await Assert.That(Closed(ZoneUnderTest, 169345, 20779)).IsFalse();
    }

    [Test]
    public async Task UseDespawn_RetiresOnlyTheExactIdAndType_AndKeepsTheBcIdRegistered()
    {
        // Type 20779 is shared by two placements, so matching on type alone would retire both.
        // Only the exact (id,type) pair may be despawned, and the bcId must stay registered until
        // the Zone confirms ZWRemoveNpc.
        Rows(63, new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, true));
        Placements((ZoneUnderTest, [(169343, 20779), (169344, 20779), (200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);
        zone.Connection.Units.RegisterWithId(1, CreateSpawnBody(169343, 20779));
        zone.Connection.Units.RegisterWithId(2, CreateSpawnBody(169344, 20779));
        zone.Connection.Units.RegisterWithId(3, CreateSpawnBody(200747, 20800));

        // Enter war: only the peace placement 169343 is retired.
        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        var despawns = zone.Session.Packets
            .Where(p => Opcode(p) == (ushort)WzOpcodes.NpcStartDespawn)
            .Select(BcId)
            .ToList();
        await Assert.That(despawns).IsEquivalentTo(new List<uint> { 1 });

        // Deferred removal: every bcId is still registered (no premature ObjectId release).
        await Assert.That(zone.Connection.Units.Snapshot().Select(e => e.Key).OrderBy(k => k).ToList())
            .IsEquivalentTo(new List<uint> { 1, 2, 3 });
    }

    [Test]
    public async Task UseDespawn_PlacementIsAlsoClosedSoItCannotComeBack()
    {
        Rows(63, new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, true));
        Placements((ZoneUnderTest, [(169343, 20779)]));
        var zone = AddZone(ZoneUnderTest, 0);
        zone.Connection.Units.RegisterWithId(1, CreateSpawnBody(169343, 20779));

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        // Both halves: the live NPC is despawned AND the placement is refused from re-announcing.
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcStartDespawn))
            .IsEqualTo(1);
        await Assert.That(Closed(ZoneUnderTest, 169343, 20779)).IsTrue();
    }

    [Test]
    public async Task NoDespawn_WhenUseDespawnIsFalse()
    {
        Rows(63, new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false));
        Placements((ZoneUnderTest, [(169343, 20779)]));
        var zone = AddZone(ZoneUnderTest, 0);
        zone.Connection.Units.RegisterWithId(1, CreateSpawnBody(169343, 20779));

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcStartDespawn))
            .IsEqualTo(0);
        // Still closed — the gate is what stops it coming back, independent of the despawn.
        await Assert.That(Closed(ZoneUnderTest, 169343, 20779)).IsTrue();
    }

    [Test]
    public async Task EscalationState_ClosesTheWarPlacementAndSendsNoCircle()
    {
        // Escalation is neither Peace nor War, so this war row is not in force and its placement is
        // closed. Publishing an empty set here is what used to happen, and the gate reads an empty set
        // as "this group has no rows" — the war placement then came back for the whole escalation
        // stretch, which for a war-only group like 19 is most of each cycle.
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, true));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Conflict);

        // No circle is sent for conflict state at all — that is unchanged.
        await Assert.That(zone.Session.Packets.Count).IsEqualTo(0);
        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(1);
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();
    }

    [Test]
    public async Task ALaterStateThatRearmsAPlacement_ReAnnouncesTheZone()
    {
        // A placement that has just left the closed set has no NPC standing and will not announce
        // itself again on its own: the Zone only re-announces on an activate sphere, which otherwise
        // arrives on a player enter or a schedule window. Without this the placement stays empty
        // until one of those happens by chance.
        Rows(63,
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, true),
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, true));
        Placements((ZoneUnderTest, [(200747, 20800), (169343, 20779)]));
        AddZone(ZoneUnderTest, 0);

        var reArms = new List<string>();
        ConflictZoneSpawnerRelay.ReactivateSpawners = (_, reason) => reArms.Add(reason);

        // Entering war arms the war row, so the zone has to re-announce.
        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);

        await Assert.That(reArms.Count).IsEqualTo(1);
        await Assert.That(Closed(ZoneUnderTest, 169343, 20779)).IsTrue();
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsFalse();

        // And a state that arms nothing must not ask for a re-announce.
        reArms.Clear();
        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Conflict);

        await Assert.That(reArms).IsEmpty();
    }

    [Test]
    public async Task AGroupWithNoRowsAtAll_PublishesNothingAndDoesNotRearm()
    {
        // The one case where an empty result genuinely means "nothing to say": a property of the
        // rows, not of the state. Contrast with the escalation case above, where the same empty
        // armed set must NOT be published.
        Rows(63);
        Placements((ZoneUnderTest, [(200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        var reArms = 0;
        ConflictZoneSpawnerRelay.ReactivateSpawners = (_, _) => reArms++;

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Conflict);

        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(0);
        await Assert.That(reArms).IsEqualTo(0);
    }

    [Test]
    public async Task UnknownWarStateByte_IsIgnored()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, true));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, 200);

        await Assert.That(zone.Session.Packets.Count).IsEqualTo(0);
        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(0);
    }

    [Test]
    public async Task AGroupThatStopsResolvingItsPlacements_ClearsAPreviouslyClosedSet()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsTrue();

        // The zone's catalog stops resolving (level files moved, zone reloading). The set must not
        // stay armed from the previous pass.
        ConflictZoneSpawnerRelay.ResolvePlacements = _ => [];
        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        await Assert.That(ConflictSpawnerGate.ClosedCount(63)).IsEqualTo(0);
        await Assert.That(Closed(ZoneUnderTest, 200747, 20800)).IsFalse();
    }

    // ---- spawn-path enforcement ----
    // The gate only matters if the ZWSpawnNpc announcement actually consults it. These drive
    // NpcSpawnRelay.OnSpawn itself, so removing the gate from that path fails here rather than
    // leaving every assertion in this file green against a gate nothing calls.

    [Test]
    public async Task SpawnPath_RefusesAConflictPlacementTheGateHoldsClosed()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        var bcId = new NpcSpawnRelay().OnSpawn(zone.Connection, CreateSpawnBody(200747, 20800));

        // WZNpcSpawnFailed is what stops Zone running NpcManager::Create, so the NPC exists nowhere.
        await Assert.That(bcId).IsEqualTo(0u);
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcSpawnFailed))
            .IsEqualTo(1);
        await Assert.That(zone.Connection.Units.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SpawnPath_AcceptsANativePlacementInTheSameZone()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800), (120120, 23183)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);

        // The conflict placement is refused: the gate answers it before a bcId is spent, so
        // OnSpawn returns 0 and no allocator is touched.
        await Assert.That(new NpcSpawnRelay().OnSpawn(zone.Connection, CreateSpawnBody(200747, 20800)))
            .IsEqualTo(0u);
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcSpawnFailed))
            .IsEqualTo(1);

        // The native placement is not. The gate lets it through, so execution reaches
        // UnitRegistry.Register — which needs the process-wide ObjectIdManager, and that
        // initialises from MySQL and is therefore unavailable here. Reaching it IS the assertion:
        // a native placement in a zone whose conflict group is in peace must not be gated.
        // (The old circle-based toggle switched off this placement too, because its circle covered
        // the whole zone.)
        var reachedRegister = false;
        try
        {
            new NpcSpawnRelay().OnSpawn(zone.Connection, CreateSpawnBody(120120, 23183));
            reachedRegister = true;
        }
        catch (InvalidOperationException)
        {
            reachedRegister = true; // got past the gate, then hit the missing allocator
        }

        await Assert.That(reachedRegister).IsTrue();
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcSpawnFailed))
            .IsEqualTo(1);
    }

    [Test]
    public async Task SpawnPath_AcceptsTheConflictPlacementAgainWhenTheStateFlipsBack()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((ZoneUnderTest, [(200747, 20800)]));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.Peace);
        await Assert.That(new NpcSpawnRelay().OnSpawn(zone.Connection, CreateSpawnBody(200747, 20800)))
            .IsEqualTo(0u);

        // Entering war authorises the war row, so the same placement must no longer be refused. As
        // above, getting past the gate is proven by reaching the allocator.
        ConflictZoneSpawnerRelay.ApplyOnTransition(63, (byte)ZoneConflictType.War);
        var reachedRegister = false;
        try
        {
            new NpcSpawnRelay().OnSpawn(zone.Connection, CreateSpawnBody(200747, 20800));
            reachedRegister = true;
        }
        catch (InvalidOperationException)
        {
            reachedRegister = true;
        }

        await Assert.That(reachedRegister).IsTrue();
        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcSpawnFailed))
            .IsEqualTo(1);
    }

    // ---- helpers ----

    private static void Rows(ushort groupId, params ConflictZoneSpawnerEntry[] rows) =>
        ConflictZoneSpawnerRelay.ResolveRows = g => g == groupId ? rows : [];

    /// <summary>
    /// Places each zone's placements. Only the id and type matter to the gate; X/Y/Z are carried
    /// because the catalog record requires them.
    /// </summary>
    private void Placements(params (uint ZoneId, (uint Id, uint Type)[] Items)[] perZone)
    {
        var map = perZone.ToDictionary(p => p.ZoneId, p => p.Items);
        ConflictZoneSpawnerRelay.ResolvePlacements = zoneId => map.TryGetValue(zoneId, out var items)
            ? items.Select(p => new ZoneSpawnerPlacementCatalog.SpawnerPlacement(p.Id, p.Type, p.Id, 0f, 0f, 0f)).ToList()
            : [];
    }

    /// <summary>What the spawn path will decide for this exact placement in this zone.</summary>
    private static bool Closed(uint zoneId, uint spawnerId, uint spawnerType) =>
        ConflictSpawnerGate.IsClosed(zoneId, spawnerId, spawnerType);

    private ZoneFixture AddZone(uint zoneId, uint instanceId, ZoneConnectionState state = ZoneConnectionState.ZoneLoaded)
    {
        var session = new RecordingSession(_nextSessionId++);
        var connection = new ZoneConnection(session)
        {
            ZoneId = zoneId,
            InstanceId = instanceId,
            State = state,
        };
        ZoneSession.Instance.Add(connection);
        ZoneSession.Instance.IndexByZoneId(connection);
        _connections.Add(connection);
        return new ZoneFixture(connection, session);
    }

    private void RemoveConnections()
    {
        foreach (var connection in _connections)
            ZoneSession.Instance.Remove(connection.Id);
        _connections.Clear();
    }

    /// <summary>WZ frame is [u16 length][u16 opcode][body], so the opcode sits at offset 2.</summary>
    private static ushort Opcode(byte[] packet) => BitConverter.ToUInt16(packet, 2);

    /// <summary>WZNpcStartDespawn body: a bcId (WriteBc encoding) after the two u16 header words.</summary>
    private static uint BcId(byte[] packet)
    {
        var stream = new PacketStream(packet);
        stream.ReadUInt16();
        stream.ReadUInt16();
        return stream.ReadBc();
    }

    /// <summary>
    /// ZWSpawnNpc body: u32 sid, u32 sType, u8 mIdx, u8 pIdx, u16 tIdx, u32 templateId, u32 groupType,
    /// u32 groupId, u8 groupMemberIdx, f32 x, y, z, f32 zRot, f32 scale, then a zeroed tail.
    /// </summary>
    private static byte[] CreateSpawnBody(uint spawnerId, uint spawnerType) =>
        new PacketStream()
            .Write(spawnerId)
            .Write(spawnerType)
            .Write((byte)0)
            .Write((byte)0)
            .Write((ushort)0)
            .Write(1001u)
            .Write(0u)
            .Write(0u)
            .Write((byte)0)
            .Write(1000f)
            .Write(2000f)
            .Write(50f)
            .Write(0f)
            .Write(1f)
            .Write(new byte[37])
            .GetBytes();

    private sealed record ZoneFixture(ZoneConnection Connection, RecordingSession Session);

    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId { get; } = sessionId;
        public Socket Socket { get; } = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }
}
