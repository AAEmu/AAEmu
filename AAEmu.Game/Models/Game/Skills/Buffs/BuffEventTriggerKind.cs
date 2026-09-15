namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// The <c>buff_triggers.event_id</c> values, one member per row of the content database's
/// <c>enum_buff_trigger_events</c> (10.0.2.13: 36 rows, 11,283 enabled <c>buff_triggers</c> rows).
/// <see cref="BuffTriggerKindRules"/> is the table that says how each one is raised.
/// </summary>
/// <remarks>
/// The names are the database's own, in this enum's casing: <c>damage_any</c> is
/// <see cref="Damage"/>, <c>remove_on_damaged</c> is <see cref="RemoveOnDamaged"/> and
/// <c>immotality</c> is <see cref="Immotality"/> (the database spells it that way - it is a typo in
/// the content, and keeping it makes a grep of either side find the other). The single exception is
/// <c>damage_range</c>, named <see cref="DamageRanged"/> so it reads beside the <c>damaged_*</c>
/// family; <see cref="BuffTriggerKindRules"/> carries the database spelling for every id.
/// </remarks>
public enum BuffEventTriggerKind
{
    Attack = 1,
    Attacked = 2,
    Damage = 3,
    Damaged = 4,
    Dispelled = 5,
    Timeout = 6,
    DamagedMelee = 7,
    DamagedRanged = 8,
    DamagedSpell = 9,
    DamagedSiege = 10,
    Landing = 11,
    Started = 12,
    RemoveOnMove = 13,
    ChannelingCancel = 14,
    RemoveOnDamaged = 15,
    Death = 16,
    Unmount = 17,
    Kill = 18,
    DamagedCollision = 19,
    Immotality = 20,
    Time = 21,
    KillAny = 22,
    Any = 23,
    RemoveNeedBuff = 24,
    UserCancel = 25,
    UseSkill = 26,
    RemoveStealth = 27,
    SkillController = 28,
    Absorption = 29,
    RemoveAura = 30,
    Breaker = 31,
    DamageMelee = 32,
    DamageSpell = 33,
    DamageRanged = 34,
    DamageSiege = 35,
    System = 36
}
