using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
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
    private bool _stopRan;
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
    /// Where the owner stood when this instance was applied, for the <c>save_pos</c> families.
    /// </summary>
    /// <remarks>
    /// Written by <see cref="Units.Buffs.AddBuff"/> and read back by the move_to_saved_pos special
    /// effect; null for every buff whose template does not carry <c>save_pos</c>.
    /// </remarks>
    public SavedPosition? SavedPosition { get; set; }

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

    /// <summary>
    /// One unit this aura is holding its slave buff on, and the instance it applied.
    /// </summary>
    public readonly record struct AuraRecipient(Unit Unit, uint Index);

    /// <summary>
    /// The units this aura currently holds its <c>aura_slave_buff_id</c> on, keyed by object id.
    /// </summary>
    /// <remarks>
    /// The applied index is kept so a unit that walks out of the radius loses the instance this aura gave
    /// it and not another caster's copy of the same family. <see cref="AuraRecipientsLock"/> guards the
    /// map: the pulse writes it from the task thread, and the buff's own end path drains it.
    /// </remarks>
    public Dictionary<uint, AuraRecipient> AuraRecipients { get; } = [];

    public object AuraRecipientsLock { get; } = new();

    private bool _auraScheduled;

    /// <summary>
    /// Claims the right to schedule this buff's aura pulse, once per instance.
    /// </summary>
    /// <remarks>
    /// <c>BuffTemplate.Start</c> runs again on every refresh and on every stack growth, so the claim has
    /// to be made here rather than at the call site, or a sixty-stack bard song would carry sixty pulses.
    /// </remarks>
    public bool TryClaimAuraPulse()
    {
        lock (AuraRecipientsLock)
        {
            if (_auraScheduled)
                return false;

            _auraScheduled = true;
            return true;
        }
    }

    /// <summary>
    /// One pass of an aura: apply <c>aura_slave_buff_id</c> to the units now inside
    /// <c>aura_radius</c> that match <c>aura_relation_id</c>, and take it back from the ones that left.
    /// </summary>
    /// <remarks>
    /// Eligibility is decided by <see cref="AuraRules"/>; this method only supplies the live units. The
    /// nearest eligible units win the <c>aura_max_count</c> slots, so a ceiling that bites always keeps
    /// the same set instead of however the region happens to enumerate.
    /// </remarks>
    public void PulseAura()
    {
        var template = Template;
        var owner = Owner;
        var caster = Caster;
        if (template == null || owner == null || caster == null)
            return;

        var slaveTemplate = SkillManager.Instance.GetBuffTemplate(template.AuraSlaveBuffId);
        if (slaveTemplate == null)
            return;

        var around = WorldManager.GetAround<Unit>(owner, template.AuraRadius) ?? [];
        // The source carries its own aura unless a relation excludes it; the area ticks add their owner
        // the same way, because a region query is not guaranteed to return the unit at its centre.
        if (owner is Unit ownerUnit && !around.Contains(ownerUnit))
            around.Add(ownerUnit);

        var eligible = new List<Unit>();
        foreach (var candidate in around)
        {
            if (candidate?.Buffs == null || candidate.ObjId == 0 || candidate.Hp <= 0)
                continue;
            if (!AuraRules.InRadius(template.AuraRadius, owner.GetDistanceTo(candidate)))
                continue;

            var isCreator = candidate.ObjId == caster.ObjId;
            var isOwned = isCreator || candidate.GetOwnerCharacter()?.ObjId == caster.ObjId;
            var relationMatches = SkillTargetingUtil.IsRelationValid(
                (SkillTargetRelation)template.AuraRelationId, caster, candidate);

            if (AuraRules.AllowsRecipient(
                    template.AuraCreatorOnly, template.AuraChildOnly, isCreator, isOwned, relationMatches))
                eligible.Add(candidate);
        }

        eligible.Sort((a, b) => owner.GetDistanceTo(a).CompareTo(owner.GetDistanceTo(b)));

        var roster = new HashSet<uint>();
        foreach (var candidate in eligible)
        {
            if (!AuraRules.HasRoom(template.AuraMaxCount, roster.Count))
                break;
            roster.Add(candidate.ObjId);
        }

        // A unit that left the radius, or lost its slot to a nearer one, gives the buff back and is
        // forgotten — otherwise it would never be offered the aura again if it walked back in.
        List<KeyValuePair<uint, AuraRecipient>> leaving = null;
        lock (AuraRecipientsLock)
        {
            foreach (var entry in AuraRecipients.Where(held => !roster.Contains(held.Key)).ToList())
            {
                (leaving ??= []).Add(entry);
                AuraRecipients.Remove(entry.Key);
            }
        }

        if (leaving != null)
            foreach (var entry in leaving)
                ReleaseAuraRecipient(entry.Key, entry.Value);

        foreach (var candidate in eligible)
        {
            if (!roster.Contains(candidate.ObjId))
                continue;

            lock (AuraRecipientsLock)
            {
                if (AuraRecipients.ContainsKey(candidate.ObjId))
                    continue;
            }

            var slave = new Buff(candidate, caster, SkillCaster, slaveTemplate, null, DateTime.UtcNow);
            candidate.Buffs.AddBuff(slave);

            // A refused application — an immunity, a missing require-tag, a tolerance ladder's immune step
            // — leaves the instance out of the owner's list. Only what actually landed is tracked, so an
            // untracked unit is offered the aura again on the next pulse.
            if (!slave.InUse)
                continue;

            lock (AuraRecipientsLock)
            {
                AuraRecipients[candidate.ObjId] = new AuraRecipient(candidate, slave.Index);
            }
        }
    }

    /// <summary>
    /// Ends the aura: every unit still holding the slave buff this aura applied gives it back.
    /// </summary>
    public void ReleaseAura()
    {
        List<KeyValuePair<uint, AuraRecipient>> held;
        lock (AuraRecipientsLock)
        {
            if (AuraRecipients.Count == 0)
                return;

            held = AuraRecipients.ToList();
            AuraRecipients.Clear();
        }

        foreach (var entry in held)
            ReleaseAuraRecipient(entry.Key, entry.Value);
    }

    private static void ReleaseAuraRecipient(uint objId, AuraRecipient recipient)
    {
        var unit = recipient.Unit;
        if (unit?.Buffs == null)
            return;

        var live = unit.Buffs.GetEffectByIndex(recipient.Index);
        if (live == null)
            return;

        Logger.Debug("Aura {0} releases buff {1} (index {2}) from {3}",
            objId, live.Template?.BuffId ?? 0, recipient.Index, objId);
        unit.Buffs.RemoveEffect(recipient.Index);
    }

    public uint AbLevel { get; set; }
    public BuffEvents Events { get; }
    public BuffTriggersHandler Triggers { get; }
    public Dictionary<uint, FactionsEnum> saveFactions { get; set; }

    /// <summary>
    /// Publishes this buff's taunt (<c>buffs.taunt</c> / <c>taunt_with_top_aggro</c>): its owner is an NPC
    /// and it now attacks the unit that applied the buff.
    /// </summary>
    /// <remarks>
    /// Under zone authority the zone owns NPC AI, so World only asks: <c>WZTargetChanged</c> with
    /// <c>forceByWorld</c> is the native forced-target call, and <c>WZUpdateAggro</c> is how a threat entry
    /// is published. The same pair is what the 49554 Taunt skill's <c>change_target</c> special effect
    /// uses. Standalone the mirror is the authority and is moved directly.
    /// </remarks>
    public void ApplyTaunt()
    {
        if (Owner is not Npc npc || Caster is not Unit caster || caster.ObjId == npc.ObjId || npc.ObjId == 0)
            return;

        var template = Template;
        var topAggro = TauntRules.GrantsTopAggro(template.Taunt, template.TauntWithTopAggro);

        long aggro = 0;
        if (topAggro)
        {
            // Only the mirror is read here, and only to find the entry to beat: the zone's table is the
            // authority and it applies this value to its own copy.
            var highest = npc.AggroTable.IsEmpty
                ? 0L
                : npc.AggroTable.Values.Max(entry => (long)entry.TotalAggro);
            var own = npc.AggroTable.TryGetValue(caster.ObjId, out var mine) ? mine.TotalAggro : 0;
            aggro = TauntRules.TopAggroValue(highest, own);
        }

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayTargetChangedToZone?.Invoke(npc.ObjId, caster.ObjId, true);

            if (topAggro)
                WorldIntegration.PublishAggro(npc, caster, TauntRules.PublishableAggro(aggro), new CastBuff(this));

            Logger.Debug("Taunt buff {0} forces npc {1} onto {2} (topAggro={3} aggro={4})",
                template.BuffId, npc.ObjId, caster.ObjId, topAggro, aggro);
            return;
        }

        if (topAggro)
            npc.AddUnitAggro(AggroKind.Etc, caster, (int)Math.Min(TauntRules.PublishableAggro(aggro), int.MaxValue));

        npc.CurrentTarget = caster;
        npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, caster.ObjId), true);
    }

    /// <summary>
    /// Hands the NPC back when a plain taunt ends. A taunt that also granted top threat leaves the caster
    /// on top, so the zone's own pick is already right and nothing is published for it.
    /// </summary>
    public void ReleaseTaunt()
    {
        if (Owner is not Npc npc || Caster is not Unit caster || npc.ObjId == 0)
            return;

        var template = Template;
        if (!TauntRules.ForcesTarget(template.Taunt, template.TauntWithTopAggro)
            || TauntRules.GrantsTopAggro(template.Taunt, template.TauntWithTopAggro))
            return;

        var topAggroId = npc.AggroTable.GetTopTotalAggroAbuserObjId();
        var currentTargetId = (npc.CurrentTarget as Unit)?.ObjId ?? 0;
        if (TauntRules.ReleaseTarget(caster.ObjId, currentTargetId, topAggroId) is not { } release)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayTargetChangedToZone?.Invoke(npc.ObjId, release, false);
            return;
        }

        npc.CurrentTarget = npc.ParentWorld?.GetGameObject(release) as Unit;
        npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, release), true);
    }

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

            // The instance survives but the caster may not be the one it was, so the death it listens for
            // moves with it.
            SyncSourceDeathSubscription();

            // Set StartTime to now.
            var now = DateTime.UtcNow;
            StartTime = now;

            // Update Duration based on the stack rule:
            if (Template.StackRule == BuffStackRule.Extend)
            {
                // Extend: new Duration = remaining time (from old timer) + newBuff.Duration. GetTimeLeft()
                // answers -1 for a permanent instance, so the sum goes through the rule, which floors that
                // sentinel at zero instead of shaving a millisecond off the incoming duration.
                Duration = BuffStackRules.ExtendedDuration(newBuff.Duration, remaining);
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
    /// <para>
    /// Which figure that is depends on the rule: a family that lives as one instance reports the family
    /// total, while an instance that belongs to one caster (Independent, Multiple, MultipleDecreaseOne)
    /// reports the applications it represents itself, or every icon of the family would print the other
    /// instances' stacks.
    /// </para>
    /// </remarks>
    public uint StackCount =>
        BuffStackRules.WireStack(
            Template.StackRule,
            Stack,
            Owner?.Buffs?.GetBuffCountById(Template.BuffId) ?? Math.Max(1, Stack));

    /// <summary>
    /// <see cref="BuffStackRule.ChargeExtend"/>: takes the summed charge of an incoming application,
    /// held at the ceiling by the caller.
    /// </summary>
    public void AddCharge(int charge)
    {
        lock (_lock)
        {
            Charge = Math.Max(0, charge);
        }

        NotifyUpdated(reason: 2); // charge changed, the same code ConsumeCharge sends
    }

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

    // The caster whose death ends this buff (remove_on_source_dead), and the handler hanging on it.
    private Unit _sourceDeathSubscriber;
    private EventHandler<OnDeathArgs> _sourceDeathHandler;

    /// <summary>
    /// <c>remove_on_source_dead</c> (749 buffs): the buff ends when the unit that applied it dies.
    /// </summary>
    /// <remarks>
    /// The instance lives on the target while the unit that dies is usually somewhere else — a bard's
    /// song on a party member, a totem's blessing, a pet's aura — so there is nothing on the dying unit's
    /// own buff list to look at. Each instance carrying the flag therefore hangs a handler on its caster's
    /// <c>OnDeath</c>, which <c>Unit.DoDie</c> raises once per death and which <c>Slave.DoDie</c> and
    /// <c>Npc.DoDie</c> reach as well, and drops it again when the buff ends or its caster changes. This
    /// is the shape the Death buff trigger already subscribes with. A source that is not a unit (a doodad
    /// or an item cast) has no death to wait for and keeps its duration.
    /// </remarks>
    internal void SyncSourceDeathSubscription()
    {
        if (ReferenceEquals(_sourceDeathSubscriber, Caster))
            return;

        UnsubscribeSourceDeath();

        if (Template?.RemoveOnSourceDead != true || Caster == null)
            return;

        _sourceDeathSubscriber = Caster;
        _sourceDeathHandler = (_, args) => OnSourceDied(args);
        Caster.Events.OnDeath += _sourceDeathHandler;
    }

    private void UnsubscribeSourceDeath()
    {
        if (_sourceDeathSubscriber == null || _sourceDeathHandler == null)
            return;

        _sourceDeathSubscriber.Events.OnDeath -= _sourceDeathHandler;
        _sourceDeathSubscriber = null;
        _sourceDeathHandler = null;
    }

    private void OnSourceDied(OnDeathArgs args)
    {
        // The decision is the rule's; the handler only supplies the dead unit's id and this instance's
        // caster, so the same flag read by Buffs.TriggerRemoveOn and by the subscription agree.
        if (BuffRemoveOnRules.Matches(BuffRemoveOn.SourceDead, Template, args?.Victim?.ObjId ?? 0,
                Caster?.ObjId ?? 0))
            Exit();
    }

    private void FinishBuff(bool replace, bool fireTimeout)
    {
        State = EffectState.Finished;
        InUse = false;
        StopEffectTask(replace, fireTimeout);
    }

    /// <summary>
    /// Ends this buff through its natural-timeout path: the Timeout triggers run and the buff is
    /// removed, exactly as if its duration had elapsed. A seat ride uses this - the seat buff's Timeout
    /// trigger is what carries the rider (skills.id 40228 '층간 이동' applies it, its trigger casts the
    /// ride skill), and the ride happens when the rider leaves the seat, long before the buff's own
    /// duration ends. Ordinary early removals must not use it - see <see cref="StopEffectTask"/>.
    /// </summary>
    public void TimeOut()
    {
        if (State == EffectState.Finished)
            return;

        StopEffectTask(replace: false, fireTimeout: true);
    }

    private void StopEffectTask(bool replace, bool fireTimeout)
    {
        lock (_lock)
        {
            // A forced timeout (seat release) and the buff's own scheduled expiry can arrive together:
            // the first one here ends the buff, the second must not run the triggers or dispel it again
            // (that would send a duplicate removal to the client and relay it to the zone twice).
            if (_stopRan)
                return;
            _stopRan = true;

            // Exactly one of the two lifecycle triggers runs, and which one is the answer to "how did this
            // buff end". Natural expiry (duration or tick ran out) is the Timeout kind; every other way it
            // ends - purged by a dispel, removed by a remove_on_* flag, toggled off, charge exhausted, death
            // cleanup - is the Dispelled kind. The handler no longer raises OnDispelled by itself, which is
            // what used to make a dispelled trigger fire on expiry as well.
            if (fireTimeout)
                Events.OnTimeout(this, new OnTimeoutArgs());
            else
                Events.OnDispelled(this, new OnDispelledArgs());
            Triggers.UnsubscribeEvents();
            UnsubscribeSourceDeath();
            // An aura hands its slave buff back before the instance itself goes, so a dispel, a duration
            // expiry and a death cleanup all release the recipients the same way.
            ReleaseAura();
            // A plain taunt ends with the NPC's target: a dispelled 도발 must not pin the mob for the rest
            // of the encounter. The top-aggro variant is left alone; it keeps the caster on top by design.
            ReleaseTaunt();
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
        var absorbed = Math.Min(Math.Max(0, Charge), Math.Max(0, value));
        var newCharge = Math.Max(0, Charge - value);
        value = Math.Max(0, value - Charge);
        Charge = newCharge;

        if (Charge <= 0)
        {
            // The last point of the shield has just been spent: a buff whose absorption is consumed
            // runs its `absorption` triggers here, before Exit() unsubscribes them.
            if (absorbed > 0)
                Events.OnAbsorptionConsumed(this, new OnAbsorptionConsumedArgs { Amount = absorbed });
            Exit(false);
        }
        else
        {
            NotifyUpdated(reason: 2); // charge consumed
        }

        return value;
    }

    /// <summary>
    /// Takes one hit into this buff's absorption, as its own <c>damage_absorption_type_id</c> and
    /// <c>damage_absorption_per_hit</c> describe it, and returns the damage that is still owed.
    /// </summary>
    /// <remarks>
    /// This is what the damage path calls; <see cref="ConsumeCharge"/> is the raw "spend the pool" step it
    /// used to be, and the two differ wherever the authored shape is not a plain pool — a count shield
    /// spends a hit rather than the damage it absorbed, and a type-0 shield with a per-hit ceiling holds no
    /// charge to spend at all. <see cref="AbsorptionRules.Apply"/> decides.
    /// </remarks>
    public int AbsorbDamage(int value)
    {
        var outcome = AbsorptionRules.Apply(
            Template?.DamageAbsorptionTypeId ?? 0,
            Template?.DamageAbsorptionPerHit ?? 0,
            Charge,
            value);

        var changed = outcome.Charge != Charge;
        Charge = outcome.Charge;

        if (outcome.Consumed)
        {
            // Same contract as ConsumeCharge: the last point of the shield has just been spent, so an
            // `absorption` trigger runs here, before Exit() unsubscribes it. A shield that swallowed no
            // damage (a 은신 buff breaking on the first hit) raises nothing, as it did before.
            if (outcome.Absorbed > 0)
                Events.OnAbsorptionConsumed(this, new OnAbsorptionConsumedArgs { Amount = outcome.Absorbed });
            Exit(false);
        }
        else if (changed)
        {
            NotifyUpdated(reason: 2); // charge consumed
        }

        return outcome.Remaining;
    }
}
