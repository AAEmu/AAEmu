using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.World
{
    public enum AreaShapeType
    {
        Sphere = 1,
        Cuboid = 2
    }
    public class AreaShape
    {
        public uint Id { get; set; }
        public AreaShapeType Type { get; set; }
        public float Value1 { get; set; }
        public float Value2 { get; set; }
        public float Value3 { get; set; }

        public List<T> ComputeCuboid<T>(GameObject origin, List<T> toCheck) where T : GameObject
        {
            // Z check
            var zOffset = Value3;
            toCheck = toCheck.Where(o => (o.Transform.World.Position.Z >= origin.Transform.World.Position.Z - zOffset) && (o.Transform.World.Position.Z <= origin.Transform.World.Position.Z + zOffset)).ToList();
            if (toCheck.Count == 0)
                return toCheck;

            // Triangle check
            var vertices = MathUtil.GetCuboidVertices(Value1, Value2,
                origin.Transform.World.Position.X, origin.Transform.World.Position.Y,
                //origin.Transform.World.ToRollPitchYawSBytes().Item3);
                origin.Transform.World.Rotation.Z);

            toCheck = toCheck.Where(o =>
            {
                var tri1 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[0], vertices[1],
                    vertices[2]);

                var tri2 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[1], vertices[2],
                    vertices[3]);

                return tri1 || tri2;
            }).ToList();

            return toCheck;
        }
    }

    public class AreaTrigger
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public AreaShape Shape { get; set; }
        public Doodad Owner { get; set; }
        public Unit Caster { get; set; }
        private List<Unit> Units { get; set; }


        public uint SkillId { get; set; }
        public uint TlId { get; set; }
        public SkillTargetRelation TargetRelation { get; set; }
        public BuffTemplate InsideBuffTemplate { get; set; }
        public List<EffectTemplate> EffectPerTick { get; set; }
        public int TickRate { get; set; }
        private DateTime _lastTick = DateTime.MinValue;

        public AreaTrigger()
        {
            Units = new List<Unit>();
        }

        public void UpdateUnits()
        {
            if (Owner == null || !Owner.IsVisible)
            {
                AreaTriggerManager.Instance.RemoveAreaTrigger(this);
                return;
            }

            var units = WorldManager.Instance.GetAroundByShape<Unit>(Owner, Shape);
            if (units is { Count: 0 })
            {
                _log.Warn("AreaShape with no size values was remove");
                AreaTriggerManager.Instance.RemoveAreaTrigger(this);
                return;
            }

            var leftUnits = Units.Where(u => units.All(u2 => u.ObjId != u2.ObjId));
            var newUnits = units.Where(u => Units.All(u2 => u.ObjId != u2.ObjId));

            foreach (var newUnit in newUnits)
            {
                OnEnter(newUnit);
            }

            foreach (var leftUnit in leftUnits)
            {
                OnLeave(leftUnit);
            }

            Units = units;
        }

        public void OnEnter(Unit unit)
        {
            if (Caster == null)
                return;

            if (SkillTargetingUtil.IsRelationValid(TargetRelation, Caster, unit))
                InsideBuffTemplate?.Apply(Caster, new SkillCasterUnit(Caster.ObjId), unit, new SkillCastUnitTarget(unit.ObjId), null, new EffectSource(), null, DateTime.UtcNow);
            // unit.Effects.AddEffect(new Effect(Owner, Caster, new SkillCasterUnit(Caster.ObjId), InsideBuffTemplate, null, DateTime.UtcNow));
        }

        public void OnLeave(Unit unit)
        {
            if (InsideBuffTemplate != null)
                unit.Buffs.RemoveBuff(InsideBuffTemplate.BuffId);
        }

        public void OnDelete()
        {
            if (InsideBuffTemplate != null)
            {
                foreach (var unit in Units)
                {
                    unit.Buffs.RemoveBuff(InsideBuffTemplate.BuffId);
                }
            }
        }

        public void ApplyEffects()
        {
            if (InsideBuffTemplate == null)
                return;
            if (Caster == null)
                return;

            var unitsToApply = SkillTargetingUtil.FilterWithRelation(TargetRelation, Caster, Units);
            foreach (var unit in unitsToApply)
            {
                foreach (var effect in EffectPerTick)
                {
                    if (effect is BuffEffect buffEffect && unit.Buffs.CheckBuff(buffEffect.BuffId))
                        continue;
                    var eff = unit.Buffs.GetEffectFromBuffId(InsideBuffTemplate.BuffId);
                    CastAction castAction = null;
                    if (eff != null)
                        castAction = new CastBuff(eff);
                    else
                        castAction = new CastSkill(SkillId, 0);

                    effect.Apply(Caster, new SkillCasterUnit(Caster.ObjId), unit, new SkillCastUnitTarget(unit.ObjId), castAction, new EffectSource(), new SkillObject(), DateTime.UtcNow);
                }
            }
        }

        // Called every 50ms
        public void Tick(TimeSpan delta)
        {
            UpdateUnits();
            if (TickRate > 0)
                if ((DateTime.UtcNow - _lastTick).TotalMilliseconds > TickRate)
                {
                    ApplyEffects();
                    _lastTick = DateTime.UtcNow;
                }
        }
    }
}
