using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// Binds a buff's <c>buff_triggers</c> rows to the events that fire them.
/// </summary>
/// <remarks>
/// A trigger belongs to the unit the buff sits on: "when I am damaged" is the owner's <c>OnDamaged</c>, not
/// the applier's. Subscribing to <c>Buff.Caster</c> instead made a debuff cast by A on B react to A being
/// hit and never to B - and threw outright when the applier was not a Unit at all (a doodad or item leaves
/// <c>Caster</c> null). <c>Buff.Caster</c> is still what a row reaches for when it names agent
/// <see cref="BuffTriggerAgent.OriginalSource"/>, so it is read during resolution rather than here.
/// <para>
/// Which kinds exist and how each one fires is <see cref="BuffTriggerKindRules"/>' table, not a fall-through
/// here: a kind is either bound below, scheduled, or listed there as not applicable with the reason.
/// </para>
/// </remarks>
public class BuffTriggersHandler(Buff buff)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<BuffTrigger> _triggers = [];

    /// <summary>
    /// The unit whose events the owner-centric kinds are subscribed to. Null when the buff sits on something
    /// that is not a Unit (doodad, house); buff-lifecycle triggers (started, timeout, dispelled) live on the
    /// buff's own events and still work, the rest have no events to bind to.
    /// </summary>
    private readonly Unit _ownerUnit = buff.Owner as Unit;

    public void SubscribeEvents()
    {
        var buffId = buff.Template.BuffId;

        var triggerTemplates = SkillManager.Instance.GetBuffTriggerTemplates(buffId);

        foreach (var triggerTemplate in triggerTemplates)
        {
            var binding = BuffTriggerKindRules.For(triggerTemplate.Kind);
            if (binding == null)
            {
                // An event_id this build does not define: new content, not a wiring gap. Loud on purpose -
                // the alternative is a row that looks subscribed and never fires.
                Logger.Warn("Buff[{0}] trigger {1} has event_id {2}, which this build does not implement",
                    buffId, triggerTemplate.Id, (int)triggerTemplate.Kind);
                continue;
            }

            if (binding.Value.Wiring == BuffTriggerWiring.NotApplicable)
            {
                Logger.Trace("Buff[{0}] trigger {1} kind {2} ({3}) cannot fire here: {4}",
                    buffId, triggerTemplate.Id, (int)binding.Value.DbId, binding.Value.DbName, binding.Value.Reason);
                continue;
            }

            var trigger = new BuffTrigger(buff, triggerTemplate);

            // One binding table, used for subscribing and unsubscribing alike: the two used to be separate
            // switches, and the kinds they disagreed about silently kept or lost their subscription.
            var bound = binding.Value.Wiring switch
            {
                BuffTriggerWiring.BuffEvent => ChangeBuffSubscription(buff, trigger, subscribe: true),
                BuffTriggerWiring.UnitEvent => _ownerUnit != null &&
                                               ChangeUnitSubscription(_ownerUnit, trigger, subscribe: true),
                BuffTriggerWiring.Scheduled => ScheduleTimeTrigger(trigger),
                _ => false
            };

            if (!bound)
            {
                // The kind's state table and these switches disagree. That is a bug in one of them, and it
                // must not look like content that is merely unwired.
                Logger.Warn("Buff[{0}] trigger {1} kind {2} ({3}) is classified as {4} but nothing bound it",
                    buffId, triggerTemplate.Id, (int)binding.Value.DbId, binding.Value.DbName, binding.Value.Wiring);
                continue;
            }

            _triggers.Add(trigger);
            Logger.Trace("Subscribed BuffTrigger[\"{0}\"] on owner {1}", triggerTemplate.Kind, _ownerUnit?.ObjId ?? 0);
        }
    }

    /// <summary>
    /// Detaches every trigger this handler subscribed, and drops the ones it scheduled. It no longer raises
    /// anything itself: which of <c>OnTimeout</c> and <c>OnDispelled</c> runs is decided by how the buff
    /// ended, in <see cref="Buff.StopEffectTask"/>.
    /// </summary>
    public void UnsubscribeEvents()
    {
        foreach (var trigger in _triggers)
        {
            var unbound = ChangeBuffSubscription(buff, trigger, subscribe: false);
            if (!unbound && _ownerUnit != null)
                ChangeUnitSubscription(_ownerUnit, trigger, subscribe: false);

            // A scheduled `time` row is bounded by the buff's life: the buff ending before the offset is
            // reached means the effect never happens, not that it happens late.
            trigger.CancelScheduled();
            // And an event trigger queued behind its delay_time must not answer for a buff that is gone.
            // A delayed `timeout` row is tracked by neither: it is authored to fire after the buff ends.
            trigger.CancelPending();
        }

        _triggers.Clear();
    }

    /// <summary>Triggers carried by the buff itself, which exist for any kind of owner.</summary>
    private static bool ChangeBuffSubscription(Buff owner, BuffTrigger trigger, bool subscribe)
    {
        var events = owner.Events;
        switch (trigger.Template.Kind)
        {
            case BuffEventTriggerKind.Started:
                Wire(ref events.OnBuffStarted, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Dispelled:
                Wire(ref events.OnDispelled, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Timeout:
                Wire(ref events.OnTimeout, trigger.Execute, subscribe);
                return true;
            // `any` is every removal reason, and StopEffectTask raises exactly one of these two.
            case BuffEventTriggerKind.Any:
                Wire(ref events.OnDispelled, trigger.Execute, subscribe);
                Wire(ref events.OnTimeout, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.RemoveNeedBuff:
                Wire(ref events.OnRequiredBuffLost, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.UserCancel:
                Wire(ref events.OnUserCancel, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.RemoveStealth:
                Wire(ref events.OnStealthRemoved, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Absorption:
                Wire(ref events.OnAbsorptionConsumed, trigger.Execute, subscribe);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Triggers carried by the owner's unit events. Which unit an event is raised on is what decides the
    /// binding: the attacker's <c>OnAttack</c>/<c>OnDamage</c>, the victim's
    /// <c>OnAttacked</c>/<c>OnDamaged*</c>/<c>OnDeath</c>.
    /// </summary>
    private static bool ChangeUnitSubscription(Unit ownerUnit, BuffTrigger trigger, bool subscribe)
    {
        var events = ownerUnit.Events;
        switch (trigger.Template.Kind)
        {
            case BuffEventTriggerKind.Attack:
                Wire(ref events.OnAttack, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Attacked:
                Wire(ref events.OnAttacked, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Damage:
                Wire(ref events.OnDamage, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Damaged:
            // The removal half of `remove_on_damaged` is the remove-on flag path; the row's own effect
            // runs on the hit that damaged the owner.
            case BuffEventTriggerKind.RemoveOnDamaged:
                Wire(ref events.OnDamaged, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamagedMelee:
                Wire(ref events.OnDamagedMelee, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamagedRanged:
                Wire(ref events.OnDamagedRanged, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamagedSpell:
                Wire(ref events.OnDamagedSpell, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamagedSiege:
                Wire(ref events.OnDamagedSiege, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamageMelee:
                Wire(ref events.OnDamageMelee, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamageRanged:
                Wire(ref events.OnDamageRanged, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamageSpell:
                Wire(ref events.OnDamageSpell, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamageSiege:
                Wire(ref events.OnDamageSiege, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Landing:
                Wire(ref events.OnLanding, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.RemoveOnMove:
                Wire(ref events.OnMovement, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.ChannelingCancel:
                Wire(ref events.OnChannelingCancel, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Death:
                Wire(ref events.OnDeath, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.Unmount:
                Wire(ref events.OnUnmount, trigger.Execute, subscribe);
                return true;
            // `kill` and `kill_any` share the killer's OnKill: see BuffTriggerKindRules for why the content
            // does not separate them.
            case BuffEventTriggerKind.Kill:
            case BuffEventTriggerKind.KillAny:
                Wire(ref events.OnKill, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.DamagedCollision:
                Wire(ref events.OnDamagedCollision, trigger.Execute, subscribe);
                return true;
            case BuffEventTriggerKind.UseSkill:
                Wire(ref events.OnSkillUse, trigger.Execute, subscribe);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Puts a <c>time</c> row on the scheduler at the offset it was authored with, measured from now -
    /// the handler subscribes at the moment the buff is applied, so that is the buff's start.
    /// </summary>
    private bool ScheduleTimeTrigger(BuffTrigger trigger)
    {
        var offset = BuffTriggerKindRules.ResolveTimeOffsetMs(trigger.Template.DelayTime, BuffDurationMs());
        return trigger.ScheduleTime(offset);
    }

    /// <summary>
    /// The buff's lifetime in milliseconds for the negative <c>time</c> offsets. The duration is resolved
    /// after <see cref="SubscribeEvents"/> runs (<c>Buffs.AddBuff</c> subscribes, then SetInUse resolves it),
    /// so the template is the authority here.
    /// </summary>
    private int BuffDurationMs() =>
        buff.Duration > 0 ? buff.Duration : buff.Template?.GetDuration(buff.AbLevel) ?? 0;

    private static void Wire<T>(ref EventHandler<T> slot, EventHandler<T> handler, bool subscribe) where T : EventArgs
    {
        if (subscribe)
            slot += handler;
        else
            slot -= handler;
    }
}
