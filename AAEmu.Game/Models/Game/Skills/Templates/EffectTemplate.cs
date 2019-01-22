using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public abstract class EffectTemplate
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger(); 
        
        public uint Id { get; set; }

        public virtual uint BuffId => Id;

        public abstract bool OnActionTime { get; }

        public abstract void Apply(Unit caster, SkillAction casterObj, BaseUnit target, SkillAction targetObj, CastAction castObj,
            Skill skill, DateTime time);

        public virtual void Start(Unit caster, BaseUnit owner, Effect effect)
        {
        }

        public virtual void TimeToTimeApply(Unit caster, BaseUnit owner, Effect effect)
        {
        }

        public virtual void Dispel(Unit caster, BaseUnit owner, Effect effect)
        {
        }

        public virtual int GetDuration()
        {
            return 0;
        }

        public virtual double GetTick()
        {
            return 0;
        }

        public virtual void WriteData(PacketStream stream)
        {
        }
    }
}