namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// The enabled <c>unit_reqs</c> rows that sit on quest Start components in the 10.0.2.13 content DB
/// (<c>owner_type = 'QuestComponent'</c> joined through <c>quest_components.component_kind_id = 2</c>),
/// copied verbatim. Checked in because the real tables are not reachable from a test run; the repo's
/// compact.sqlite3 is a placeholder.
/// </summary>
/// <remarks>
/// Regenerate <see cref="StartKinds"/> with
/// <c>select u.kind_id, count(*), count(distinct u.owner_id), count(distinct qc.quest_context_id) from unit_reqs u
/// join quest_components qc on qc.id = u.owner_id where u.owner_type = 'QuestComponent' and u.enable = 't'
/// and qc.component_kind_id = 2 group by u.kind_id order by u.kind_id</c>
/// and <see cref="AffectedRows"/> with the same join restricted to the kinds in <see cref="AuditUnknownKinds"/>
/// plus <see cref="NationMemberKind"/>, grouped by <c>kind_id, value1, value2, value3, display_msg</c> with
/// <c>count(*)</c>, <c>count(distinct qc.quest_context_id)</c>, <c>min(u.id)</c>, <c>min(qc.quest_context_id)</c>
/// and <c>min(qc.id)</c>.
/// </remarks>
public static class QuestStartRequirementContentSnapshot
{
    /// <summary>Distinct quests with at least one enabled unit_reqs row on their Start component.</summary>
    public const int QuestsWithStartRequirements = 5026;

    /// <summary>
    /// Kinds whose rows fell through to URK_UNKNOWN in the switch the audit was written against
    /// (fae0473a, 2026-09-20); #1656 gave each one a case.
    /// </summary>
    public static readonly uint[] AuditUnknownKinds = [62, 64, 86, 91, 113, 122, 123, 124, 125, 129, 137];

    /// <summary>Distinct quests whose Start component carries a row of one of those kinds.</summary>
    public const int AuditUnknownQuests = 163;

    /// <summary>
    /// Kinds that still fail closed after #1656: the faction change chain (FactionPower 123,
    /// FactionChangePossibleFromTo 124, FactionChangeCooldown 125) has its rule recovered but no
    /// server-side state to read.
    /// </summary>
    public static readonly uint[] StillClosedKinds = [123, 124, 125];

    /// <summary>Distinct quests on the faction change chain.</summary>
    public const int FactionChangeChainQuests = 18;

    /// <summary>
    /// NationMember 60 evaluates (faction id at least 1000) but no player nation exists on the server,
    /// so its rows stay closed too; listed with the affected rows for that reason.
    /// </summary>
    public const uint NationMemberKind = 60;

    /// <summary>Per kind: enabled rows on Start components, distinct components and distinct quests.</summary>
    public static readonly (uint Kind, string Name, int Rows, int Components, int Quests)[] StartKinds =
    [
        (1, "level", 35, 35, 35),
        (2, "ability", 35, 13, 13),
        (3, "race", 3, 3, 3),
        (4, "gender", 21, 21, 21),
        (9, "equip_item", 35, 32, 32),
        (10, "own_item", 223, 164, 164),
        (15, "buff", 186, 138, 138),
        (22, "nobuff", 368, 142, 142),
        (30, "no_buff_tag", 6, 6, 6),
        (31, "complete_quest_context", 3471, 3268, 3268),
        (32, "progress_quest_context", 106, 104, 104),
        (33, "ready_quest_context", 2, 2, 2),
        (36, "except_complete_quest_context", 582, 326, 326),
        (37, "precomplete_quest_context", 16, 16, 16),
        (40, "faction_match", 19, 19, 19),
        (42, "mother_faction", 1080, 1079, 1079),
        (43, "actability_point", 153, 152, 152),
        (45, "honor_point", 2, 2, 2),
        (46, "crime_record", 27, 27, 27),
        (47, "jury_point", 6, 6, 6),
        (51, "in_zone", 3, 3, 3),
        (55, "faction_match_only", 38, 28, 28),
        (56, "mother_faction_only", 926, 926, 926),
        (58, "faction_match_only_not", 21, 21, 21),
        (59, "mother_faction_only_not", 26, 26, 26),
        (60, "nation_member", 64, 64, 64),
        (62, "dominion_member_at_pos", 66, 66, 66),
        (64, "housing", 2, 2, 2),
        (69, "max_level", 11, 11, 11),
        (70, "expedition_owner", 5, 5, 5),
        (71, "expedition_member", 28, 28, 28),
        (72, "except_progress_quest_context", 580, 249, 249),
        (73, "except_ready_quest_context", 517, 193, 193),
        (74, "own_item_not", 83, 26, 26),
        (76, "own_quest_item_group", 2, 2, 2),
        (79, "hero", 17, 17, 17),
        (86, "dominion_member", 14, 14, 14),
        (91, "is_resident", 36, 36, 36),
        (98, "buff_tag", 34, 34, 34),
        (99, "labor_power_margin_local", 1, 1, 1),
        (100, "heir_level", 6, 6, 6),
        (101, "in_zone_group", 68, 68, 68),
        (104, "own_appellation", 3, 3, 3),
        (108, "raid_owner", 1, 1, 1),
        (109, "vice_raid_owner", 1, 1, 1),
        (110, "raid_member", 1, 1, 1),
        (113, "achievement_complete", 24, 3, 3),
        (119, "not_hero", 3, 3, 3),
        (122, "gear_score", 15, 15, 15),
        (123, "faction_power", 24, 18, 18),
        (124, "faction_change_possible_from_to", 12, 12, 12),
        (125, "faction_change_cooldown", 8, 8, 8),
        (127, "leadership_period", 6, 6, 6),
        (128, "not_hero_not_candidate", 18, 18, 18),
        (129, "conflict_zone_state", 1, 1, 1),
        (134, "equip_item_tag", 5, 5, 5),
        (137, "tower_def_step", 29, 15, 15),
    ];

