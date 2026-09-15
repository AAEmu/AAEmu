using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// The pure decisions behind one fired <c>buff_triggers</c> row: which two units its effect runs between,
/// how much it carries, and whether the row's unit gates let it run at all.
/// </summary>
/// <remarks>
/// A trigger is subscribed to the events of the buff's OWNER, so the unit that caused the event is not the
/// row's author - it arrives in the event args. Everything that turns (row, owner, caster, args) into an
/// effect call lives here so it can be tested without a world, a socket or a scheduler.
/// </remarks>
public static class BuffTriggerAgentRules
{
    /// <summary>
    /// The per-mille amount a triggered effect reads as "its authored value, once". Effects that consume
    /// <c>EffectSource.Amount</c> scale by it - <c>HealEffect</c> computes <c>value / 1000f * Amount</c> for
    /// a triggered fixed heal (<c>Effects/HealEffect.cs</c>, the <c>source.IsTrigger</c> branch) - so one
    /// stack is 1000, not 1.
    /// </summary>
    public const int TriggerFullAmount = 1000;

    /// <summary>
    /// Resolves the effect's source and target from the row's <c>source_agent_id</c> /
    /// <c>target_agent_id</c> columns, decoded through <see cref="BuffTriggerAgent"/>.
    /// </summary>
    /// <param name="template">The row. A null template, or the absent columns it stands for (agent 0),
    /// resolves to owner -> owner, which is what every trigger did before the columns were read.</param>
    /// <param name="owner">The unit the buff sits on. May be null or a non-Unit (doodad, house).</param>
    /// <param name="caster">The buff's caster, i.e. the original source. Null when the buff was applied by
    /// something that is not a Unit.</param>
    /// <param name="args">The event args the trigger was called with; they carry the event's own units.</param>
    /// <returns>
    /// The pair to apply the effect between. Either half can be null - a row that asks for a source that
    /// does not exist (a doodad caster, an attacker-less tick) has nothing to apply - and the caller must
    /// skip rather than substitute a unit the row did not name.
    /// </returns>
    public static (BaseUnit Source, BaseUnit Target) Resolve(BuffTriggerTemplate template, BaseUnit owner, BaseUnit caster, EventArgs args)
    {
        var (eventSource, eventTarget) = EventUnits(args);

        var sourceAgent = template?.SourceAgentId ?? BuffTriggerAgent.Owner;
        var targetAgent = template?.TargetAgentId ?? BuffTriggerAgent.Owner;

        return (Pick(sourceAgent, owner, caster, eventSource, eventTarget),
            Pick(targetAgent, owner, caster, eventSource, eventTarget));
    }

    /// <summary>
    /// The value a fired trigger hands to its effect in <c>EffectSource.Amount</c>.
    /// </summary>
    /// <param name="template">The row carrying <c>use_damage_amount</c> and <c>use_stack_count</c>.</param>
    /// <param name="stackCount">Applications this buff instance represents (<c>Buff.Stack</c>).</param>
    /// <param name="args">The event args, for the damage amount when the row asks for it.</param>
    /// <remarks>
    /// The two flags describe the same field, so one of them has to win when a row sets both: the damage
    /// amount is the base and the stack count multiplies it. No enabled row sets both (all four
    /// <c>use_stack_count</c> rows have <c>use_damage_amount = 'f'</c>).
    /// </remarks>
    public static int ResolveAmount(BuffTriggerTemplate template, int stackCount, EventArgs args)
    {
        if (template == null)
            return 0;

        var damage = EventDamageAmount(args);
        var amount = template.UseDamageAmount ? damage : 0;

        if (!template.UseStackCount)
            return amount;

        var stacks = Math.Max(1, stackCount);
        return template.UseDamageAmount ? amount * stacks : TriggerFullAmount * stacks;
    }

    /// <summary>
    /// Whether the row's unit requirements are met by the units its effect will act on. Each gate is
    /// checked the way the old code checked <c>target_buff_tag_id</c>, through
    /// <see cref="Buffs.CheckBuffTag"/> on the unit the column names.
    /// </summary>
    /// <remarks>
    /// A gate whose tag id is absent is not a requirement, so a row with no tag ids passes whatever
    /// <c>or_unit_reqs</c> says - reading that flag as "at least one of zero gates" would otherwise block
    /// every one of its 12 enabled rows. The <c>check_tag_src_in_*</c> flags pick which unit the
    /// <c>source_*_buff_tag_id</c> gates are looked for on; the column name already means the source, so
    /// leaving them clear (all 83 enabled rows that set <c>source_buff_tag_id</c> do) keeps that default.
    /// </remarks>
    public static bool AllowsGates(BuffTriggerTemplate template, BaseUnit owner, BaseUnit source, BaseUnit target)
    {
        if (template == null)
            return true;

        var gates = new List<bool>(6);

        AddGate(gates, template.OwnerBuffTagId, owner, mustHave: true);
        AddGate(gates, template.OwnerNoBuffTagId, owner, mustHave: false);

        var tagSourceUnit = template.CheckTagSrcInOwner
            ? owner
            : template.CheckTagSrcInTarget ? target : source;
        var noTagSourceUnit = template.CheckNoTagSrcInOwner
            ? owner
            : template.CheckNoTagSrcInTarget ? target : source;
        AddGate(gates, template.SourceBuffTagId, tagSourceUnit, mustHave: true);
        AddGate(gates, template.SourceNoBuffTagId, noTagSourceUnit, mustHave: false);

        AddGate(gates, template.TargetBuffTagId, target, mustHave: true);
        AddGate(gates, template.TargetNoBuffTagId, target, mustHave: false);

        if (gates.Count == 0)
            return true;

        return template.OrUnitReqs ? gates.Any(passed => passed) : gates.All(passed => passed);
    }

    private static void AddGate(List<bool> gates, uint tagId, BaseUnit unit, bool mustHave)
    {
        if (tagId == 0)
            return;

        var hasTag = unit?.Buffs?.CheckBuffTag(tagId) == true;
        gates.Add(mustHave ? hasTag : !hasTag);
    }

    private static BaseUnit Pick(BuffTriggerAgent agent, BaseUnit owner, BaseUnit caster, BaseUnit eventSource, BaseUnit eventTarget) =>
        agent switch
        {
            BuffTriggerAgent.Owner => owner,
            BuffTriggerAgent.Source => eventSource ?? caster,
            BuffTriggerAgent.Target => eventTarget ?? owner,
            BuffTriggerAgent.OriginalSource => caster,
            // An id this build does not know must not quietly point the effect at some other unit.
            _ => owner
        };

    /// <summary>
    /// The units the event that fired this trigger names. Only what the raisers actually carry: the
    /// attacker of a hit, the killer and victim of a death. <c>attacked</c> carries no unit of its own
    /// beyond the attacker, and the buff-lifecycle events (timeout, dispelled, started) carry none.
    /// </summary>
    private static (BaseUnit Source, BaseUnit Target) EventUnits(EventArgs args) =>
        args switch
        {
            OnAttackArgs attack => (attack.Attacker, attack.Target),
            OnAttackedArgs attacked => (attacked.Attacker, null),
            OnDamageArgs damage => (damage.Attacker, damage.Target),
            OnDamagedArgs damaged => (damaged.Attacker, null),
            OnDeathArgs death => (death.Killer, death.Victim),
            _ => (null, null)
        };

    private static int EventDamageAmount(EventArgs args) =>
        args switch
        {
            OnDamageArgs damage => damage.Amount,
            OnDamagedArgs damaged => damaged.Amount,
            _ => 0
        };
}