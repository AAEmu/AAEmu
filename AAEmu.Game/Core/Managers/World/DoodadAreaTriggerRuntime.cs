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
    /// Args: zone id, unit id, area group id, area id, area secondary value, entering.
    /// </summary>
    public static void OnZoneAreaEvent(
        uint zoneId, uint unitId, uint groupId, int areaId, int secondary, bool entering)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        var unit = WorldIntegration.FindUnitAcrossWorlds(unitId);
        if (unit is not Character character || character.ObjId != unitId)
            return;

        if (zoneId != 0 && character.Transform.ZoneId != zoneId)
            return;

        if (!ShouldHandleEvent(groupId, areaId))
            return;

        var edges = AreaTriggerManager.Instance.AreaEdges;
        if (!entering)
        {
            // The unit is already outside the area, so containment can no longer be
            // tested and the doodads are not rediscovered: a leave edge only drops
            // the membership its enter edge created, for every owner at once — and
            // only for the area that was left, not every area of the same kind.
            edges.ForgetMembership(zoneId, unitId, groupId, unchecked((uint)areaId));
            return;
        }

        // The packet names the area; it carries no geometry. Zone has already decided
        // the unit is inside it, so World does not re-derive containment from a
        // distance it does not have. Candidates are the area-trigger doodads in the
        // unit's region neighbourhood — the scope the world model actually defines,
        // rather than a radius read out of an id.
        var candidates = new List<AreaTriggerCandidate>();
        foreach (var doodad in WorldManager.GetAround<Doodad>(character))
        {
            // A pending despawn is the same refusal the normal use path applies.
            if (doodad == null || !doodad.IsVisible || doodad.Despawn > DateTime.MinValue)
                continue;

            // The doodad itself must own the trigger.
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

            candidates.Add(new AreaTriggerCandidate(doodad, trigger, template));
        }

        ApplyEnterEdges(zoneId, unitId, groupId, unchecked((uint)areaId), candidates, edges,
            (candidate, _) => Dispatch(candidate.Doodad, character, candidate.Func));    }

    /// <summary>
    /// An area edge is usable only when it names an area group and a real area id.
    /// <para>
    /// <c>groupId</c> is the area KIND (the dedicated level data carries it as a
    /// constant <c>GroupId</c> — every district row shares one), and <c>value1</c> is
    /// the individual area's id within that kind. Zero names no area and a negative value
    /// is not an id, so both are refused rather than matched against every area of the
    /// kind. This is deliberately NOT a distance test: the field is an id.
    /// </para>
    /// </summary>
    public static bool ShouldHandleEvent(uint groupId, int areaId) => groupId != 0 && areaId > 0;

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
        uint areaId,
        IReadOnlyList<AreaTriggerCandidate> candidates,
        AreaEdgeTracker edges,
        Func<AreaTriggerCandidate, uint, bool> dispatch)
    {
        var dispatched = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Template == null || !ShouldDispatch(candidate.Template, entering: true))
                continue;

            var key = new AreaEdgeKey(zoneId, unitId, groupId, areaId, candidate.Doodad.ObjId);
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
                Logger.Error(e, "DoodadFuncAreaTrigger dispatch failed obj={0} zone={1} unit={2} group={3} area={4}",
                    candidate.Doodad.ObjId, zoneId, unitId, groupId, areaId);
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
/// A doodad that owns an area trigger and is in scope for the current area edge.
/// <para>
/// There is no range flag: the edge names an area but carries no geometry, and World
/// holds no area-geometry registry, so containment cannot be re-derived here. Zone owns
/// the enter/leave decision and this runtime trusts it.
/// </para>
/// </summary>
public readonly record struct AreaTriggerCandidate(
    Doodad Doodad,
    DoodadFunc Func,
    DoodadFuncAreaTrigger Template);

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
