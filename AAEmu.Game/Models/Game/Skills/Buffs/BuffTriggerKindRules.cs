namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>How a <c>buff_triggers</c> row of a given kind reaches its effect.</summary>
public enum BuffTriggerWiring
{
    /// <summary>An event on the buff instance itself: the row fires because the buff did something.</summary>
    BuffEvent,

    /// <summary>An event on the unit the buff sits on, raised by the world.</summary>
    UnitEvent,

    /// <summary>No event: the handler schedules the effect itself, at the row's authored offset.</summary>
    Scheduled,

    /// <summary>Nothing in this build can raise it. <see cref="BuffTriggerKindBinding.Reason"/> says why.</summary>
    NotApplicable
}

/// <summary>One <c>enum_buff_trigger_events</c> id: what it is, how it fires, and why.</summary>
/// <param name="Kind">The enum member the loader casts <c>event_id</c> into.</param>
/// <param name="DbId">The id in <c>enum_buff_trigger_events</c>.</param>
/// <param name="DbName">The name in <c>enum_buff_trigger_events</c>, spelling included.</param>
/// <param name="Wiring">Which mechanism fires it.</param>
/// <param name="Reason">What raises it, or why nothing in this build can.</param>
public readonly record struct BuffTriggerKindBinding(
    BuffEventTriggerKind Kind,
    uint DbId,
    string DbName,
    BuffTriggerWiring Wiring,
    string Reason);

