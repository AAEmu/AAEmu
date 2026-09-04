using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;
using NLog;

using WorldIntegration = AAEmu.Game.WorldIntegration;

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
    protected static Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
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
    /// <summary>Periodic tick index carried by the UnitState buff snapshot.</summary>
    public uint TickIndex { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Charge { get; set; }

    /// <summary>
    /// How many applications of a multiple-stack family this single instance represents.
    /// </summary>
    /// <remarks>
    /// One instance carries the whole stack because the client draws an icon per instance and reads the
    /// number on it from the stack field of the wire. The unit_modifiers of the template are multiplied
    /// by this, so a member at sixty stacks has the same total effect as sixty separate members had.
    /// </remarks>
    public int Stack { get; set; } = 1;
    public bool Passive { get; set; }
    /// <summary>
    /// World mirrors it for state and client presentation, but must not send the corresponding
    /// WZ create/update/remove messages back to the Zone.
    /// </summary>
    public bool ZoneAuthored { get; set; }

    /// <summary>
    /// Set when World actually sent WZBuffCreated. WZBuffRemoved must not go to Zone
    /// unless this is true — Zone Buff Destroy on a unit that never received Create
    /// can take the Zone process down instead of logging an invalid buff id.
    /// </summary>
    public bool RelayedToZone { get; set; }

    public uint AbLevel { get; set; }
    public BuffEvents Events { get; }
    public BuffTriggersHandler Triggers { get; }
    public Dictionary<uint, FactionsEnum> saveFactions { get; set; }

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
        saveFactions = [];
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
            EffectTaskManager.Instance.AddDispelTask(this, Tick);
        }
        else if (BuffStackRules.ShouldScheduleDispel(Duration, Tick))
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
                        EffectTaskManager.Instance.AddDispelTask(this, Tick);
                    }
                    else if (BuffStackRules.ShouldScheduleDispel(Duration, Tick))
                        EffectTaskManager.Instance.AddDispelTask(this, GetTimeLeft());

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
                            TickIndex++;
                            Template.TimeToTimeApply(Caster, Owner, this);
                            return;
                        }
                    }
                    else if (_count > 0)
                    {
                        _count--;
                        if (Template.OnActionTime && _count > 0)
                        {
                            TickIndex++;
                            Template.TimeToTimeApply(Caster, Owner, this);
                            return;
                        }
                    }

                    // Natural duration/tick expiry. remove_on_move / dispel / charge-zero call
                    // Exit() → Finishing without this path; those must not raise OnTimeout.
                    // Buff 31556 (질주 이동확인): Timeout dispel (tag 4154 → 2675) only when the
                    // 800ms check expires while standing still; move clears 31556 via
                    // RemoveOnMove and must leave dash 2675 intact.
                    State = EffectState.Finishing;
                    FinishBuff(replace, fireTimeout: true);
                    return;
                }
        }

        if (State == EffectState.Finishing)
        {
            FinishBuff(replace, fireTimeout: false);
        }
    }
    /// <summary>
    /// Takes one more application of a multiple-stack family into this instance.
    /// </summary>
    /// <param name="maxStack">The template ceiling; zero means the family does not stack.</param>
    /// <returns>Whether the application was absorbed, i.e. the ceiling had room.</returns>
    public bool TryGrowStack(int maxStack)
    {
        lock (_lock)
        {
            if (!BuffStackRules.CanGrow(Stack, maxStack))
                return false;

            Stack++;
        }

        // The bonuses of this index are scaled by the count, so the whole set is rebuilt for the new
        // one. Start clears the index before it writes, which is what makes re-running it safe.
        if (InUse)
            Template.Start(Caster, Owner, this);

        NotifyUpdated(reason: 1);
        return true;
    }

    public void OverwriteWith(Buff newBuff)
    {
        lock (_lock)
        {
            var remaining = GetTimeLeft();

            // Update buff properties from the new buff.
            this.Charge = newBuff.Charge;
            this.AbLevel = newBuff.AbLevel;
            this.Caster = newBuff.Caster;
            this.SkillCaster = newBuff.SkillCaster;
            this.ZoneAuthored = newBuff.ZoneAuthored;
            TickIndex = 0;

            // Set StartTime to now.
            var now = DateTime.UtcNow;
            StartTime = now;

            // Update Duration based on the stack rule:
            if (Template.StackRule == BuffStackRule.Extend)
            {
                // Extend: new Duration = remaining time (from old timer) + newBuff.Duration.
                Duration = newBuff.Duration + (int)remaining;
            }
            else
            {
                // Refresh: new Duration = newBuff.Duration.
                Duration = newBuff.Duration;
            }

            if (!BuffStackRules.ShouldScheduleDispel(Duration, Template.Tick))
            {
                // Permanent refresh: keep Acting. SetInUse(update) would queue a
                // -1 ms dispel and the instance would finish on the next tick.
                EndTime = DateTime.MinValue;
                InUse = true;
                State = EffectState.Acting;
            }
            else
            {
                EndTime = StartTime.AddMilliseconds(Duration);
                TaskManager.Instance.RemoveTasks(task =>
                {
                    if (task is DispelTask dt && dt.Effect.Target is Buff existing)
                        return existing == this;
                    return false;
                });
                SetInUse(true, true);
            }
        }

        NotifyUpdated(reason: 1); // refresh/overwrite
    }

    /// <summary>
    /// Applications this buff family currently represents on its owner, as every wire field that
    /// carries a "stack" expects it.
    /// </summary>
    /// <remarks>
    /// This has to be the same figure on Create as on Update. The zone recomputes attributes that scale
    /// with the count — a sail's contribution to hull speed among them — from whatever the last packet
    /// told it, so a Create that always claims one application leaves the simulation running on a single
    /// stack of a sixty-stack buff no matter what the client is showing.
    /// </remarks>
    public uint StackCount =>
        Owner?.Buffs == null ? 1u : (uint)Math.Max(1, Owner.Buffs.GetBuffCountById(Template.BuffId));

    /// <summary>
    /// Push SC + WZ BuffUpdated so clients and Zone see charge/duration changes after Create.
    /// </summary>
    public void NotifyUpdated(byte reason = 0)
    {
        if (Owner == null || Passive)
            return;

        var elapsedMs = StartTime == DateTime.MinValue
            ? 0
            : (int)Math.Max(0, (DateTime.UtcNow - StartTime).TotalMilliseconds);
        var stack = StackCount;

        Owner.BroadcastPacket(
            new SCBuffUpdatedPacket(Owner.ObjId, (int)Index, stack, (uint)Charge, elapsedMs, reason),
            true);

        if (WorldIntegration.ZoneAuthority && !ZoneAuthored)
            WorldIntegration.RelayBuffUpdatedToZone?.Invoke(
                Owner.ObjId, (int)Index, stack, (uint)Charge, elapsedMs, reason);
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

    private void FinishBuff(bool replace, bool fireTimeout)
    {
        State = EffectState.Finished;
        InUse = false;
        StopEffectTask(replace, fireTimeout);
    }

    private void StopEffectTask(bool replace, bool fireTimeout)
    {
        lock (_lock)
        {
            // Timeout triggers (buff_triggers.kind=timeout) fire only on natural expiry.
            // Early Exit (remove_on_move, purge, toggle-off, etc.) must not run them —
            // e.g. dash move-check 31556 Timeout → DispelEffect tag 4154 (질주 태그 / 2675).
            if (fireTimeout)
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
            FinishBuff(false, fireTimeout: false);
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
        stream.WritePisc(Charge, Duration / 10, 0, (long)(Template.Tick / 10));
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
        else
        {
            NotifyUpdated(reason: 2); // charge consumed
        }

        return value;
    }
}
