using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// Which unit an item proc casts its skill at.
/// </summary>
public enum ItemProcTargetSide
{
    /// <summary>The unit wearing the item.</summary>
    Owner,
    /// <summary>The unit on the other side of the event: the victim of the owner's hit, the attacker that hit the
    /// owner, the unit the owner healed, or the target of the skill the owner fired.</summary>
    Other,
    /// <summary>A target type no proc row ships (a position, an item, a doodad): the proc does not fire.</summary>
    Unsupported
}

/// <summary>
/// The trigger, chance, target and cooldown decisions of <c>item_procs</c> (202 rows). Kinds are
/// <c>enum_proc_chance_type</c>; the populated ones are 1-7, 9, 10, 13, 14, 17, 18 and 19. The client reads the
/// same twelve columns in the native loader (LoadItemProcDescs: id, chance_kind_id, chance_param,
/// chance_rate, cooldown_sec, description, finisher, item_level_based_chance_bonus, or_unit_reqs, skill_id,
/// trigger_skill_id, trigger_tag_id), packs finisher and or_unit_reqs into two flag bits and attaches the
/// <c>unit_reqs</c> rows with owner_type "ItemProc" to each row.
/// </summary>
public static class ItemProcRules
{
    private static readonly ProcChanceKind[] HitAnyOnly = [ProcChanceKind.HitAny];
    private static readonly ProcChanceKind[] TakeDamageAnyOnly = [ProcChanceKind.TakeDamageAny];
    private static readonly ProcChanceKind[] HealOnly = [ProcChanceKind.HitHeal];
    private static readonly ProcChanceKind[] HealAndCritical = [ProcChanceKind.HitHeal, ProcChanceKind.HitHealCrit];
    private static readonly ProcChanceKind[] SkillFiredOnly = [ProcChanceKind.FireSkill];

    /// <summary>
    /// The kinds one outgoing hit raises on the attacker: <c>hit_any</c> (1) for every hit, the kind of the
    /// damage type (<c>hit_melee</c> 2, <c>hit_spell</c> 4, <c>hit_ranged</c> 6, <c>hit_siege</c> 8) and, on a
    /// critical, its critical twin (3, 5, 7). A critical melee hit is still a melee hit and still a hit, so a
    /// proc on kind 2 rolls on criticals too; only kind 3 asks for the critical.
    /// </summary>
    public static IReadOnlyList<ProcChanceKind> HitKinds(DamageType damageType, bool critical) => damageType switch
    {
        DamageType.Melee => critical
            ? [ProcChanceKind.HitAny, ProcChanceKind.HitMelee, ProcChanceKind.HitMeleeCrit]
            : [ProcChanceKind.HitAny, ProcChanceKind.HitMelee],
        DamageType.Magic => critical
            ? [ProcChanceKind.HitAny, ProcChanceKind.HitSpell, ProcChanceKind.HitSpellCrit]
            : [ProcChanceKind.HitAny, ProcChanceKind.HitSpell],
        DamageType.Ranged => critical
            ? [ProcChanceKind.HitAny, ProcChanceKind.HitRanged, ProcChanceKind.HitRangedCrit]
            : [ProcChanceKind.HitAny, ProcChanceKind.HitRanged],
        // Siege has no critical kind in the enum, and DamageEffect never rolls a siege critical.
        DamageType.Siege => [ProcChanceKind.HitAny, ProcChanceKind.HitSiege],
        _ => HitAnyOnly
    };

    /// <summary>
    /// The victim-side mirror of <see cref="HitKinds"/>: <c>take_damage_any</c> (9) for every hit taken, the
    /// kind of the damage type (10, 12, 14, 16) and its critical twin (11, 13, 15) when the hit was a critical.
    /// </summary>
    public static IReadOnlyList<ProcChanceKind> TakeDamageKinds(DamageType damageType, bool critical) => damageType switch
    {
        DamageType.Melee => critical
            ? [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageMelee, ProcChanceKind.TakeDamageMeleeCrit]
            : [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageMelee],
        DamageType.Magic => critical
            ? [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageSpell, ProcChanceKind.TakeDamageSpellCrit]
            : [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageSpell],
        DamageType.Ranged => critical
            ? [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageRanged, ProcChanceKind.TakeDamageRangedCrit]
            : [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageRanged],
        DamageType.Siege => [ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageSiege],
        _ => TakeDamageAnyOnly
    };

    /// <summary>
    /// The kinds a heal raises on the healer: <c>hit_heal</c> (17) for every heal and <c>hit_heal_crit</c> (18) on
    /// a critical heal as well. 7 rows sit on 17 and 3 on 18.
    /// </summary>
    public static IReadOnlyList<ProcChanceKind> HealKinds(bool critical) => critical ? HealAndCritical : HealOnly;

    /// <summary>
    /// The kind the fire edge of a cast raises on the caster: <c>fire_skill</c> (19, 26 rows). <c>hit_skill</c>
    /// (20) has no row in 10.0.2.13 and nothing raises it.
    /// </summary>
    public static IReadOnlyList<ProcChanceKind> SkillFiredKinds() => SkillFiredOnly;

    /// <summary>Whether a hit type is one of the three criticals DamageEffect rolls.</summary>
    public static bool IsCritical(SkillHitType hitType) =>
        hitType is SkillHitType.MeleeCritical or SkillHitType.SpellCritical or SkillHitType.RangedCritical;

