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
            var trigger = new BuffTrigger(buff, triggerTemplate);

            // One binding table, used for subscribing and unsubscribing alike: the two used to be separate
            // switches, and the kinds they disagreed about silently kept or lost their subscription.
            var bound = ChangeBuffSubscription(buff, trigger, subscribe: true);
            if (!bound && _ownerUnit != null)
                bound = ChangeUnitSubscription(_ownerUnit, trigger, subscribe: true);

            if (!bound)
            {
                // Kinds whose events exist but are not wired yet (landing, kill, unmount, time, ...).
                Logger.Trace("Unimplemented BuffTrigger[\"{0}\"]", triggerTemplate.Kind);
                continue;
            }

            _triggers.Add(trigger);
            Logger.Trace("Subscribed BuffTrigger[\"{0}\"] on owner {1}", triggerTemplate.Kind, _ownerUnit?.ObjId ?? 0);
        }
    }

    /// <summary>
    /// Detaches every trigger this handler subscribed. It no longer raises anything itself: which of
    /// <c>OnTimeout</c> and <c>OnDispelled</c> runs is decided by how the buff ended, in
    /// <see cref="Buff.StopEffectTask"/>.
    /// </summary>
    public void UnsubscribeEvents()
    {
        foreach (var trigger in _triggers)
        {
            var unbound = ChangeBuffSubscription(buff, trigger, subscribe: false);
            if (!unbound && _ownerUnit != null)
                ChangeUnitSubscription(_ownerUnit, trigger, subscribe: false);

            // A trigger sitting out its delay_time would otherwise apply for a buff that is already gone.
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
            case BuffEventTriggerKind.Death:
                Wire(ref events.OnDeath, trigger.Execute, subscribe);
                return true;
            default:
                return false;
        }
    }

    private static void Wire<T>(ref EventHandler<T> slot, EventHandler<T> handler, bool subscribe) where T : EventArgs
    {
        if (subscribe)
            slot += handler;
        else
            slot -= handler;
    }
}
