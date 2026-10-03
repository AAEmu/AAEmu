using AAEmu.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// The zone owns who is fighting an npc and reports that membership as its abuse list; the World
/// table only carries the threat score. These cover the consumer of that list: a target the zone
/// has dropped must not win on stale threat, and an npc the zone has not reported at all must keep
/// being decided by the World's own table.
/// </summary>
[NotInParallel]
public class NpcAbuserTargetSelectionTests
{
    private const uint Npc = 0x010001;
    private const uint OtherNpc = 0x010002;
    private const uint First = 0x0A0001;
    private const uint Second = 0x0A0002;
    private const uint Reported = 0x0A0003;

    private IDisposable _worldManager = null!;
    private WorldInstance _world = null!;

    [Before(Test)]
    public void OpenScope()
    {
        NpcAbuserRegistry.Reset();
        _worldManager = TestDungeonWorld.InstallWorldManager();
        _world = TestDungeonWorld.CreateWorld(9001, 0, 1);
    }

    [After(Test)]
    public void CloseScope()
    {
        NpcAbuserRegistry.Reset();
        _worldManager.Dispose();
    }

    private Npc MirrorNpc(uint objId)
    {
        var npc = new Npc { ObjId = objId };
        TestDungeonWorld.Attach(npc, _world);
        _world.AddObject(npc);
        return npc;
    }

    /// <summary>Puts one scored row on the npc's own table, the way a World-side damage does.</summary>
    private static Unit Score(Npc npc, uint objId, int damage)
    {
        var unit = new Unit { ObjId = objId };
        var aggro = new Aggro(unit);
        aggro.AddAggro(AggroKind.Damage, damage);
        npc.AggroTable[objId] = aggro;
        return unit;
    }

    [Test]
    public async Task AUnitTheZoneNoLongerListsIsNotTheTarget()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        var second = Score(npc, Second, 50);
        NpcAbuserRegistry.Register(Npc, Second);

        var chosen = WorldIntegration.ResolveZoneKillCredit(npc);

        // The World would have picked the 100-threat unit on its own; the zone says it is no longer
        // on the npc's list, so that row must lose to the one the zone does list.
        await Assert.That(chosen).IsSameReferenceAs(second);
        await Assert.That(chosen).IsNotSameReferenceAs(first);
    }

    [Test]
    public async Task TheHighestThreatZoneListedUnitWins()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        var second = Score(npc, Second, 50);
        NpcAbuserRegistry.Register(Npc, First);
        NpcAbuserRegistry.Register(Npc, Second);

        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(first);
    }

    [Test]
    public async Task AnUnreportedNpcKeepsTheWorldsOwnChoice()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        Score(npc, Second, 50);

        // No report at all is not "nobody is fighting it" — the World's table decides as before.
        await Assert.That(NpcAbuserRegistry.HasEntry(Npc)).IsFalse();
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(first);
    }

    [Test]
    public async Task AReportNamingNobodyOnTheWorldTableIsNotOverriddenByStaleThreat()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        NpcAbuserRegistry.Register(Npc, Reported);

        var chosen = WorldIntegration.ResolveZoneKillCredit(npc);

        await Assert.That(NpcAbuserRegistry.HasEntry(Npc)).IsTrue();
        await Assert.That(chosen).IsNotSameReferenceAs(first);
    }

    [Test]
    public async Task AnEqualThreatTieGoesToTheLowerUnitId()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 50);
        var second = Score(npc, Second, 50);
        NpcAbuserRegistry.Register(Npc, First);
        NpcAbuserRegistry.Register(Npc, Second);

        var chosen = WorldIntegration.ResolveZoneKillCredit(npc);

        await Assert.That(chosen).IsSameReferenceAs(first);
        await Assert.That(chosen).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task RegisteringAnAbuserChangesWhatTheNpcIsFighting()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        var second = Score(npc, Second, 10);
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(first);

        NpcAbuserRegistry.Register(Npc, Second);

        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(second);
    }

    [Test]
    public async Task TheNpcLeavingTheWorldStopsTheReportFromNamingATarget()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 100);
        var second = Score(npc, Second, 10);
        NpcAbuserRegistry.Register(Npc, Second);
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(second);

        // The death / despawn path: the npc holds no list after this, so a recycled id cannot
        // inherit the previous occupant's abusers and hand its kill to a unit that never fought it.
        NpcAbuserRegistry.ForgetNpc(Npc);

        await Assert.That(NpcAbuserRegistry.HasEntry(Npc)).IsFalse();
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(first);
    }

    [Test]
    public async Task TheAbuserLeavingTheWorldStopsTheReportFromNamingATarget()
    {
        var npc = MirrorNpc(Npc);
        var first = Score(npc, First, 50);
        var second = Score(npc, Second, 100);
        NpcAbuserRegistry.Register(Npc, First);
        NpcAbuserRegistry.Register(Npc, Second);
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(second);

        // The disconnect path: the zone reports unregisters for live participants only, so a
        // departed unit would otherwise stay the npc's target through the World's stale threat.
        NpcAbuserRegistry.ForgetUnit(Second);

        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsSameReferenceAs(first);
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsNotSameReferenceAs(second);
    }

    [Test]
    public async Task AnNpcWithNoAggroRowsHasNoTarget()
    {
        var npc = MirrorNpc(Npc);
        NpcAbuserRegistry.Register(Npc, First);

        await Assert.That(WorldIntegration.ResolveZoneKillCredit(npc)).IsNull();
    }

    [Test]
    public async Task ClearingOneNpcLeavesTheOtherNpcsReportAlone()
    {
        var npc = MirrorNpc(Npc);
        var other = MirrorNpc(OtherNpc);
        Score(npc, First, 100);
        var otherFirst = Score(other, First, 100);
        NpcAbuserRegistry.Register(Npc, First);
        NpcAbuserRegistry.Register(OtherNpc, First);

        NpcAbuserRegistry.Clear(Npc);

        // The clear is per npc: another npc keeps its own list and its own target.
        await Assert.That(NpcAbuserRegistry.HasEntry(Npc)).IsFalse();
        await Assert.That(NpcAbuserRegistry.HasEntry(OtherNpc)).IsTrue();
        await Assert.That(WorldIntegration.ResolveZoneKillCredit(other)).IsSameReferenceAs(otherFirst);
    }
}
