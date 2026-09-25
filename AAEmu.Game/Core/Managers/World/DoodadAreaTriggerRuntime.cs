using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// The Zone area relay for <c>DoodadFuncAreaTrigger</c>.
/// Zone owns enter/leave edges; World owns the doodad state that follows them.
/// </summary>
public static class DoodadAreaTriggerRuntime
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Rows already refused for naming an unresolved target, logged once each.</summary>
    private static readonly ConcurrentDictionary<uint, bool> WarnedUnmappedTargets = new();

    /// <summary>
    /// Handles one ZW area membership edge.
    /// Args: zone id, unit id, area group id, area radius, area secondary value, entering.
    /// </summary>
    public static void OnZoneAreaEvent(
        uint zoneId, uint unitId, uint groupId, int radius, int secondary, bool entering)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        var unit = WorldIntegration.FindUnitAcrossWorlds(unitId);
        if (unit is not Character character || character.ObjId != unitId)
            return;

        if (zoneId != 0 && character.Transform.ZoneId != zoneId)
            return;

        if (!ShouldHandleEvent(groupId, radius))
            return;

        var edges = AreaTriggerManager.Instance.AreaEdges;
        if (!entering)
        {
            // The unit is already outside the area, so containment can no longer be
            // tested and the doodads are not rediscovered: a leave edge only drops
            // the membership its enter edge created, for every owner at once.
            edges.ForgetMembership(zoneId, unitId, groupId);
            return;
        }

        var areaShape = new AreaShape
        {
            Id = groupId,
            Type = AreaShapeType.Sphere,
            Value1 = radius,
        };

        var candidates = new List<AreaTriggerCandidate>();
        foreach (var doodad in WorldManager.GetAround<Doodad>(character, radius, true))
        {
            // A pending despawn is the same refusal the normal use path applies.
            if (doodad == null || !doodad.IsVisible || doodad.Despawn > DateTime.MinValue)
                continue;

            // The doodad itself must own the trigger, and the character must still
            // be inside the radius Zone reported for this edge.
            var trigger = FindAreaTriggerFunc(doodad);
            if (trigger == null)
                continue;

            var template = DoodadManager.Instance.GetFuncTemplate(trigger.FuncId, trigger.FuncType)
                as DoodadFuncAreaTrigger;
            if (template == null)
                continue;

            // Fail closed, but never silently: a row naming a target the content
            // cannot resolve is reported once so the gap is visible in the log.
            if (ResolveTarget(template) != AreaTriggerTarget.Self &&
                WarnedUnmappedTargets.TryAdd(trigger.FuncId, true))
            {
                Logger.Warn(
                    "DoodadFuncAreaTrigger func={0} npc_id={1} is not dispatchable: " +
                    "no target resolution exists for a non-NULL npc_id; row skipped",
                    trigger.FuncId, template.NpcId);
            }

            var inRange = WorldManager.GetAroundByShape<Character>(doodad, areaShape)
                .Any(c => c.ObjId == unitId);
            candidates.Add(new AreaTriggerCandidate(doodad, trigger, template, inRange));
        }

        ApplyEnterEdges(zoneId, unitId, groupId, candidates, edges,
            (candidate, _) => Dispatch(candidate.Doodad, character, candidate.Func));    }

    /// <summary>
    /// An area edge is usable only when it names an area group and carries the
    /// positive radius Zone reported for it; Zone never emits the zero-radius form.
    /// </summary>
    public static bool ShouldHandleEvent(uint groupId, int radius) => groupId != 0 && radius > 0;

    /// <summary>
    /// Resolves which unit a row reacts to. The <c>npc_id</c> column is the only
    /// target selector on <c>doodad_func_area_triggers</c>, and all 72 shipped rows
    /// leave it NULL, which is the loader's "no value" for that column
    /// (<c>reader.GetUInt32("npc_id", 0)</c>). A row with no value to point at can
    /// only mean the unit that owns it, so NULL resolves to <see cref="AreaTriggerTarget.Self"/>
    /// — the doodad reacts to the edge that reached it, with no target lookup.
    /// <para>
    /// Any other value is a target the shipped data never resolves, so it fails closed
    /// as <see cref="AreaTriggerTarget.Unmapped"/> and is refused rather than matched
    /// against a guessed unit.
    /// </para>
    /// </summary>
    public static AreaTriggerTarget ResolveTarget(DoodadFuncAreaTrigger template) =>
        template is { NpcId: 0 } ? AreaTriggerTarget.Self : AreaTriggerTarget.Unmapped;

    /// <summary>
    /// A row dispatches on an edge when its <c>is_enter</c> matches the edge direction
    /// and its <c>npc_id</c> resolves under <see cref="ResolveTarget"/>. Every state
    /// outside the shipped contract is refused.
    /// </summary>
    public static bool ShouldDispatch(DoodadFuncAreaTrigger template, bool entering) =>
        template != null && ResolveTarget(template) == AreaTriggerTarget.Self && template.IsEnter == entering;

    /// <summary>
    /// Dispatches each eligible candidate exactly once.
    /// <para>
    /// A key is claimed for the whole dispatch, so a pair of concurrent enter edges for
    /// the same doodad runs the work once. The membership is published only after the
    /// dispatch reports success: a refused or throwing dispatch leaves the edge open so
    /// the next enter retries it instead of being suppressed forever. One failing doodad
    /// is isolated and the remaining candidates still run.
    /// </para>
    /// </summary>
    /// <returns>How many doodads were dispatched.</returns>
    public static int ApplyEnterEdges(
        uint zoneId,
        uint unitId,
        uint groupId,
        IReadOnlyList<AreaTriggerCandidate> candidates,
        AreaEdgeTracker edges,
        Func<AreaTriggerCandidate, uint, bool> dispatch)
    {
        var dispatched = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Template == null || !ShouldDispatch(candidate.Template, entering: true))
                continue;
            if (!candidate.InRange)
                continue;

            var key = new AreaEdgeKey(zoneId, unitId, groupId, candidate.Doodad.ObjId);
            var claim = edges.TryBegin(key);
            if (claim == null)
                continue;

            bool ok;
            try
            {
                ok = dispatch(candidate, unitId);
            }
            catch (Exception e)
            {
                Logger.Error(e, "DoodadFuncAreaTrigger dispatch failed obj={0} zone={1} unit={2} group={3}",
                    candidate.Doodad.ObjId, zoneId, unitId, groupId);
                ok = false;
            }

            if (!ok)
            {
                // Nothing ran: leave the edge unclaimed so a later enter can try again.
                edges.Abort(claim.Value);
                continue;
            }

            // A leave, a doodad removal or a reset that landed while this dispatch was
            // running refuses the commit, so the edge stays open instead of holding a
            // membership that would suppress the next enter.
            if (edges.Commit(claim.Value))
                dispatched++;
        }

        return dispatched;
    }

    private static DoodadFunc FindAreaTriggerFunc(Doodad doodad)
    {
        foreach (var func in doodad.CurrentFuncs)
        {
            if (func.FuncType == nameof(DoodadFuncAreaTrigger))
                return func;
        }
        return null;
    }

    /// <summary>
    /// Runs the owning function through the normal doodad use path.
    /// </summary>
    /// <returns>
    /// False when the function is no longer part of the doodad's current phase — the phase
    /// moved under us — so the caller must not record the edge as dispatched.
    /// </returns>
    private static bool Dispatch(Doodad doodad, Character character, DoodadFunc func)
    {
        lock (doodad)
        {
            if (doodad.CurrentFuncs.Contains(func) == false)
                return false;

            // The template itself is the marker. Setting ToNextPhase here is what
            // the normal Use path does after a completed area trigger.
            doodad.ToNextPhase = true;
            doodad.DoFunc(character, 0, func);
            if (doodad.ToNextPhase)
                doodad.DoChangePhase(character, (int)doodad.FuncGroupId);
            return true;
        }
    }
}

/// <summary>
/// A doodad that is a candidate for the current area edge, with the range answer
/// already resolved so the edge decision needs no world lookups.
/// </summary>
public readonly record struct AreaTriggerCandidate(
    Doodad Doodad,
    DoodadFunc Func,
    DoodadFuncAreaTrigger Template,
    bool InRange);

/// <summary>
/// What a <c>DoodadFuncAreaTrigger</c> row reacts to on an area edge.
/// </summary>
public enum AreaTriggerTarget
{
    /// <summary><c>npc_id</c> is NULL: the row names no other unit, so its owner reacts.</summary>
    Self,

    /// <summary><c>npc_id</c> names a target this build cannot resolve; the row is refused.</summary>
    Unmapped
}