/// <summary>
/// Every <c>buff_triggers.event_id</c> the 10.0.2.13 content database defines, and how each one is
/// raised. <see cref="BuffTriggersHandler"/> consumes this table instead of falling through a switch,
/// so a kind can no longer be silently unwired: it is either bound to something that is raised, or
/// listed here as not applicable with the reason.
/// </summary>
/// <remarks>
/// <para>
/// Content counts are from <c>game_decrypted.sqlite3</c>:
/// <c>SELECT event_id, count(*) FROM buff_triggers WHERE enable='t' GROUP BY event_id</c> - 36 ids,
/// 11,283 enabled rows. They are documentation, not a decision input: nothing here branches on them.
/// </para>
/// <code>
/// id  name                rows  decision
///  1  attack                62  unit  owner's OnAttack (the attacker's own event)          [B1]
///  2  attacked              48  unit  owner's OnAttacked                                  [B1]
///  3  damage_any            34  unit  owner's OnDamage                                    [B1]
///  4  damaged              443  unit  owner's OnDamaged                                   [B1]
///  5  dispelled            136  buff  the buff's OnDispelled                              [B1]
///  6  timeout             4060  buff  the buff's OnTimeout                                 [B1]
///  7  damaged_melee         20  unit  owner's OnDamagedMelee                              [B1]
///  8  damaged_ranged        21  unit  owner's OnDamagedRanged                             [B1]
///  9  damaged_spell         30  unit  owner's OnDamagedSpell                              [B1]
/// 10  damaged_siege        154  unit  owner's OnDamagedSiege                              [B1]
/// 11  landing              190  unit  owner's OnLanding - raised where a fall is reported
/// 12  started             5121  buff  the buff's OnBuffStarted                             [B1]
/// 13  remove_on_move       15  unit  owner's OnMovement - raised where a move is accepted
/// 14  channeling_cancel     1  unit  owner's OnChannelingCancel (raised by Skill.EndChanneling)
/// 15  remove_on_damaged    14  unit  owner's OnDamaged
/// 16  death                287  unit  owner's OnDeath (the buff sits on the victim)        [B1]
/// 17  unmount              20  unit  owner's OnUnmount - raised on the rider that dismounts
/// 18  kill                  56  unit  owner's OnKill (the buff sits on the killer)
/// 19  damaged_collision      1  unit  owner's OnDamagedCollision - raised by hull collisions
/// 20  immotality            15  N/A   no immortality state exists to change - see Reason
/// 21  time                 256  sched scheduled at the row's authored offset - see Reason
/// 22  kill_any              13  unit  owner's OnKill, the same raise as id 18 - see Reason
/// 23  any                  242  buff  the buff's OnDispelled and OnTimeout (every removal)
/// 24  remove_need_buff      13  buff  the buff's OnRequiredBuffLost (buffs.require_buff_id)
/// 25  user_cancel            1  buff  the buff's OnUserCancel (CSRemoveBuffPacket)
/// 26  use_skill              1  unit  owner's OnSkillUse - raised by Unit.OnSkillEnd
/// 27  remove_stealth         1  buff  the buff's OnStealthRemoved (Buffs.RemoveStealth)
/// 28  skill_controller       0  N/A   no enabled rows, and no removal path for the column
/// 29  absorption             7  buff  the buff's OnAbsorptionConsumed (Buff.ConsumeCharge)
/// 30  remove_aura            0  N/A   no enabled rows, and no aura state to observe
/// 31  breaker                5  N/A   buff_breakers is not loaded (task B5)
/// 32  damage_melee           7  unit  owner's OnDamageMelee (the attacker's own event)
/// 33  damage_spell           3  unit  owner's OnDamageSpell
/// 34  damage_range           4  unit  owner's OnDamageRanged
/// 35  damage_siege           1  unit  owner's OnDamageSiege
/// 36  system                 1  N/A   one row on a buffs.system='t' buff - see Reason
/// </code>
/// </remarks>
public static class BuffTriggerKindRules
{
    /// <summary>
    /// The 36 kinds, in <c>enum_buff_trigger_events</c> id order. Order is part of the contract: the
    /// test that walks the database's ids walks this list.
    /// </summary>
    private static readonly BuffTriggerKindBinding[] Table =
    [
        new(BuffEventTriggerKind.Attack, 1, "attack", BuffTriggerWiring.UnitEvent,
            "the owner's OnAttack (DamageEffect raises it on the attacker)"),
        new(BuffEventTriggerKind.Attacked, 2, "attacked", BuffTriggerWiring.UnitEvent,
            "the owner's OnAttacked (DamageEffect raises it on the victim)"),
        new(BuffEventTriggerKind.Damage, 3, "damage_any", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamage (DamageEffect raises it on the attacker)"),
        new(BuffEventTriggerKind.Damaged, 4, "damaged", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamaged (DamageEffect raises it on the victim)"),
        new(BuffEventTriggerKind.Dispelled, 5, "dispelled", BuffTriggerWiring.BuffEvent,
            "the buff's OnDispelled: every way the buff ends except its own expiry"),
        new(BuffEventTriggerKind.Timeout, 6, "timeout", BuffTriggerWiring.BuffEvent,
            "the buff's OnTimeout: its own duration or tick running out"),
        new(BuffEventTriggerKind.DamagedMelee, 7, "damaged_melee", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamagedMelee (DamageEffect, typed damage)"),
        new(BuffEventTriggerKind.DamagedRanged, 8, "damaged_ranged", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamagedRanged"),
        new(BuffEventTriggerKind.DamagedSpell, 9, "damaged_spell", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamagedSpell"),
        new(BuffEventTriggerKind.DamagedSiege, 10, "damaged_siege", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamagedSiege"),
        new(BuffEventTriggerKind.Landing, 11, "landing", BuffTriggerWiring.UnitEvent,
            "the owner's OnLanding, raised where a fall is reported: CSMoveUnitPacket (a client " +
            "FallVel) and ZoneSimRelay.HandleUnitFell (ZWUnitFell from the native zone)"),
        new(BuffEventTriggerKind.Started, 12, "started", BuffTriggerWiring.BuffEvent,
            "the buff's OnBuffStarted, raised by Buffs.AddBuff once the triggers are subscribed"),
        new(BuffEventTriggerKind.RemoveOnMove, 13, "remove_on_move", BuffTriggerWiring.UnitEvent,
            "the owner's OnMovement, raised where a move is accepted (CSMoveUnitPacket.RemoveEffects " +
            "and Unit.SetPosition/CheckMovedPosition). The removal half of the name is the separate " +
            "remove-on flag path: buffs.remove_on_move -> BuffRemoveOn.Move"),
        new(BuffEventTriggerKind.ChannelingCancel, 14, "channeling_cancel", BuffTriggerWiring.UnitEvent,
            "the owner's OnChannelingCancel, raised by Skill.EndChanneling and PlotTree.DoPlotEnd"),
        new(BuffEventTriggerKind.RemoveOnDamaged, 15, "remove_on_damaged", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamaged. The removal half of the name is the remove-on flag path " +
            "(buffs.remove_on_damaged_* -> BuffRemoveOn.Damaged*, owned by task B5)"),
        new(BuffEventTriggerKind.Death, 16, "death", BuffTriggerWiring.UnitEvent,
            "the owner's OnDeath: the buff sits on the unit that died"),
        new(BuffEventTriggerKind.Unmount, 17, "unmount", BuffTriggerWiring.UnitEvent,
            "the owner's OnUnmount, raised on the rider that dismounts: MateManager.UnMountMate and " +
            "SlaveManager.UnbindSlave (all 20 enabled rows sit on a remove_on_unmount='t' buff)"),
        new(BuffEventTriggerKind.Kill, 18, "kill", BuffTriggerWiring.UnitEvent,
            "the owner's OnKill: the buff sits on the unit that landed the killing blow, raised once " +
            "by Unit.DoDie (and Slave.DoDie) with the victim in the args"),
        new(BuffEventTriggerKind.DamagedCollision, 19, "damaged_collision", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamagedCollision, raised by SlaveCollisionDamage.ApplyFromImpact - the " +
            "only collision damage this build applies is hull damage"),
        new(BuffEventTriggerKind.Immotality, 20, "immotality", BuffTriggerWiring.NotApplicable,
            "no immortality state exists to raise on: buffs.one_time_immortality, " +
            "melee/spell/ranged/siege/drowning_immortality and immune_health are loaded into " +
            "BuffTemplate and read by nothing. Only FallDamageImmortality is consulted " +
            "(Unit.DoFallDamage), and that path is the landing/immunity code, not a state change. " +
            "The 15 rows (buff 18348 '불사', 649 '죽은척 하기') fire when a lethal hit is negated; " +
            "raise OnImmortality where that is implemented"),
        new(BuffEventTriggerKind.Time, 21, "time", BuffTriggerWiring.Scheduled,
            "scheduled by the handler at the row's delay_time, measured from the buff's start, and " +
            "cancelled when the buff ends. delay_time is an offset, not a period: buff 25106 carries " +
            "six rows at 1s/30s/60s/90s/120s/150s over a 180s buff, and buff 32463 four rows at " +
            "7s/14s/21s/28s over 29s. A negative delay_time is that far before the buff's end " +
            "(buff 27016: duration 23000, delay -3000). Separate from buff_tick_effects, which the " +
            "buff's own tick applies: no time row repeats an effect that its buff also ticks " +
            "(0 of the 256 rows share a buff_id and effect_id with buff_tick_effects)"),
        new(BuffEventTriggerKind.KillAny, 22, "kill_any", BuffTriggerWiring.UnitEvent,
            "the owner's OnKill, the same raise site as id 18. The content gives the two kinds no " +
            "row-level difference: no buff carries both, both are authored on 'after killing, do X' " +
            "rows (31810 is literally 'kill 하고 나서 스코어 획득') and both use target_agent_id=2 " +
            "where they name a unit. If the client means 'killing blow' by one and 'any kill credit' " +
            "by the other, that split has to be made at the raise site, which sees only the killer"),
        new(BuffEventTriggerKind.Any, 23, "any", BuffTriggerWiring.BuffEvent,
            "the buff's OnDispelled and OnTimeout together: every way this buff can end. 185 of the " +
            "242 rows sit on a buff that also authors started/landing rows"),
        new(BuffEventTriggerKind.RemoveNeedBuff, 24, "remove_need_buff", BuffTriggerWiring.BuffEvent,
            "the buff's OnRequiredBuffLost, raised by BuffTemplate.Dispel just before it exits the " +
            "buffs whose buffs.require_buff_id names the buff being removed"),
        new(BuffEventTriggerKind.UserCancel, 25, "user_cancel", BuffTriggerWiring.BuffEvent,
            "the buff's OnUserCancel, raised by CSRemoveBuffPacket - the client asking for the buff " +
            "to be cancelled"),
        new(BuffEventTriggerKind.UseSkill, 26, "use_skill", BuffTriggerWiring.UnitEvent,
            "the owner's OnSkillUse, raised by Unit.OnSkillEnd. That is the only skill-lifecycle hook " +
            "outside Skill.cs (which task B2 must not change), so it fires when a cast resolves rather " +
            "than when it starts"),
        new(BuffEventTriggerKind.RemoveStealth, 27, "remove_stealth", BuffTriggerWiring.BuffEvent,
            "the buff's OnStealthRemoved, raised by Buffs.RemoveStealth for each stealth buff it " +
            "removes. That method's only caller is the CancelStealth special effect; a stealth buff " +
            "removed by attacking goes through the remove-on flags instead (task B5)"),
        new(BuffEventTriggerKind.SkillController, 28, "skill_controller", BuffTriggerWiring.NotApplicable,
            "no enabled rows, and nothing to raise on: buffs.skill_controller_id is loaded into " +
            "BuffTemplate and read by nothing, so a controller never starts or stops"),
        new(BuffEventTriggerKind.Absorption, 29, "absorption", BuffTriggerWiring.BuffEvent,
            "the buff's OnAbsorptionConsumed, raised by Buff.ConsumeCharge when the last charge is " +
            "absorbed. All 7 rows are shields (buff 95/426-429/13802/13803) whose effect is the " +
            "explosion '모든 피해를 흡수한 보호막은 폭발하여'"),
        new(BuffEventTriggerKind.RemoveAura, 30, "remove_aura", BuffTriggerWiring.NotApplicable,
            "no enabled rows, and no aura state to observe: buffs.aura_radius/aura_relation_id are " +
            "loaded and nothing tracks an aura leaving"),
        new(BuffEventTriggerKind.Breaker, 31, "breaker", BuffTriggerWiring.NotApplicable,
            "the buff_breakers table (buff -> buff_tag_id, 100+ rows) is not loaded anywhere in this " +
            "build - it is task B5. The hook belongs on the removal path (Buffs.TriggerRemoveOn), not here"),
        new(BuffEventTriggerKind.DamageMelee, 32, "damage_melee", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamageMelee: the attacker's own event, typed the way OnDamagedMelee is"),
        new(BuffEventTriggerKind.DamageSpell, 33, "damage_spell", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamageSpell"),
        new(BuffEventTriggerKind.DamageRanged, 34, "damage_range", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamageRanged (the database spells this one 'damage_range')"),
        new(BuffEventTriggerKind.DamageSiege, 35, "damage_siege", BuffTriggerWiring.UnitEvent,
            "the owner's OnDamageSiege"),
        new(BuffEventTriggerKind.System, 36, "system", BuffTriggerWiring.NotApplicable,
            "one row, on buff 32459 (다루 변신, the only buffs.system='t' row carrying this kind). " +
            "The kind has no event of its own, and that buff already drives the same transformation " +
            "through its own started/timeout rows, so there is nothing this row can be raised by")
    ];

    /// <summary>The whole table, in <c>enum_buff_trigger_events</c> id order.</summary>
    public static IReadOnlyList<BuffTriggerKindBinding> All => Table;

    /// <summary>
    /// The row for a kind, or null when the kind is not one of the content database's ids (a
    /// <c>buff_triggers</c> row written for a newer client would decode to one).
    /// </summary>
    public static BuffTriggerKindBinding? For(BuffEventTriggerKind kind)
    {
        foreach (var binding in Table)
        {
            if (binding.Kind == kind)
                return binding;
        }

        return null;
    }

    /// <summary>How a kind is raised; an unclassified kind is reported as not applicable.</summary>
    public static BuffTriggerWiring WiringOf(BuffEventTriggerKind kind) =>
        For(kind)?.Wiring ?? BuffTriggerWiring.NotApplicable;

    /// <summary>
    /// When a <c>time</c> row fires, in milliseconds from the moment the buff was applied.
    /// </summary>
    /// <param name="delayTimeMs">The row's signed <c>buff_triggers.delay_time</c>.</param>
    /// <param name="buffDurationMs">
    /// The buff's duration in milliseconds; 0 for an unlimited buff. Only a negative
    /// <paramref name="delayTimeMs"/> reads it.
    /// </param>
    /// <remarks>
    /// A positive value is the offset the row fires at. The content authors a handful of rows as the
    /// time left instead - buff 27016 (운수 좋은 날) has duration 23000 with delay -3000, buff
    /// 20392 (기갑병 탑승) duration 300000 with delay -10000 - so a negative value is read from the
    /// buff's end, and clamps to 0 (fire at once) when the buff has no end or the offset is already
    /// past.
    /// </remarks>
    public static uint ResolveTimeOffsetMs(int delayTimeMs, int buffDurationMs)
    {
        if (delayTimeMs >= 0)
            return (uint)delayTimeMs;

        if (buffDurationMs <= 0)
            return 0;

        return (uint)Math.Max(0, buffDurationMs + delayTimeMs);
    }
}
