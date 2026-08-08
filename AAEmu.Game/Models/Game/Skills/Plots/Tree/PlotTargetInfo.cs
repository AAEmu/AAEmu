using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotTargetInfo
{
    public BaseUnit Source { get; set; }
    /// <summary>enum_plot_area_target_kinds: which unit an area shape is aimed at.</summary>
    private BaseUnit ResolveAreaAnchor(AreaShape shape, PlotState state, BaseUnit currentPosition) =>
        shape.AreaTargetKind switch
        {
            AreaTargetKindType.OriginalSource => state.Caster,
            AreaTargetKindType.OriginalTarget => state.Target,
            AreaTargetKindType.PreviousSource => PreviousSource,
            AreaTargetKindType.PreviousTarget => PreviousTarget,
            AreaTargetKindType.CurrentPosition => currentPosition,
            _ => PreviousTarget
        };

    /// <summary>
    /// Origin a cone-shaped sphere (aoe_shapes value3 &gt; 0) is measured from.
    /// </summary>
    /// <remarks>
    /// A cone has a direction, and <see cref="AreaShape.FilterSphereCone"/> takes that direction from the
    /// origin's own world rotation. Anchoring it on <see cref="PreviousTarget"/> like a plain sphere aimed
    /// it along the TARGET's facing, from the TARGET's position — so Ceaseless Fire (44196, shape 20447:
    /// r 22.7, cone 45) swept out of the mob in whatever direction it happened to be looking, while the
    /// client drew the telegraph out of the player. The search then came back empty, the per_target edge
    /// to the projectile/damage node expanded to nothing, and the skill played its animation and cooldown
    /// without ever dealing damage.
    ///
    /// Plain spheres are unaffected: "everything within R of the target" stays anchored on the target.
    /// Where area_target_kind_id IS set the data wins, exactly as for line corridors.
    /// </remarks>
    private BaseUnit ResolveConeOrigin(AreaShape shape, PlotState state, BaseUnit fallback)
    {
        var origin = shape.AreaTargetKind != AreaTargetKindType.None
            ? ResolveAreaAnchor(shape, state, fallback)
            : Source ?? state.Caster;

        // GetAround walks the origin's region; an origin without one would find nothing at all.
        return origin?.Region != null ? origin : fallback;
    }

    /// <summary>
    /// Records how far the search that picked these units legitimately reached, so the plot's own Range
    /// gate can honour it instead of cutting inside its own selection. See
    /// <see cref="PlotState.AreaSelectionRadius"/>.
    /// </summary>
    /// <remarks>
    /// Only for spheres with a real radius. Blank shape rows fall back to a 40m guess in WorldManager, and
    /// letting that guess widen a range gate would turn a placeholder into reach.
    /// </remarks>
    private static void RememberSelectionRadius(PlotState state, AreaShape shape, IEnumerable<Unit> units)
    {
        if (shape is not { Type: AreaShapeType.Sphere } || shape.Value1 <= 0f)
            return;

        foreach (var unit in units)
            state.AreaSelectionRadius[unit.ObjId] = shape.Value1;
    }

    /// <summary>True when this shape is a sphere carrying a cone rather than a full 360° disc.</summary>
    private static bool IsCone(AreaShape shape) =>
        shape is { Type: AreaShapeType.Sphere } && shape.SphereConeHalfAngleDegrees > 0f;

    private BaseUnit PreviousSource { get; set; }
    public BaseUnit Target { get; set; }
    private BaseUnit PreviousTarget { get; set; }
    public List<BaseUnit> EffectedTargets { get; set; }

    public PlotTargetInfo(PlotState state)
    {
        EffectedTargets = [];
        PreviousSource = state.Caster;
        PreviousTarget = state.Target;
    }

    public PlotTargetInfo(BaseUnit source, BaseUnit target)
    {
        EffectedTargets = [];
        PreviousSource = source;
        PreviousTarget = target;
        Source = source;
        Target = target;
    }

    public void UpdateTargetInfo(PlotEventTemplate template, PlotState state)
    {
        UpdateSource(template, state);
        UpdateTargets(template, state);
    }

    public void UpdateSource(PlotEventTemplate template, PlotState state)
    {
        switch ((PlotSourceUpdateMethodType)template.SourceUpdateMethodId)
        {
            case PlotSourceUpdateMethodType.OriginalSource:
                Source = state.Caster;
                break;
            case PlotSourceUpdateMethodType.OriginalTarget:
                Source = state.Target;
                break;
            case PlotSourceUpdateMethodType.PreviousSource:
                Source = PreviousSource;
                break;
            case PlotSourceUpdateMethodType.PreviousTarget:
                Source = PreviousTarget;
                break;
        }
    }

    public void UpdateTargets(PlotEventTemplate template, PlotState state)
    {
        switch ((PlotTargetUpdateMethodType)template.TargetUpdateMethodId)
        {
            case PlotTargetUpdateMethodType.OriginalSource:
                Target = state.Caster;
                EffectedTargets.Add(Target);
                break;
            case PlotTargetUpdateMethodType.OriginalTarget:
                Target = state.Target;
                EffectedTargets.Add(Target);
                break;
            case PlotTargetUpdateMethodType.PreviousSource:
                Target = PreviousSource;
                EffectedTargets.Add(Target);
                break;
            case PlotTargetUpdateMethodType.PreviousTarget:
                Target = PreviousTarget;
                EffectedTargets.Add(Target);
                break;
            case PlotTargetUpdateMethodType.Area:
                Target = UpdateAreaTarget(new PlotTargetAreaParams(template), state, template);
                break;
            case PlotTargetUpdateMethodType.RandomUnit:
                Target = UpdateRandomUnitTarget(new PlotTargetRandomUnitParams(template), state, template);
                break;
            case PlotTargetUpdateMethodType.RandomArea:
                Target = UpdateRandomAreaTarget(new PlotTargetRandomAreaParams(template), state, template);
                break;
        }
    }

    private BaseUnit UpdateAreaTarget(PlotTargetAreaParams args, PlotState state, PlotEventTemplate plotEvent)
    {
        var posUnit = new BaseUnit { ObjId = uint.MaxValue, Region = PreviousTarget.Region };
        posUnit.Transform = PreviousTarget.Transform.CloneDetached(posUnit);
        var degrees = (float)args.Angle;
        posUnit.Transform.Local.Rotate(0, 0, degrees.DegToRad() * -1f);
        // posUnit.Transform.Local.Rotate(Quaternion.CreateFromYawPitchRoll(((float)args.Angle).DegToRad() * -1f, 0f, 0f));
        if (args.Distance != 0)
        {
            posUnit.Transform.Local.AddDistanceToFront(args.Distance / 1000f - 0.01f);
        }
        // TODO: Make this use geo data, need to check if we can grab parent world from here
        posUnit.Transform.Local.SetHeight(Math.Max(PreviousTarget.Transform.World.Position.Z + args.HeightOffset / 1000f, WorldManager.Instance.GetHeight(posUnit.Transform)));

        if (args.MaxTargets == 0)
        {
            EffectedTargets.Add(posUnit);
            return posUnit;
        }

        // posUnit.Position.Z = get heightmap value for x:y
        //TODO: Get Targets around posUnit?
        // A line shape is a corridor, not a box around the anchor: it starts at this event's Source
        // and is aimed at whatever area_target_kind_id names. Handing it the centred-box path put the
        // aimed-at unit exactly on the rectangle's diagonal, where the strict point-in-triangle test
        // drops it — the search came back empty and every per-target edge behind it enqueued nothing.
        var searchOrigin = IsCone(args.Shape) ? ResolveConeOrigin(args.Shape, state, posUnit) : posUnit;
        var trace = AreaDebug ? new List<(string step, int left)>() : null;
        var candidates = args.Shape switch
        {
            { Type: AreaShapeType.Line } =>
                WorldManager.GetAroundByShape<Unit>(posUnit, args.Shape, Source, ResolveAreaAnchor(args.Shape, state, posUnit)),
            _ when IsCone(args.Shape) =>
                WorldManager.GetAroundByShape<Unit>(searchOrigin, args.Shape, trace),
            _ => WorldManager.GetAroundByShape<Unit>(posUnit, args.Shape, trace)
        };
        var unitsInRange = FilterTargets(candidates, state, args, plotEvent, trace).Take(args.MaxTargets).ToList();
        // TODO : Filter min distance
        // TODO : Compute Unit Relation
        // TODO : Compute Unit Flag
        // unitsInRange = unitsInRange.Where(u => u.);

        RememberSelectionRadius(state, args.Shape, unitsInRange);

        if (AreaDebug)
            LogAreaSearch(plotEvent, args, searchOrigin, state, candidates.Count, trace, unitsInRange.Count, args.MaxTargets);

        EffectedTargets.AddRange(unitsInRange);
        if (state.HitObjects.TryGetValue(plotEvent.Id, out var o))
        {
            o.AddRange(unitsInRange);
        }
        else
        {
            state.HitObjects.Add(plotEvent.Id, [.. unitsInRange]);
        }

        return posUnit;
    }

    private Unit UpdateRandomUnitTarget(PlotTargetRandomUnitParams args, PlotState state, PlotEventTemplate plotEvent)
    {
        //TODO for now we get all units in a 5 meters radius
        var randomUnits = WorldManager.GetAroundByShape<Unit>(Source, args.Shape);

        var filteredUnits = FilterTargets(randomUnits, state, args, plotEvent);
        if (args.HitOnce)
            filteredUnits = filteredUnits.Where(unit => unit.ObjId != PreviousTarget.ObjId);

        var index = Random.Shared.Next(0, filteredUnits.Count());

        if (!filteredUnits.Any())
            return null;

        var randomUnit = filteredUnits.ElementAt(index);

        EffectedTargets.Add(randomUnit);
        if (state.HitObjects.TryGetValue(plotEvent.Id, out var o))
        {
            o.Add(randomUnit);
        }
        else
        {
            state.HitObjects.Add(plotEvent.Id, [randomUnit]);
        }

        return randomUnit;
    }

    private BaseUnit UpdateRandomAreaTarget(PlotTargetRandomAreaParams args, PlotState state, PlotEventTemplate plotEvent)
    {
        var posUnit = new BaseUnit { ObjId = uint.MaxValue, Region = PreviousTarget.Region };
        posUnit.Transform = PreviousTarget.Transform.CloneDetached(posUnit);
        posUnit.Transform.ZoneId = PreviousTarget.Transform.ZoneId;
        posUnit.Transform.InstanceId = PreviousTarget.Transform.InstanceId;
        posUnit.Transform.Local.SetZRotation(((float)Random.Shared.Next(-180, 180)).DegToRad());
        posUnit.Transform.Local.AddDistanceToFront(args.Distance / 1000f);
        // TODO: Make this use geo data, need to check if we can grab parent world from here
        posUnit.Transform.Local.SetHeight(Math.Max(PreviousTarget.Transform.World.Position.Z + args.HeightOffset / 1000f, WorldManager.Instance.GetHeight(posUnit.Transform)));

        if (args.MaxTargets == 0)
        {
            EffectedTargets.Add(posUnit);
            return posUnit;
        }

        // posUnit.Position.Z = get heightmap value for x:y
        //TODO: Get Targets around posUnit?
        // A line shape is a corridor, not a box around the anchor: it starts at this event's Source
        // and is aimed at whatever area_target_kind_id names. Handing it the centred-box path put the
        // aimed-at unit exactly on the rectangle's diagonal, where the strict point-in-triangle test
        // drops it — the search came back empty and every per-target edge behind it enqueued nothing.
        var searchOrigin = IsCone(args.Shape) ? ResolveConeOrigin(args.Shape, state, posUnit) : posUnit;
        var trace = AreaDebug ? new List<(string step, int left)>() : null;
        var candidates = args.Shape switch
        {
            { Type: AreaShapeType.Line } =>
                WorldManager.GetAroundByShape<Unit>(posUnit, args.Shape, Source, ResolveAreaAnchor(args.Shape, state, posUnit)),
            _ when IsCone(args.Shape) =>
                WorldManager.GetAroundByShape<Unit>(searchOrigin, args.Shape, trace),
            _ => WorldManager.GetAroundByShape<Unit>(posUnit, args.Shape, trace)
        };
        var unitsInRange = FilterTargets(candidates, state, args, plotEvent, trace).Take(args.MaxTargets).ToList();

        RememberSelectionRadius(state, args.Shape, unitsInRange);

        if (AreaDebug)
            LogAreaSearch(plotEvent, args, searchOrigin, state, candidates.Count, trace, unitsInRange.Count, args.MaxTargets);

        // TODO : Filter min distance
        // TODO : Compute Unit Relation
        // TODO : Compute Unit Flag
        // unitsInRange = unitsInRange.Where(u => u.);

        EffectedTargets.AddRange(unitsInRange);
        if (state.HitObjects.TryGetValue(plotEvent.Id, out var o))
        {
            o.AddRange(unitsInRange);
        }
        else
        {
            state.HitObjects.Add(plotEvent.Id, [.. unitsInRange]);
        }

        return posUnit;
    }

    /// <summary>
    /// Reports, per plot area search, where it looked and how many units each filter step drops.
    /// Set AAEMU_PLOT_AREA_DEBUG=0 to turn it off.
    /// </summary>
    /// <remarks>
    /// On by default while plot AoE target counts are under investigation. It materialises the
    /// intermediate lists, but one line per area search is nothing next to the per-packet and per-doodad
    /// TRACE this log already carries — and it is the one step whose outcome nothing else records:
    /// events without effects or conditions emit no SCPlotEvent, so an empty search looks exactly like an
    /// event that never ran.
    /// </remarks>
    private static readonly bool AreaDebug = Environment.GetEnvironmentVariable("AAEMU_PLOT_AREA_DEBUG") != "0";

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// One line describing where a plot area search looked and what each filter step removed. This is the
    /// question the ordinary log cannot answer: events without effects or conditions emit no SCPlotEvent,
    /// so an empty search is indistinguishable from an event that never ran.
    /// </summary>
    private static void LogAreaSearch(
        PlotEventTemplate plotEvent, IPlotTargetParams args, GameObject origin, PlotState state,
        int found, IReadOnlyList<(string step, int left)> steps, int taken, int maxTargets)
    {
        var shape = args.Shape;
        var pos = origin?.Transform?.World?.Position ?? System.Numerics.Vector3.Zero;
        var yaw = origin?.Transform?.World?.Rotation.Z ?? 0f;
        Logger.Info(
            "PlotArea evt={0} skill={1} shape={2}(kind={3} v1={4:F1} v2={5:F1} v3={6:F1}) origin={7} pos=({8:F1},{9:F1},{10:F1}) yawDeg={11:F0} rel={12} typeFlag={13} maxT={14} | found={15} → {16} | taken={17}",
            plotEvent.Id, state.ActiveSkill?.Template?.Id, shape?.Id, shape?.Type, shape?.Value1, shape?.Value2,
            shape?.Value3, origin?.ObjId, pos.X, pos.Y, pos.Z, yaw.RadToDeg(), args.UnitRelationType,
            args.UnitTypeFlag, maxTargets, found,
            string.Join(" → ", steps.Select(s => $"{s.step}:{s.left}")), taken);

        LogNeighbourhood(origin, shape);
    }

    /// <summary>
    /// Lists what the server believes stands near the search origin, with each unit's distance and bearing.
    /// </summary>
    /// <remarks>
    /// The counts above answer "how many survived each filter" but not "did the server ever know this unit
    /// was there". With six dummies visibly at 3-8m and a 9.7m sphere reporting two, that is the question
    /// that matters: a unit listed here at 4m but absent from the search points at the region index, while
    /// a unit listed at 30m means the server's position for it disagrees with what the client draws.
    /// </remarks>
    private static void LogNeighbourhood(GameObject origin, AreaShape shape)
    {
        if (origin?.Region == null || shape is not { Type: AreaShapeType.Sphere })
            return;

        // Raw 2D centre-to-centre distance, matching what GetAround measures — deliberately without the
        // model-radius subtraction GetDistanceTo applies, so the number here is pure geometry.
        var near = WorldManager.GetAround<Unit>(origin, NeighbourhoodProbeRadius, true)
            .Select(u => (u, dist: MathUtil.CalculateDistance(
                origin.Transform.World.Position, u.Transform.World.Position, false)))
            .OrderBy(x => x.dist)
            .Take(12)
            .Select(x => $"{x.u.ObjId}({x.u.TemplateId}) d={x.dist:F1} a={MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(origin, x.u)):F0}°");

        Logger.Info("PlotArea   neighbourhood({0}m): {1}", NeighbourhoodProbeRadius, string.Join(", ", near));
    }

    /// <summary>How far the neighbourhood dump looks — wide enough to catch units the shape radius missed.</summary>
    private const float NeighbourhoodProbeRadius = 40f;

    private static IEnumerable<Unit> FilterTargets(IEnumerable<Unit> units, PlotState state, IPlotTargetParams args, PlotEventTemplate plotEvent,
        List<(string step, int left)> trace = null)
    {
        var template = state.ActiveSkill.Template;
        var filtered = units;
        if (!template.TargetAlive)
            filtered = filtered.Where(o => o.Hp == 0);
        if (!template.TargetDead)
            filtered = filtered.Where(o => o.Hp > 0);
        Step("alive/dead");
        if (args.HitOnce)
        {
            filtered = filtered.Where(o =>
            {
                if (state.HitObjects.TryGetValue(plotEvent.Id, out var o1))
                    return !o1.Contains(o);
                else
                    return true;
            });
            Step("hitOnce");
        }

        filtered = filtered
            .Where(o =>
            {
                var relationState = state.Caster.GetRelationStateTo(o);
                if (relationState == RelationState.Neutral) // TODO ?
                    return false;
                return true;
            });
        Step("notNeutral");

        filtered = SkillTargetingUtil.FilterWithRelation(args.UnitRelationType, state.Caster, filtered);
        Step("relation");
        filtered = filtered.Where(o => ((byte)o.TypeFlag & args.UnitTypeFlag) != 0);
        Step("typeFlag");

        // plot_aoe_conditions (loaded on PlotEventTemplate.AoeConditions) gate Area / Random*
        // picks. Crime Aura 40917 event 35070 requires BuffTag 894 (현상수배); without this
        // filter every character in the 25m sphere is "wanted" → taunt, chip damage, and the
        // criminal bubble on a Friendly Nuia guard.
        if (plotEvent.AoeConditions is { Count: > 0 })
        {
            filtered = filtered.Where(unit =>
            {
                foreach (var aoe in plotEvent.AoeConditions)
                {
                    if (!aoe.Condition.Check(
                            state.Caster,
                            state.CasterCaster,
                            unit,
                            state.TargetCaster,
                            state.SkillObject,
                            state.ActiveSkill))
                        return false;
                }

                return true;
            });
            Step("aoeConditions");
        }

        // plot_aoe_conditions (loaded on PlotEventTemplate.AoeConditions) gate Area / Random*
        // picks. Crime Aura 40917 event 35070 requires BuffTag 894 (현상수배); without this
        // filter every character in the 25m sphere is "wanted" → taunt, chip damage, and the
        // criminal bubble on a Friendly Nuia guard.
        if (plotEvent.AoeConditions is { Count: > 0 })
        {
            filtered = filtered.Where(unit =>
            {
                foreach (var aoe in plotEvent.AoeConditions)
                {
                    if (!aoe.Condition.Check(
                            state.Caster,
                            state.CasterCaster,
                            unit,
                            state.TargetCaster,
                            state.SkillObject,
                            state.ActiveSkill))
                        return false;
                }

                return true;
            });
        }

        return filtered;

        // Materialises only while the probe is on; otherwise the whole chain stays lazy.
        void Step(string name)
        {
            if (trace == null)
                return;
            var list = filtered.ToList();
            filtered = list;
            trace.Add((name, list.Count));
        }
    }
}
