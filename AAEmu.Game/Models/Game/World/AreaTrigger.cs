using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.World;

public class AreaTrigger
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public AreaShape Shape { get; set; }
    public Doodad Owner { get; set; }
    public BaseUnit Caster { get; set; }

    /// <summary>
    /// Units currently inside the Shape
    /// </summary>
    private List<Unit> Units { get; set; } = [];
    private HashSet<Unit> TriggeredUnits { get; } = [];

    public uint SkillId { get; set; }
    public uint TlId { get; set; }

    /// <summary>Plot that spawned this hazard. Zero for a clout that is not a plot summon.</summary>
    public uint PlotId { get; set; }

    public uint PlotEventId { get; set; }

    /// <summary>
    /// A plot-summoned hazard reports its hit as that plot cast. A skill-timeline lookup misses once
    /// the cast bar belongs to the plot, and the client then drops both the number and the health change.
    /// </summary>
    public static CastAction CreateHitCast(uint plotId, ushort tlId, uint plotEventId, uint skillId) =>
        plotId != 0
            ? new CastPlot(plotId, tlId, plotEventId, skillId)
            : new CastSkill(skillId, tlId);
    public SkillTargetRelation TargetRelation { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
    public BuffTemplate InsideBuffTemplate { get; set; }
    public Dictionary<uint, List<EffectTemplate>> EffectsPerBuff { get; set; } = new();
    public int TickRate { get; set; }
    private DateTime _lastTick = DateTime.MinValue;
    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _tickLock = new();

    /// <summary>
    /// True while at least one unit still has this trigger's inside-buff. The owner region may
    /// already be idle (player teleported away); the manager must keep ticking until leave runs.
    /// </summary>
    public bool HasOccupants => TriggeredUnits.Count > 0;

    private void UpdateUnits()
    {
        if (Owner == null || Shape == null)
        {
            AreaTriggerManager.Instance.RemoveAreaTrigger(this);
            return;
        }

        // Get units currently in the shape
        var currentUnitsInShape = WorldManager.GetAroundByShape<Unit>(Owner, Shape) ?? [];

        // Check who left since last check
        var leftUnits = Units?.Where(oldU => currentUnitsInShape.All(newU => oldU.ObjId != newU.ObjId)) ?? [];
        // Check who's new in the shape
        var newUnits = currentUnitsInShape.Where(newU => Units == null || Units.All(oldU => newU.ObjId != oldU.ObjId));

        // Trigger events for new units
        foreach (var newUnit in newUnits)
        {
            OnEnter(newUnit);
            Logger.Debug($"AreaShape Enter {Shape.Id} {Shape.Type} with count {currentUnitsInShape.Count} unit in shape arround {Owner.Transform}");
        }

        // Trigger events for units that left
        foreach (var leftUnit in leftUnits)
        {
            OnLeave(leftUnit);
            Logger.Debug($"AreaShape Leave {Shape.Id} {Shape.Type} with count {currentUnitsInShape.Count} unit in shape arround {Owner.Transform}");
        }

        // Buff tags can change while a unit remains inside the shape. Re-evaluate the content
        // gates so a newly valid unit can enter and a newly excluded unit stops participating.
        foreach (var unit in currentUnitsInShape.Where(unit => Units.Any(old => old.ObjId == unit.ObjId)))
        {
            if (IsValidTarget(unit))
                OnEnter(unit);
            else
                OnLeave(unit);
        }

        // Save new units list
        Units = currentUnitsInShape;
    }

    private bool HasRegisteredEffects => EffectsPerBuff.Count > 0 && EffectsPerBuff.Values.Any(list => list is { Count: > 0 });

    private void OnEnter(Unit unit)
    {
        if (Caster == null || Owner == null || unit == null)
            return;

        if (!AreaTriggerCloutRules.TracksOccupantsWithoutBuff(InsideBuffTemplate != null, HasRegisteredEffects))
            return;

        if (!IsValidTarget(unit) || !TriggeredUnits.Add(unit))
            return;

        if (InsideBuffTemplate == null)
            return;

        unit.IncrementTriggerCount(InsideBuffTemplate.BuffId);
        if (unit.GetTriggerCount(InsideBuffTemplate.BuffId) == 1)
        {
            InsideBuffTemplate.Apply(Caster, new SkillCasterUnit(Caster.ObjId), unit, new SkillCastUnitTarget(unit.ObjId), null, new EffectSource(), null, DateTime.UtcNow);
        }
    }

    private void OnLeave(Unit unit)
    {
        if (unit == null || !TriggeredUnits.Remove(unit))
            return;

        if (InsideBuffTemplate == null)
            return;

        unit.DecrementTriggerCount(InsideBuffTemplate.BuffId);
        if (unit.GetTriggerCount(InsideBuffTemplate.BuffId) == 0)
            unit.Buffs.RemoveBuff(InsideBuffTemplate.BuffId);
    }

    public void OnDelete()
    {
        lock (_tickLock)
        {
            foreach (var unit in TriggeredUnits.ToList())
                OnLeave(unit);
        }
    }

    private bool IsValidTarget(Unit unit)
    {
        if (Caster == null || unit == null || !SkillTargetingUtil.IsRelationValid(TargetRelation, Caster, unit))
            return false;

        if (TargetBuffTagId > 0 && !unit.Buffs.CheckBuffTag(TargetBuffTagId))
            return false;

        return TargetNoBuffTagId == 0 || !unit.Buffs.CheckBuffTag(TargetNoBuffTagId);
    }

    private void ApplyEffects()
    {
        if (Caster == null)
            return;

        if (!AreaTriggerCloutRules.AppliesEffectsOnTick(InsideBuffTemplate != null, HasRegisteredEffects))
            return;

        foreach (var unit in TriggeredUnits)
        {
            foreach (var effect in EnumerateEffectsForTick())
            {
                if (effect == null)
                    continue;

                if (effect is BuffEffect buffEffect && unit.Buffs.CheckBuff(buffEffect.BuffId))
                    continue;

                CastAction castAction;
                if (InsideBuffTemplate != null)
                {
                    var eff = unit.Buffs.GetEffectFromBuffId(InsideBuffTemplate.BuffId);
                    castAction = eff != null
                        ? new CastBuff(eff)
                        : CreateHitCast(PlotId, (ushort)TlId, PlotEventId, SkillId);
                }
                else
                {
                    castAction = CreateHitCast(PlotId, (ushort)TlId, PlotEventId, SkillId);
                }

                effect.Apply(Caster, new SkillCasterUnit(Caster.ObjId), unit, new SkillCastUnitTarget(unit.ObjId),
                    castAction, CreateEffectSource(), new SkillObject(), DateTime.UtcNow);
            }
        }
    }

    private IEnumerable<EffectTemplate> EnumerateEffectsForTick()
    {
        if (InsideBuffTemplate != null
            && EffectsPerBuff.TryGetValue(InsideBuffTemplate.BuffId, out var keyed)
            && keyed != null)
        {
            return keyed;
        }

        // Buff-less clouts key their effect list under buff_id 0 (or any single entry).
        return EffectsPerBuff.Values.SelectMany(list => list ?? []);
    }

    private EffectSource CreateEffectSource()
    {
        if (SkillId == 0 || Caster is not Unit unitCaster)
            return new EffectSource();

        var template = SkillManager.Instance.GetSkillTemplate(SkillId);
        return template == null
            ? new EffectSource()
            : new EffectSource(new Skill(template, unitCaster));
    }

    // Called every 50ms
    public void Tick(TimeSpan delta)
    {
        lock (_tickLock)
        {
            // Initial-phase clouts arm before Spawn. Do not start the effect clock (or search)
            // until the owner is visible — otherwise _lastTick advances on an empty pulse and a
            // short clout can expire before the first real hit.
            if (Owner != null && AreaTriggerCloutRules.WaitUntilOwnerVisible(Owner.IsVisible))
                return;

            UpdateUnits();
            if (TickRate > 0)
            {
                if ((DateTime.UtcNow - _lastTick).TotalMilliseconds > TickRate)
                {
                    ApplyEffects();
                    _lastTick = DateTime.UtcNow;
                }
            }
        }
    }
}