    /// <summary>
    /// Every distinct operand tuple of the affected kinds on Start components, with its row and quest
    /// counts and the lowest row, quest and component id that carries it.
    /// </summary>
    public static readonly (uint Kind, uint Value1, uint Value2, uint Value3, bool DisplayMessage, int Rows, int Quests, uint RowId, uint QuestId, uint ComponentId)[] AffectedRows =
    [
        (60, 0, 0, 0, true, 64, 64, 44653, 5932, 25589),
        (62, 0, 0, 0, true, 66, 66, 66791, 9356, 40773),
        (64, 12, 1, 0, true, 2, 2, 50218, 7673, 32763),
        (86, 0, 0, 0, true, 14, 14, 51914, 7911, 33882),
        (91, 0, 0, 0, false, 1, 1, 53537, 8343, 35802),
        (91, 0, 0, 0, true, 34, 34, 53516, 8341, 35791),
        (91, 5, 0, 0, true, 1, 1, 53238, 8302, 35573),
        (113, 1888, 0, 0, true, 1, 1, 80416, 11205, 48860),
        (113, 1892, 0, 0, true, 1, 1, 80417, 11205, 48860),
        (113, 1896, 0, 0, true, 1, 1, 80418, 11205, 48860),
        (113, 1900, 0, 0, true, 1, 1, 80419, 11205, 48860),
        (113, 1904, 0, 0, true, 1, 1, 80420, 11205, 48860),
        (113, 1908, 0, 0, true, 1, 1, 80421, 11205, 48860),
        (113, 1912, 0, 0, true, 1, 1, 80422, 11205, 48860),
        (113, 1916, 0, 0, true, 1, 1, 80423, 11205, 48860),
        (113, 1920, 0, 0, true, 1, 1, 80424, 11205, 48860),
        (113, 1924, 0, 0, true, 1, 1, 80425, 11205, 48860),
        (113, 1928, 0, 0, true, 1, 1, 80426, 11205, 48860),
        (113, 1932, 0, 0, true, 1, 1, 80427, 11205, 48860),
        (113, 1936, 0, 0, true, 1, 1, 80428, 11205, 48860),
        (113, 1940, 0, 0, true, 1, 1, 80429, 11205, 48860),
        (113, 1944, 0, 0, true, 1, 1, 80430, 11205, 48860),
        (113, 1948, 0, 0, true, 1, 1, 80431, 11205, 48860),
        (113, 1952, 0, 0, true, 1, 1, 80432, 11205, 48860),
        (113, 1956, 0, 0, true, 1, 1, 80433, 11205, 48860),
        (113, 1960, 0, 0, true, 1, 1, 80434, 11205, 48860),
        (113, 1964, 0, 0, true, 1, 1, 80435, 11205, 48860),
        (113, 1965, 0, 0, true, 1, 1, 80436, 11205, 48860),
        (113, 1968, 0, 0, true, 1, 1, 80437, 11205, 48860),
        (113, 4841, 0, 0, true, 1, 1, 70546, 10181, 44277),
        (113, 5431, 0, 0, true, 1, 1, 79670, 11179, 48737),
        (122, 0, 9000, 0, true, 6, 6, 77618, 9432, 41095),
        (122, 0, 10000, 0, true, 3, 3, 78282, 10423, 45363),
        (122, 0, 11000, 0, true, 6, 6, 79064, 11096, 48321),
        (123, 114, 1, 0, true, 10, 10, 67999, 9432, 41095),
        (123, 148, 1, 0, true, 7, 7, 68002, 9516, 41524),
        (123, 149, 1, 0, true, 7, 7, 67996, 9517, 41528),
        (124, 114, 148, 0, true, 2, 2, 68003, 9516, 41524),
        (124, 114, 149, 0, true, 2, 2, 68007, 9585, 41842),
        (124, 148, 114, 0, true, 2, 2, 68010, 9432, 41095),
        (124, 148, 149, 0, true, 2, 2, 67997, 9517, 41528),
        (124, 149, 114, 0, true, 2, 2, 68000, 9583, 41832),
        (124, 149, 148, 0, true, 2, 2, 68083, 9518, 41535),
        (125, 0, 0, 0, true, 8, 8, 68106, 9515, 41520),
        (129, 1, 0, 0, true, 1, 1, 74226, 10556, 45996),
        (137, 140, 165, 1, true, 1, 1, 75203, 10555, 45992),
        (137, 140, 165, 2, true, 14, 14, 75317, 10558, 46004),
        (137, 140, 165, 3, true, 14, 14, 75340, 10558, 46004),
    ];
}
