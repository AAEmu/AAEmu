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
    private BuffTriggerTask _delayedTask;
    public BuffTriggerTemplate Template { get; set; }
    public virtual void Execute(object sender, EventArgs eventArgs)
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
        var effectSource = template.UseDamageAmount
            ? new EffectSource(_buff.Skill, _buff.Template) { Amount = amount, IsTrigger = true }
            : new EffectSource(_buff.Skill, _buff.Template);

        // Queued while the buff was live or as it was ending: a delay scheduled by a timeout or a dispel
        // runs during that ending, so only a buff that was live when the delay was armed has to still be
        // live when it expires.
        var wasLive = _buff != null && _buff.InUse && !_buff.IsEnded();

        void ApplyEffect()
        {
            // Re-checked when a delayed task runs, and UnsubscribeEvents cancels the handle as well: the
            // buff can be removed or dispelled while the task waits.
            if (_buff?.Owner == null || (wasLive && (!_buff.InUse || _buff.IsEnded())))
                return;

            template.Effect.Apply(source, casterObj, target, new SkillCastUnitTarget(target.ObjId),
                new CastBuff(_buff), effectSource, null, DateTime.UtcNow);
        }

        if (template.DelayTime == 0)
        {
            ApplyEffect();
            return;
        }

        // The units are resolved now, while the event that named them is still on the stack; only the
        // application waits, so a delayed trigger still acts on what its event was about.
        Logger.Trace("Buff[{0}] {1} delayed by {2} ms", _buff?.Template?.BuffId, GetType().Name, template.DelayTime);
        _delayedTask = new BuffTriggerTask(ApplyEffect);
        TaskManager.Instance.Schedule(_delayedTask, TimeSpan.FromMilliseconds(template.DelayTime));
    }

    /// <summary>
    /// Drops the application this trigger queued for its <c>delay_time</c>. Called when the buff is
    /// unsubscribed, so a trigger cannot act for a buff that has since been removed.
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
