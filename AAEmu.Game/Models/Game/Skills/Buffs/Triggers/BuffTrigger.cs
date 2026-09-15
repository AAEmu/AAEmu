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
        var ownerObjId = owner.ObjId;
        void ApplyEffect() =>
            template.Effect.Apply(source, new SkillCasterUnit(ownerObjId), target, new SkillCastUnitTarget(target.ObjId),
                new CastBuff(_buff),
                new EffectSource(_buff.Skill, _buff.Template) { Amount = amount, IsTrigger = true },
                null, DateTime.UtcNow);

        if (template.DelayTime == 0)
        {
            ApplyEffect();
            return;
        }

        // The units are resolved now, while the event that named them is still on the stack; only the
        // application waits, so a delayed trigger still acts on what its event was about.
        Logger.Trace("Buff[{0}] {1} delayed by {2} ms", _buff?.Template?.BuffId, GetType().Name, template.DelayTime);
        TaskManager.Instance.Schedule(new BuffTriggerTask(ApplyEffect), TimeSpan.FromMilliseconds(template.DelayTime));
    }

    public BuffTrigger(Buff buff, BuffTriggerTemplate template)
    {
        _buff = buff;
        _owner = _buff.Owner;
        Template = template;
    }
}
