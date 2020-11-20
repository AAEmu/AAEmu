using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public enum EffectState
    {
        Created,
        Acting,
        Finishing,
        Finished
    }

    public class Effect
    {
        private object _lock = new object();
        private int _count;

        public uint Index { get; set; }
        public Skill Skill { get; set; }
        public EffectTemplate Template { get; set; }
        public Unit Caster { get; set; }
        public SkillCaster SkillCaster { get; set; }
        public BaseUnit Owner { get; set; }
        public EffectState State { get; set; }
        public bool InUse { get; set; }
        public int Duration { get; set; }
        public double Tick { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Charge { get; set; }
        public bool Passive { get; set; }
        public uint AbLevel { get; set; }
        public BuffEvents Events { get;}
        public BuffTriggersHandler Triggers { get;}

        public Effect(BaseUnit owner, Unit caster, SkillCaster skillCaster, EffectTemplate template, Skill skill, DateTime time)
        {
            Owner = owner;
            Caster = caster;
            SkillCaster = skillCaster;
            Template = template;
            Skill = skill;
            StartTime = time;
            EndTime = DateTime.MinValue;
            AbLevel = 1;
            Events = new BuffEvents();
            Triggers = new BuffTriggersHandler(this);
        }

        public void UpdateEffect()
        {
            Template.Start(Caster, Owner, this);
            if (Duration == 0)
                Duration = Template.GetDuration();
            if (StartTime == DateTime.MinValue)
            {
                StartTime = DateTime.Now;
                EndTime = StartTime.AddMilliseconds(Duration);
            }

            Tick = Template.GetTick();

            if (Tick > 0)
            {
                var time = GetTimeLeft();
                if (time > 0)
                    _count = (int) (time / Tick + 0.5f + 1);
                else
                    _count = -1;
                EffectTaskManager.Instance.AddDispelTask(this, Tick);
            }
            else
                EffectTaskManager.Instance.AddDispelTask(this, GetTimeLeft());
        }

        public void ScheduleEffect(bool replace)
        {
            switch (State)
            {
                case EffectState.Created:
                {
                    State = EffectState.Acting;

                    Template.Start(Caster, Owner, this);

                    if (Duration == 0)
                        Duration = Template.GetDuration();
                    if (StartTime == DateTime.MinValue)
                    {
                        StartTime = DateTime.Now;
                        EndTime = StartTime.AddMilliseconds(Duration);
                    }

                    Tick = Template.GetTick();

                    if (Tick > 0)
                    {
                        var time = GetTimeLeft();
                        if (time > 0)
                            _count = (int) (time / Tick + 0.5f + 1);
                        else
                            _count = -1;
                        EffectTaskManager.Instance.AddDispelTask(this, Tick);
                    }
                    else
                        EffectTaskManager.Instance.AddDispelTask(this, GetTimeLeft());

                    return;
                }
                case EffectState.Acting:
                {
                    if (_count == -1)
                    {
                        if (Template.OnActionTime)
                        {
                            Template.TimeToTimeApply(Caster, Owner, this);
                            return;
                        }
                    }
                    else if (_count > 0)
                    {
                        _count--;
                        if (Template.OnActionTime && _count > 0)
                        {
                            Template.TimeToTimeApply(Caster, Owner, this);
                            return;
                        }
                    }

                    State = EffectState.Finishing;
                    break;
                }
            }

            if (State == EffectState.Finishing)
            {
                State = EffectState.Finished;
                InUse = false;
                StopEffectTask(replace);
            }
        }

        public void Exit(bool replace = false)
        {
            if (State == EffectState.Finished)
                return;
            if (State != EffectState.Created)
            {
                State = EffectState.Finishing;
                ScheduleEffect(replace);
            }
            else
                State = EffectState.Finishing;
        }

        private void StopEffectTask(bool replace)
        {
            lock (_lock)
            {
                Owner.Effects.RemoveEffect(this);
                Template.Dispel(Caster, Owner, this, replace);
            }
        }

        public void SetInUse(bool inUse, bool update)
        {
            InUse = inUse;
            if (update)
                UpdateEffect();
            else if (inUse)
                ScheduleEffect(false);
            else if (State != EffectState.Finished)
                State = EffectState.Finishing;
        }

        public bool IsEnded()
        {
            return State == EffectState.Finished || State == EffectState.Finishing;
        }

        public double GetTimeLeft()
        {
            if (Duration == 0)
                return -1;
            var time = (long) (StartTime.AddMilliseconds(Duration) - DateTime.Now).TotalMilliseconds;
            return time > 0 ? time : 0;
        }

        public uint GetTimeElapsed()
        {
            var time = (uint) (DateTime.Now - StartTime).TotalMilliseconds;
            return time > 0 ? time : 0;
        }

        public void WriteData(PacketStream stream)
        {
            switch (Template)
            {
                case BuffEffect buffEffect:
                    stream.WritePisc(Charge, buffEffect.Buff.Duration / 10, 0, (long)(buffEffect.Buff.Tick / 10));
                    break;
                case BuffTemplate buffTemplate:
                    stream.WritePisc(Charge, buffTemplate.Duration / 10, 0, (long)(buffTemplate.Tick / 10));
                    break;
                default:
                    Template.WriteData(stream);
                    break;
            }
        }
        
        /// <summary>
        /// Consumes as much charge as possible. Remainder is returned
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public int ConsumeCharge(int value)
        {
            var newCharge = Math.Max(0, Charge - value);
            value = Math.Max(0, value - Charge);
            Charge = newCharge;

            if (Charge <= 0)
            {
                Exit(false);
            }
            
            return value;
        }
    }
}
