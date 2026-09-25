using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// The Zone area edge is the only spatial signal the doodad side has: an area
/// group, a positive radius, and the entering flag. Every decision below is pinned
/// so a malformed, repeated or stale edge cannot advance a doodad twice.
/// </summary>
public class DoodadAreaTriggerRuntimeTests
{
    private const uint ZoneId = 133;
    private const uint GroupId = 0x16;
    private const uint UnitId = 0x010203;

    [Test]
    [Arguments(1u, 10, true)]
    [Arguments(1u, 1, true)]
    [Arguments(0u, 10, false)]
    [Arguments(1u, 0, false)]
    [Arguments(1u, -5, false)]
    public async Task ShouldHandleEvent_RequiresGroupAndPositiveRadius(
        uint groupId, int radius, bool expected)
    {
        await Assert.That(DoodadAreaTriggerRuntime.ShouldHandleEvent(groupId, radius)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0u, true, true)]
    [Arguments(0u, false, false)]
    [Arguments(1234u, true, false)]
    [Arguments(1234u, false, false)]
    public async Task ShouldDispatch_OnlyNullNpcIdSelfRowsMatchTheEdge(
        uint npcId, bool entering, bool expected)
    {
        var template = new DoodadFuncAreaTrigger { NpcId = npcId, IsEnter = true };

        await Assert.That(DoodadAreaTriggerRuntime.ShouldDispatch(template, entering)).IsEqualTo(expected);
    }

    [Test]
    public async Task EnterEdge_DispatchesTheOwningDoodadOnce()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var dispatched = new List<(uint Doodad, uint Unit)>();