    /// <summary>
    /// Whether the unit_reqs check reads the wearer instead of the other side of the event. The take-damage
    /// kinds (9-16) are raised on the unit that was hit, so <c>other</c> is the attacker there and the bands
    /// that matter (procs 87, 114 and 153, all self-target take_damage_any) are the wearer's own. The hit and
    /// heal kinds read the other side: procs 173 and 198 are hit_heal rows whose band reads the healed unit.
    /// </summary>
    public static bool RequirementTargetIsOwner(ProcChanceKind chanceKind) => chanceKind switch
    {
        >= ProcChanceKind.TakeDamageAny and <= ProcChanceKind.TakeDamageSiege => true,
        _ => false
    };

    /// <summary>
    /// Whether the skill behind the event is the one the row asks for. <c>trigger_skill_id</c> (10 rows, all
    /// kind 19: proc 109 answers 11943 불협 화음 and nothing else) and <c>trigger_tag_id</c> (6 rows, tag 378
    /// "player skills", 782 skills: procs 174-179, the 노르예트 arena jewellery) are filters; a row that sets
    /// neither, 186 of the 202, answers any skill. No row sets both; one that did would ask for both.
    /// </summary>
    public static bool TriggerMatches(uint triggerSkillId, uint triggerTagId, uint firedSkillId, bool firedSkillHasTriggerTag)
    {
        if (triggerSkillId != 0 && firedSkillId != triggerSkillId)
            return false;
        if (triggerTagId != 0 && !firedSkillHasTriggerTag)
            return false;
        return true;
    }

    /// <summary>
    /// <c>finisher</c> ('t' on one row, proc 46 막타시치명타율증가 on sets 125 and 133, whose text reads "적을
    /// 죽일 때 마다", each time you kill an enemy): the proc answers only the hit that killed the victim.
    /// </summary>
    public static bool FinisherAllows(bool finisher, bool victimDied) => !finisher || victimDied;

    /// <summary>
    /// Whether the proc is still inside its <c>cooldown_sec</c> window (0-180 s across the table, 0 on the rows
    /// that never wait). The window starts at the last successful fire, see <see cref="StartsCooldown"/>.
    /// </summary>
    public static bool CooldownBlocks(DateTime lastProc, uint cooldownSec, DateTime now) =>
        cooldownSec > 0 && now < lastProc.AddSeconds(cooldownSec);

    /// <summary>
    /// Whether the proc skill's own cooldown still refuses the proc. A proc casts with <c>bypassGcd</c> to get
    /// past the shared global cooldown, and in <c>Skill.Use</c> that same flag skips the whole block holding
    /// <c>unit.Cooldowns.CheckCooldown(Template.Id)</c> (Skill.cs:314) and
    /// <see cref="SkillCooldownGateRules.ShouldWaitForCooldown"/> (Skill.cs:324), while the cast still arms that
    /// cooldown (ArmCooldowns, Skill.cs:1019). So procs 116-119, 121, 122, 124 and 125, whose skills carry a
    /// 60-180 s <c>cooldown_time</c> against a <c>cooldown_sec</c> of 0 or 1, would otherwise fire on every roll.
    /// Both gates hold: the window above and the skill's own cooldown here.
    /// </summary>
    public static bool SkillCooldownBlocks(bool skillCooldownActive) => skillCooldownActive;

    /// <summary>
    /// <c>chance_rate</c> is a plain percentage (0-100 across every row), rolled against a draw from 0..99: rate 0
    /// never passes, rate 100 always does, and a 15 % proc passes exactly 15 draws in 100.
    /// </summary>
    public static bool RollPasses(uint chanceRate, int roll) => roll < chanceRate;

    /// <summary>
    /// Which unit the proc skill is cast at, from the proc skill's own <c>skills.target_type_id</c>. Self (0,
    /// 162 rows) is the wearer. Friendly (1, 2 rows), hostile (4, 36) and any_unit (5, 2), the other 40 rows,
    /// are the unit on the other side of the event: proc 109's text is "즉시 적 대상에게 피해를 입히지만" (deals
    /// damage to the enemy target at once) and proc 111's is "대상에게... 생명력을 추가로 치유합니다" (heals the
    /// target for more), both on the target of the fired skill. The relation check is the skill's own: a
    /// hostile proc skill handed a friendly unit fails its target resolution and does not fire. Target types
    /// no row ships are unsupported.
    /// </summary>
    public static ItemProcTargetSide TargetSide(SkillTargetType procSkillTargetType) => procSkillTargetType switch
    {
        SkillTargetType.Self => ItemProcTargetSide.Owner,
        SkillTargetType.Friendly
            or SkillTargetType.Hostile
            or SkillTargetType.AnyUnit
            or SkillTargetType.AnyUnitAlways
            or SkillTargetType.Others
            or SkillTargetType.FriendlyOthers
            or SkillTargetType.GeneralUnit
            or SkillTargetType.IgnoreProtected => ItemProcTargetSide.Other,
        _ => ItemProcTargetSide.Unsupported
    };

    /// <summary>
    /// The cooldown starts only when the proc skill was really cast. A failed chance roll, a missing target, a
    /// requirement that did not hold or a cast the skill itself refused (out of range, no mana, silenced) leaves
    /// the proc ready for the next event.
    /// </summary>
    public static bool StartsCooldown(SkillResult result) => result == SkillResult.Success;

    /// <summary>
    /// A skill that an item proc cast does not raise procs of its own, on either side. All 34 damage effects
    /// behind proc skills carry <c>fire_proc</c> 't', so without this a 100 % <c>hit_any</c> proc with
    /// <c>cooldown_sec</c> 0 (rows exist at both values) would hit, proc, hit, proc without end.
    /// </summary>
    public static bool SourceMayProc(bool sourceIsItemProc) => !sourceIsItemProc;
}
