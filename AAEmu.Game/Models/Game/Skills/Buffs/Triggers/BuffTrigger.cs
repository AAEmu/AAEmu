using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

/// <summary>
/// One enabled <c>buff_triggers</c> row bound to a live buff: when the event the row names fires on the
/// buff's owner, apply the row's effect with the source and target the row's agent columns name.
/// </summary>
/// <remarks>
/// One class serves every kind. The old per-kind subclasses differed only in which of these columns they
/// looked at, and disagreed with each other about the amount and the target while doing it.
/// </remarks>
public class BuffTrigger
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected Buff _buff;
    protected readonly BaseUnit _owner;
    /// <summary>The application waiting out <c>delay_time</c>, so it can be cancelled with the buff.</summary>
    /// <summary>
    /// The queued application of an event trigger's <c>delay_time</c>, kept so removing the buff first
    /// can drop it. A delayed <c>timeout</c> row is not tracked here: it is meant to run after the buff
    /// has ended. Scheduled <c>time</c> rows have their own field, <see cref="_scheduled"/>.
    /// </summary>
    private BuffTriggerTask _delayedTask;
    public BuffTriggerTemplate Template { get; set; }

    /// <summary>
    /// The queued application of a scheduled (<c>time</c>) row, kept so the buff ending can drop it.
    /// Delayed event triggers are not tracked here: a <c>timeout</c> row with a delay fires <em>after</em>
    /// the buff it belongs to has ended, so cancelling those would lose the row.
    /// </summary>
    private BuffTriggerTask _scheduled;

    public virtual void Execute(object sender, EventArgs eventArgs) => Fire(eventArgs, Template?.DelayTime ?? 0);

    /// <summary>
    /// Queues this trigger for <paramref name="delayMs"/> from now, for kinds that have no event of their
    /// own (<c>time</c>). <see cref="CancelScheduled"/> drops it if the buff ends first.
    /// </summary>
    /// <returns>Whether the application was queued (or run inline); false if the scheduler refused it.</returns>
    public bool ScheduleTime(uint delayMs)
    {
        if (delayMs == 0)
        {
            // Authored at the buff's start: apply it now rather than queue a task for the next tick.
            Fire(EventArgs.Empty, 0);
            return true;
        }

        var task = new BuffTriggerTask(() => Fire(EventArgs.Empty, 0));
        _scheduled = task;
        return TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(delayMs));
    }

    /// <summary>Drops a scheduled application that has not run yet.</summary>
    public void CancelScheduled()
    {
        if (_scheduled == null)
            return;

        TaskManager.Instance.Cancel(_scheduled);
        _scheduled = null;
    }

    private void Fire(EventArgs eventArgs, int delayMs)
    {
        var template = Template;
        var owner = _buff?.Owner;
        if (template?.Effect == null || owner == null)
            return;

        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff?.Template?.BuffId, GetType().Name, template.Effect.GetType().Name, template.Effect.Id);

        // 10.0.2.13: buff_triggers.effect_on_source / use_original_source removed - which units the effect
        // runs between comes from source_agent_id / target_agent_id, through the enum the content database
        // itself names them with.
        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, _buff.Caster, eventArgs);
        if (source == null || target == null)
        {
            // A row can name a unit the event does not have (a doodad applier for a source, an attr-less tick).
            // Nothing is applied rather than substituting a unit the row did not ask for.
            Logger.Trace("Buff[{0}] {1} skipped: {2} -> {3}", _buff?.Template?.BuffId, GetType().Name,
                source == null ? "no source" : "source", target == null ? "no target" : "target");
            return;
        }

        if (!BuffTriggerAgentRules.AllowsGates(template, owner, source, target))
            return;

        var amount = BuffTriggerAgentRules.ResolveAmount(template, _buff.Stack, eventArgs);

        // The descriptor names the unit the effect is applied by, which is the resolved source: a row that
        // names another unit as its source (821 enabled rows set source_agent_id) otherwise travelled with
        // a caster and a SkillCaster that disagreed — DamageEffect puts both in the same
        // SCUnitDamagedPacket, HealEffect and RestoreManaEffect do the same for SCUnitHealedPacket, and
        // BuffEffect stores a Buff whose Caster and SkillCaster differ.
        var casterObj = new SkillCasterUnit(source.ObjId);

        // Only a row that reads the damage amount gets the trigger-shaped source. HealEffect branches on
        // IsTrigger and Amount (HealEffect.cs:114), and with use_damage_amount 'f' the amount is 0, so the
        // 20 enabled use_fixed_heal rows on attack/started/timeout/damage kinds healed nothing instead of
        // their authored range; the per-kind triggers this replaced passed a plain EffectSource there.
        //
        // FromBuffTrigger is separate from IsTrigger and set for every row: it says where the hit came
        // from, not how much of the event's amount the row reads. DamageEffect raises
        // remove_on_*_buff_trigger (344/486/301/680 buffs) for a hit that carries it, and a
        // use_damage_amount 'f' damage row is still a trigger's hit even though IsTrigger stays false for
        // it. IsTrigger keeps B1's narrow meaning so HealEffect cannot see it on a row it would zero.
        var effectSource = new EffectSource(_buff.Skill, _buff.Template) { FromBuffTrigger = true };
        if (template.UseDamageAmount)
        {
            effectSource.Amount = amount;
            effectSource.IsTrigger = true;
        }

        // Queued while the buff was live or as it was ending: a delay scheduled by a timeout or a dispel
        // runs during that ending, so only a buff that was live when the delay was armed has to still be
        // live when it expires.
        // A `timeout` row is the one kind whose effect is authored to run after the buff ends, so the
        // liveness re-check below must not apply to it: when the buff times out it is still in use as this
        // runs, which would make wasLive true and then discard the application a moment later.
        var wasLive = Template?.Kind != BuffEventTriggerKind.Timeout
                      && _buff != null && _buff.InUse && !_buff.IsEnded();

        void ApplyEffect()
        {
            // Re-checked when a delayed task runs, and UnsubscribeEvents cancels the handle as well: the
            // buff can be removed or dispelled while the task waits.
            if (_buff?.Owner == null || (wasLive && (!_buff.InUse || _buff.IsEnded())))
                return;

            template.Effect.Apply(source, casterObj, target, new SkillCastUnitTarget(target.ObjId),
                new CastBuff(_buff), effectSource, null, DateTime.UtcNow);
        }

        if (delayMs <= 0)
        {
            ApplyEffect();
            return;
        }

        // The units are resolved now, while the event that named them is still on the stack; only the
        // application waits, so a delayed trigger still acts on what its event was about.
        Logger.Trace("Buff[{0}] {1} delayed by {2} ms", _buff?.Template?.BuffId, GetType().Name, delayMs);
        // Two delayed lifetimes, and the field a queued application goes into is what decides whether
        // removing the buff first can drop it:
        //  * an event trigger (damaged, attacked, ...) is an answer to something that happened while the
        //    buff was up, so it is tracked and cancelled on removal - see CancelPending;
        //  * a `timeout` row is authored to fire *after* the buff ends (23 enabled rows carry
        //    delay_time > 0, e.g. a death-rattle effect), so it is queued untracked;
        //  * a `time` row never reaches here: ScheduleTime owns it and CancelScheduled drops it.
        if (Template?.Kind == BuffEventTriggerKind.Timeout)
        {
            TaskManager.Instance.Schedule(new BuffTriggerTask(ApplyEffect), TimeSpan.FromMilliseconds(delayMs));
            return;
        }

        _delayedTask = new BuffTriggerTask(ApplyEffect);
        TaskManager.Instance.Schedule(_delayedTask, TimeSpan.FromMilliseconds(delayMs));
    }

    /// <summary>
    /// Drops the application this trigger queued for its <c>delay_time</c>. Called when the buff is
    /// unsubscribed, so an event trigger cannot act for a buff that has since been removed. Scheduled
    /// <c>time</c> rows are dropped by <see cref="CancelScheduled"/> instead, and a delayed
    /// <c>timeout</c> row is deliberately not tracked by either.
    /// </summary>
    public void CancelPending()
    {
        if (_delayedTask == null)
            return;

        _delayedTask.Cancel();
        _delayedTask = null;
    }

    public BuffTrigger(Buff buff, BuffTriggerTemplate template)
    {
        _buff = buff;
        _owner = _buff.Owner;
        Template = template;
    }
}
