using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
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
    }

    public class AreaTrigger
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public AreaShape Shape { get; set; }
        public Doodad Owner { get; set; }
        public Unit Caster { get; set; }
        private List<Unit> Units { get; set; }
        
        public BuffTemplate InsideBuffTemplate { get; set; }
        public List<EffectTemplate> EffectPerTick { get; set; }
        public uint TickRate { get; set; }

        public AreaTrigger()
        {
            Units = new List<Unit>();
        }
        
        public void UpdateUnits()
        {
            var units = WorldManager.Instance.GetAroundByShape<Unit>(Owner, Shape);

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
            InsideBuffTemplate?.Apply(Caster, new SkillCasterUnit(Caster.ObjId), unit, new SkillCastUnitTarget(unit.ObjId), null, new EffectSource(), null, DateTime.Now);
            // unit.Effects.AddEffect(new Effect(Owner, Caster, new SkillCasterUnit(Caster.ObjId), InsideBuffTemplate, null, DateTime.Now));
            
        }

        public void OnLeave(Unit unit)
        {
            if (InsideBuffTemplate != null)
                unit.Effects.RemoveBuff(InsideBuffTemplate.BuffId);
        }

        public void OnDelete()
        {
            foreach (var unit in Units.Where(unit => InsideBuffTemplate != null))
            {
                unit.Effects.RemoveEffect(InsideBuffTemplate.Id);
            }
        }

        public void Tick()
        {
            
        }
    }
}
