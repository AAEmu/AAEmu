using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

public enum EffectState
{
    Created,
    Acting,
    Finishing,
    Finished
}

public class Buff
{
    protected static Logger Logger => LogManager.GetCurrentClassLogger();

    private object _lock = new();
    private int _count;

    public uint Index { get; set; }
    public Skill Skill { get; set; }
    // public EffectTemplate Template { get; set; }
    public BuffTemplate Template { get; set; }
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
    public ushort AbLevel { get; set; } // int in 1.2, ushort in 3+
    public BuffEvents Events { get; }
    public BuffTriggersHandler Triggers { get; }
    public Dictionary<uint, FactionsEnum> saveFactions { get; set; }
    public int Stack { get; set; } = 1; // добавил для учета стака баффов в пакете SCBuffCreatedPacket

    public Buff(IBaseUnit owner, IBaseUnit caster, SkillCaster skillCaster, BuffTemplate template, Skill skill, DateTime time)
    {
        Owner = (BaseUnit)owner;
        Caster = caster as Unit;
        SkillCaster = skillCaster;
        Template = template;
        Skill = skill;
        StartTime = time;
        EndTime = DateTime.MinValue;
        AbLevel = 1;
        Events = new BuffEvents();
        Triggers = new BuffTriggersHandler(this);
        saveFactions = new();
    }

    public void UpdateEffect()
    {
        Template.Start(Caster, Owner, this);
        if (Duration == 0)
            Duration = Template.GetDuration(AbLevel);
        if (StartTime == DateTime.MinValue)
        {
            StartTime = DateTime.UtcNow;
            EndTime = StartTime.AddMilliseconds(Duration);
        }

        Tick = Template.GetTick();

        if (Tick > 0)
        {
            var time = GetTimeLeft();
            if (time > 0)
                _count = (int)(time / Tick + 0.5f + 1);
            else
                _count = -1;
            EffectTaskManager.AddDispelTask(this, Tick);
        }
        else
            EffectTaskManager.AddDispelTask(this, GetTimeLeft());
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
                        Duration = Template.GetDuration(AbLevel);
                    if (StartTime == DateTime.MinValue)
                    {
                        StartTime = DateTime.UtcNow;
                        EndTime = StartTime.AddMilliseconds(Duration);
                    }

                    Tick = Template.GetTick();

                    if (Tick > 0)
                    {
                        var time = GetTimeLeft();
                        if (time > 0)
                            _count = (int)(time / Tick + 0.5f + 1);
                        else
                            _count = -1;
                        EffectTaskManager.AddDispelTask(this, Tick);
                    }
                    else
                        EffectTaskManager.AddDispelTask(this, GetTimeLeft());

                    if (Template.FactionId > 0 && Owner is Unit owner)
                    {
                        Logger.Info($"Buff: buff={Template.BuffId}:{Index}, owner={owner.TemplateId}:{owner.ObjId}");
                        owner.SetFaction(Template.FactionId);
                    }
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

                    //Buff seems to come to natural expiration here
                    //Events.OnTimeout(this, new OnTimeoutArgs());
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
            Events.OnTimeout(this, new OnTimeoutArgs());
            Triggers.UnsubscribeEvents();
            Owner.Buffs.RemoveEffect(this);
            Template.Dispel(Caster, Owner, this, replace);

            if (Template.FactionId > 0 && Owner is NPChar.Npc npc)
            {
                npc.SetFaction(npc.Template.FactionId);
            }
            else if (Template.FactionId > 0 && Owner is Unit owner)
            {
                owner.SetFaction(saveFactions[owner.Id]);
                saveFactions.Remove(owner.Id);
            }
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
        {
            State = EffectState.Finishing;
            StopEffectTask(false);
        }
    }

    public bool IsEnded()
    {
        return State == EffectState.Finished || State == EffectState.Finishing;
    }

    public double GetTimeLeft()
    {
        if (Duration == 0)
            return -1;
        var time = (long)(StartTime.AddMilliseconds(Duration) - DateTime.UtcNow).TotalMilliseconds;
        return time > 0 ? time : 0;
    }

    public uint GetTimeElapsed()
    {
        var time = (uint)(DateTime.UtcNow - StartTime).TotalMilliseconds;
        return time > 0 ? time : 0;
    }

    public void WriteData(PacketStream stream)
    {
        stream.WritePisc(Charge, Duration / 10, 0 / 10, (long)(Template.Tick / 10));
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