        var count = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (c, unit) =>
            {
                dispatched.Add((c.Doodad.ObjId, unit));
                return true;
            });

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(dispatched.Count).IsEqualTo(1);
        // NULL npc_id means the row dispatches itself: the owner doodad, not a target.
        await Assert.That(dispatched[0].Doodad).IsEqualTo(900u);
        await Assert.That(dispatched[0].Unit).IsEqualTo(UnitId);
    }

    [Test]
    public async Task EnterEdge_RepeatedEnterDoesNotDispatchTwice()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var dispatched = 0;
        var apply = () => DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (_, _) =>
            {
                dispatched++;
                return true;
            });

        await Assert.That(apply()).IsEqualTo(1);
        await Assert.That(apply()).IsEqualTo(0);
        await Assert.That(dispatched).IsEqualTo(1);
    }

    [Test]
    public async Task LeaveEdge_ReleasesMembershipWithoutRangeLookupAndReopensTheTrigger()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var dispatched = 0;
        Func<AreaTriggerCandidate, uint, bool> dispatch = (_, _) =>
        {
            dispatched++;
            return true;
        };

        DoodadAreaTriggerRuntime.ApplyEnterEdges(ZoneId, UnitId, GroupId, [candidate], edges, dispatch);
        await Assert.That(dispatched).IsEqualTo(1);

        // A leave edge arrives after the unit stepped out, so it cannot range-test:
        // the membership is released for every owner at once instead.
        await Assert.That(edges.ForgetMembership(ZoneId, UnitId, GroupId)).IsEqualTo(1);

        // Standing on the edge again is a real state change and must dispatch.
        var reentered = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges, dispatch);
        await Assert.That(reentered).IsEqualTo(1);
        await Assert.That(dispatched).IsEqualTo(2);
    }

    [Test]
    [Arguments(0u, AreaTriggerTarget.Self)]
    [Arguments(1234u, AreaTriggerTarget.Unmapped)]
    public async Task ResolveTarget_ReadsTheNullColumnAsSelfAndRefusesAnythingElse(
        uint npcId, AreaTriggerTarget expected)
    {
        var template = new DoodadFuncAreaTrigger { NpcId = npcId, IsEnter = true };

        await Assert.That(DoodadAreaTriggerRuntime.ResolveTarget(template)).IsEqualTo(expected);
    }

    [Test]
    public async Task EnterEdge_ClaimsTheEdgeOnlyAfterTheDispatchSucceeds()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);
        var fail = true;
        var attempts = 0;

        var throwing = () => DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (_, _) =>
            {
                attempts++;
                if (fail)
                    throw new InvalidOperationException("phase walk failed");
                return true;
            });

        await Assert.That(throwing()).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
        // The failed dispatch left no membership, so the doodad is not suppressed forever.
        await Assert.That(edges.IsInside(key)).IsFalse();

        fail = false;
        await Assert.That(throwing()).IsEqualTo(1);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task EnterEdge_OneThrowingDoodadDoesNotStopTheOthers()
    {
        var edges = new AreaEdgeTracker();
        var candidates = new List<AreaTriggerCandidate>
        {
            Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true),
            Candidate(objId: 901, npcId: 0, isEnter: true, inRange: true),
            Candidate(objId: 902, npcId: 0, isEnter: true, inRange: true),
        };
        var dispatched = new List<uint>();

        var count = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, candidates, edges,
            (c, _) =>
            {
                if (c.Doodad.ObjId == 900)
                    throw new InvalidOperationException("one bad doodad");
                dispatched.Add(c.Doodad.ObjId);
                return true;
            });

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(dispatched.Count).IsEqualTo(2);
        await Assert.That(dispatched[0]).IsEqualTo(901u);
        await Assert.That(dispatched[1]).IsEqualTo(902u);
        // The failed one holds no membership; the two that worked do.
        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, 900))).IsFalse();
        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, 901))).IsTrue();
        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, 902))).IsTrue();
    }

    [Test]
    public async Task AreaEdges_ResetDropsEverySessionMembership()
    {
        var edges = new AreaEdgeTracker();
        edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 100), entering: true);
        edges.TryTransition(new AreaEdgeKey(ZoneId, 2, GroupId, 200), entering: true);

        edges.Reset();

        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, 1, GroupId, 100))).IsFalse();
        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, 2, GroupId, 200))).IsFalse();
    }

    [Test]
    public async Task EnterEdge_ConcurrentEntersDispatchTheDoodadExactlyOnce()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var runs = 0;
        const int callers = 8;
        using var gate = new Barrier(callers);

        var tasks = Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
        {
            gate.SignalAndWait();
            DoodadAreaTriggerRuntime.ApplyEnterEdges(ZoneId, UnitId, GroupId, [candidate], edges,
                (_, _) =>
                {
                    // Hold the work long enough for the other callers to reach the same key.
                    Thread.Sleep(20);
                    Interlocked.Increment(ref runs);
                    return true;
                });
        })).ToArray();

        await Task.WhenAll(tasks);

        await Assert.That(runs).IsEqualTo(1);
        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, 900))).IsTrue();
    }

    [Test]
    public async Task EnterEdge_ADispatchThatReportsFailureClaimsNothing()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);
        var refuse = true;
        var attempts = 0;

        var apply = () => DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (_, _) =>
            {
                attempts++;
                return !refuse;
            });

        // A false result means the function never ran (the phase moved under us), so the
        // edge must stay unclaimed rather than record work that did not happen.
        await Assert.That(apply()).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(edges.IsInside(key)).IsFalse();

        refuse = false;
        await Assert.That(apply()).IsEqualTo(1);
        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task AreaEdges_TryBeginIsExclusiveUntilCommitOrAbort()
    {
        var edges = new AreaEdgeTracker();
        var key = new AreaEdgeKey(ZoneId, 1, GroupId, 100);

        var first = edges.TryBegin(key);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(edges.TryBegin(key).HasValue).IsFalse();

        edges.Abort(first!.Value);
        var second = edges.TryBegin(key);
        await Assert.That(second.HasValue).IsTrue();
        await Assert.That(edges.TryBegin(key).HasValue).IsFalse();

        await Assert.That(edges.Commit(second!.Value)).IsTrue();
        // A committed membership keeps later callers out just like an in-flight claim does.
        await Assert.That(edges.TryBegin(key).HasValue).IsFalse();
        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task ALeaveInvalidatesAClaimThatIsStillDispatching()
    {
        var edges = new AreaEdgeTracker();
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);

        // The dispatch is in flight, the leave edge lands, then the dispatch finishes.
        var claim = edges.TryBegin(key);
        await Assert.That(claim.HasValue).IsTrue();
        edges.TryTransition(key, entering: true);
        await Assert.That(edges.ForgetMembership(ZoneId, UnitId, GroupId)).IsEqualTo(1);

        await Assert.That(edges.Commit(claim!.Value)).IsFalse();
        await Assert.That(edges.IsInside(key)).IsFalse();
        // The refused commit leaves the edge open, so the next enter really can run again.
        await Assert.That(edges.TryBegin(key).HasValue).IsTrue();
    }

    [Test]
    public async Task AResetInvalidatesAClaimThatIsStillDispatching()
    {
        var edges = new AreaEdgeTracker();
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);

        var claim = edges.TryBegin(key);
        await Assert.That(claim.HasValue).IsTrue();
        edges.Reset();

        await Assert.That(edges.Commit(claim!.Value)).IsFalse();
        await Assert.That(edges.IsInside(key)).IsFalse();
    }

    [Test]
    [Arguments("leave")]
    [Arguments("unit")]
    [Arguments("area")]
    [Arguments("owner")]
    public async Task EveryForgetInvalidatesOnlyTheClaimsItCovers(string which)
    {
        var edges = new AreaEdgeTracker();
        var target = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);
        var bystander = new AreaEdgeKey(ZoneId, UnitId + 99, GroupId + 99, 1900);
        var targetClaim = edges.TryBegin(target);
        var bystanderClaim = edges.TryBegin(bystander);
        await Assert.That(targetClaim.HasValue && bystanderClaim.HasValue).IsTrue();

        switch (which)
        {
            case "leave": edges.ForgetMembership(ZoneId, UnitId, GroupId); break;
            case "unit": edges.ForgetUnit(UnitId); break;
            case "area": edges.ForgetArea(GroupId); break;
            default: edges.ForgetOwner(900); break;
        }

        // The covered claim can no longer publish; the uncovered one still can.
        await Assert.That(edges.Commit(targetClaim!.Value)).IsFalse();
        await Assert.That(edges.IsInside(target)).IsFalse();
        await Assert.That(edges.Commit(bystanderClaim!.Value)).IsTrue();
        await Assert.That(edges.IsInside(bystander)).IsTrue();
    }

    [Test]
    public async Task AFinishedClaimCannotPublishEvenAfterTheKeyIsReused()
    {
        var edges = new AreaEdgeTracker();
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, 900);

        var stale = edges.TryBegin(key);
        await Assert.That(stale.HasValue).IsTrue();
        await Assert.That(edges.Commit(stale!.Value)).IsTrue();
        edges.ForgetMembership(ZoneId, UnitId, GroupId);

        var fresh = edges.TryBegin(key);
        await Assert.That(fresh.HasValue).IsTrue();

        // The finished claim arriving late must not touch the key the new claim owns.
        await Assert.That(edges.Commit(stale.Value)).IsFalse();
        await Assert.That(edges.IsInside(key)).IsFalse();

        await Assert.That(edges.Commit(fresh!.Value)).IsTrue();
        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task ALeaveRacingADispatchNeverLeavesAStaleMembershipBehind()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var edges = new AreaEdgeTracker();
            var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true);
            using var dispatching = new ManualResetEventSlim(false);

            var dispatch = Task.Run(() => DoodadAreaTriggerRuntime.ApplyEnterEdges(
                ZoneId, UnitId, GroupId, [candidate], edges,
                (_, _) =>
                {
                    dispatching.Set();
                    Thread.Sleep(2);
                    return true;
                }));

            // The leave lands while the dispatch body is running.
            dispatching.Wait(1000);
            edges.ForgetMembership(ZoneId, UnitId, GroupId);
            await dispatch;

            // Whatever the interleaving, no membership may survive the leave: that would
            // suppress the next enter edge for good.
            await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, 900))).IsFalse();
        }
    }

    [Test]
    public async Task LeaveEdge_ReleasesEveryOwnerForThatUnitAndAreaOnly()
    {
        var edges = new AreaEdgeTracker();
        edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 100), entering: true);
        edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 200), entering: true);
        edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId + 1, 300), entering: true);
        edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId + 1, GroupId, 400), entering: true);

        await Assert.That(edges.ForgetMembership(ZoneId, UnitId, GroupId)).IsEqualTo(2);

        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 100), entering: true))
            .IsTrue();
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 200), entering: true))
            .IsTrue();
        // A different area group or a different unit keeps its membership.
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId + 1, 300), entering: true))
            .IsFalse();
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId + 1, GroupId, 400), entering: true))
            .IsFalse();
    }

    [Test]
    [Arguments(1234u, true, 0)]
    [Arguments(0u, false, 0)]
    [Arguments(0u, true, 1)]
    public async Task EnterEdge_DoesNotDispatchIneligibleRows(uint npcId, bool isEnter, int expected)
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId, isEnter, inRange: true);
        var dispatched = 0;

        var count = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (_, _) =>
            {
                dispatched++;
                return true;
            });

        await Assert.That(count).IsEqualTo(expected);
        await Assert.That(dispatched).IsEqualTo(expected);
        // A refused row claims no state, so it cannot suppress a later dispatch; a
        // dispatched one holds its claim until a leave edge releases it.
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 900), entering: true))
            .IsEqualTo(expected == 0);
    }

    [Test]
    public async Task EnterEdge_DoesNotDispatchOutsideTheReportedRadius()
    {
        var edges = new AreaEdgeTracker();
        var candidate = Candidate(objId: 900, npcId: 0, isEnter: true, inRange: false);
        var dispatched = 0;

        var count = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, [candidate], edges,
            (_, _) =>
            {
                dispatched++;
                return true;
            });

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(dispatched).IsEqualTo(0);
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, UnitId, GroupId, 900), entering: true))
            .IsTrue();
    }

    [Test]
    public async Task EnterEdge_DispatchesEachEligibleOwnerOnce()
    {
        var edges = new AreaEdgeTracker();
        var candidates = new List<AreaTriggerCandidate>
        {
            Candidate(objId: 900, npcId: 0, isEnter: true, inRange: true),
            Candidate(objId: 901, npcId: 0, isEnter: true, inRange: true),
            Candidate(objId: 902, npcId: 77, isEnter: true, inRange: true),
            Candidate(objId: 903, npcId: 0, isEnter: true, inRange: false),
        };
        var dispatched = new List<uint>();

        var count = DoodadAreaTriggerRuntime.ApplyEnterEdges(
            ZoneId, UnitId, GroupId, candidates, edges,
            (c, _) =>
            {
                dispatched.Add(c.Doodad.ObjId);
                return true;
            });

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(dispatched.Count).IsEqualTo(2);
        await Assert.That(dispatched[0]).IsEqualTo(900u);
        await Assert.That(dispatched[1]).IsEqualTo(901u);
    }

    [Test]
    public async Task AreaEdges_AreIndependentPerOwner()
    {
        var edges = new AreaEdgeTracker();
        var first = new AreaEdgeKey(ZoneId, 1, GroupId, 100);
        var second = new AreaEdgeKey(ZoneId, 1, GroupId, 200);

        await Assert.That(edges.TryTransition(first, entering: true)).IsTrue();
        await Assert.That(edges.TryTransition(second, entering: true)).IsTrue();
        await Assert.That(edges.TryTransition(first, entering: false)).IsTrue();

        // Losing the first owner must not release the second owner's membership.
        await Assert.That(edges.TryTransition(second, entering: true)).IsFalse();
    }

    [Test]
    public async Task AreaEdges_ForgetUnitReleasesEveryOwner()
    {
        var edges = new AreaEdgeTracker();
        edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 100), entering: true);
        edges.TryTransition(new AreaEdgeKey(200, 1, 9, 200), entering: true);

        await Assert.That(edges.ForgetUnit(1)).IsEqualTo(2);

        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 100), entering: true)).IsTrue();
        await Assert.That(edges.TryTransition(new AreaEdgeKey(200, 1, 9, 200), entering: true)).IsTrue();
    }

    [Test]
    public async Task AreaEdges_ForgetOwnerKeepsOtherOwnersInside()
    {
        var edges = new AreaEdgeTracker();
        edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 100), entering: true);
        edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 200), entering: true);

        await Assert.That(edges.ForgetOwner(100)).IsEqualTo(1);

        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 100), entering: true)).IsTrue();
        await Assert.That(edges.TryTransition(new AreaEdgeKey(ZoneId, 1, GroupId, 200), entering: true)).IsFalse();
    }

    private static AreaTriggerCandidate Candidate(uint objId, uint npcId, bool isEnter, bool inRange)
    {
        var template = new DoodadFuncAreaTrigger { NpcId = npcId, IsEnter = isEnter };
        var func = new DoodadFunc
        {
            FuncId = 1,
            FuncType = nameof(DoodadFuncAreaTrigger),
            NextPhase = 2,
        };
        return new AreaTriggerCandidate(new Doodad { ObjId = objId }, func, template, inRange);
    }
}
