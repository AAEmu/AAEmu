namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The 10.0.2.13 content DB's <c>enum_unit_formula_kinds</c> (60 rows, ids 1-68 with 3, 4, 8, 9, 56,
/// 57, 58 and 62 absent) and <c>enum_unit_owner_types</c> (8 rows), copied verbatim.
/// </summary>
/// <remarks>
/// Checked in because the real tables are not reachable from a test run: AAEmu.Game/Data/compact.sqlite3
/// is an 888-byte placeholder and <c>SqliteTestBase</c> builds a minimal in-memory schema of its own.
/// Regenerate with
/// <c>sqlite3 C:/AA/game/db/game_decrypted.sqlite3 "select id, name from enum_unit_formula_kinds order by id"</c>
/// and keep the id order. <c>UnitFormulaKindContentTests</c> asserts the enums against these rows, so a
/// row the loader would drop — which is what the eight ids above 46 and the butler owner did — fails the
/// test run instead of quietly discarding content.
/// </remarks>
public static class UnitFormulaKindContentSnapshot
{
    /// <summary>Every row of <c>enum_unit_formula_kinds</c>, ordered by id.</summary>
    public static readonly (uint Id, string Name)[] Kinds =
    [
        (1, "melee_critical"),
        (2, "melee_anti_miss"),
        (5, "melee_parry"),
        (6, "ranged_critical"),
        (7, "ranged_anti_miss"),
        (10, "spell_critical"),
        (11, "spell_anti_miss"),
        (12, "level_dps"),
        (13, "level_mana"),
        (14, "max_health"),
        (15, "max_mana"),
        (16, "health_regen"),
        (17, "mana_regen"),
        (18, "armor"),
        (19, "magic_resist"),
        (20, "facet"),
        (21, "melee_dps_inc"),
        (22, "str"),
        (23, "dex"),
        (24, "sta"),
        (25, "int"),
        (26, "spi"),
        (27, "fai"),
        (28, "ranged_dps_inc"),
        (29, "spell_dps_inc"),
        (30, "casting_tolerance"),
        (31, "persistent_health_regen"),
        (32, "persistent_mana_regen"),
        (33, "base_miss_percent"),
        (34, "kill_exp"),
        (35, "melee_critical_bonus"),
        (36, "ranged_critical_bonus"),
        (37, "spell_critical_bonus"),
        (38, "ranged_parry"),
        (39, "incoming_melee_damage_add"),
        (40, "incoming_ranged_damage_add"),
        (41, "incoming_spell_damage_add"),
        (42, "heal_critical"),
        (43, "heal_dps_inc"),
        (44, "heal_critical_bonus"),
        (45, "block"),
        (46, "dodge"),
        (47, "mass"),
        (48, "steering_speed"),
        (49, "reverse_velocity"),
        (50, "melee_dynamic_normalizable"),
        (51, "ranged_dynamic_normalizable"),
        (52, "magic_dynamic_normalizable"),
        (53, "heal_dynamic_normalizable"),
        (54, "defence_dynamic_normalizable"),
        (55, "music_dynamic_normalizable"),
        (59, "battle_resist"),
        (60, "flexibility"),
        (61, "incoming_damage_mul"),
        (63, "ignore_shield_bonus_mul"),
        (64, "melee_anti_miss_mul"),
        (65, "ranged_anti_miss_mul"),
        (66, "spell_anti_miss_mul"),
        (67, "bulls_eye"),
        (68, "casting_time_mul"),
    ];

    /// <summary>Every row of <c>enum_unit_owner_types</c>, ordered by id.</summary>
    public static readonly (uint Id, string Name)[] Owners =
    [
        (0, "character"),
        (1, "npc"),
        (2, "slave"),
        (3, "housing"),
        (4, "transfer"),
        (5, "mate"),
        (6, "shipyard"),
        (7, "butler"),
    ];
}
