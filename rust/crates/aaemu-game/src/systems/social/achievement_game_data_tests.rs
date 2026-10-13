//! Port of AAEmu.UnitTests/Game/GameData/AchiementGameDataTests.cs (`AchievementGameDataTests`)
//! plus loader tests for `AchievementGameData.Load` (in-memory tables and the gated real
//! compact.sqlite3 check).

use rusqlite::Connection;

use super::achievement::{AchievementGameData, CharRecords};

#[test]
fn can_create_instance() {
    let instance = AchievementGameData::default();
    assert!(instance.achievements.is_empty());
    assert!(instance.char_records.is_empty());
    assert!(instance.achievement_objectives.is_empty());
    assert!(instance.pre_completed_achievements.is_empty());
}

#[test]
fn new_instances_are_independent() {
    let instance1 = AchievementGameData::default();
    let mut instance2 = AchievementGameData::default();
    // Distinct objects (C# `IsNotSameReferenceAs`): changing one leaves the other untouched.
    assert!(!std::ptr::eq(&instance1, &instance2));
    instance2.char_records.insert(
        1,
        CharRecords {
            id: 1,
            ..Default::default()
        },
    );
    assert!(instance1.char_records.is_empty());
    assert_eq!(instance2.char_records.len(), 1);
}

fn schema(c: &Connection) {
    c.execute_batch(
        "CREATE TABLE achievements (id INT, name TEXT, summary TEXT, description TEXT,
            category_id INT, sub_category_id INT, parent_achievement_id INT, is_active NUM,
            is_hidden NUM, priority INT, or_unit_reqs NUM, complete_or NUM, complete_num INT,
            item_id INT, icon_id INT);
         CREATE TABLE achievement_objectives (id INT, achievement_id INT, or_unit_reqs NUM,
            record_id INT);
         CREATE TABLE pre_completed_achievements (id INT, my_achievement_id INT,
            completed_achievement_id INT);
         CREATE TABLE char_records (id INT, kind_id INT, value1 INT, value2 INT);",
    )
    .unwrap();
}

#[test]
fn load_reads_and_groups_the_tables() {
    let c = Connection::open_in_memory().unwrap();
    schema(&c);
    c.execute_batch(
        "INSERT INTO achievements VALUES (10, 'n', 's', 'd', 1, 2, 3, 't', 'f', 4, 'f', 't', 5, 6, 7);
         INSERT INTO achievements VALUES (10, 'dup', '', '', 9, 9, 9, 1, 1, 9, 1, 1, 9, 9, 9);
         INSERT INTO achievements VALUES (11, 'n', '', '', NULL, NULL, NULL, 1, 0, NULL, 1, 0,
            NULL, NULL, NULL);
         INSERT INTO achievement_objectives VALUES (5, 10, 'f', 100), (2, 10, 1, 101),
            (3, 11, 0, 102);
         INSERT INTO pre_completed_achievements VALUES (1, 11, 10), (2, 12, 10);
         INSERT INTO char_records VALUES (100, 3, 7, 8);",
    )
    .unwrap();
    let d = AchievementGameData::load(&c).unwrap();

    // TryAdd: the first row of an id wins.
    let a = &d.achievements[&10];
    assert_eq!(
        (a.category_id, a.sub_category_id, a.parent_achievement_id),
        (1, 2, 3)
    );
    assert_eq!(
        (a.priority, a.complete_num, a.item_id, a.icon_id),
        (4, 5, 6, 7)
    );
    // The text 't' / 'f' columns load as text booleans (not as the C# `GetInt64 != 0`, which
    // would make every one of them false).
    assert!(a.is_active && a.complete_or && !a.is_hidden && !a.or_unit_reqs);
    // NULL columns fall back to 0; integer booleans read normally.
    let b = &d.achievements[&11];
    assert_eq!((b.category_id, b.priority, b.item_id), (0, 0, 0));
    assert!(b.is_active && b.or_unit_reqs && !b.is_hidden && !b.complete_or);

    // Objectives are grouped by achievement_id and ordered by objective id (the table has no
    // ORDER BY).
    assert_eq!(
        d.achievement_objectives[&10]
            .iter()
            .map(|o| o.id)
            .collect::<Vec<_>>(),
        [2, 5]
    );
    assert!(d.achievement_objectives[&10][0].or_unit_reqs);
    assert!(!d.achievement_objectives[&10][1].or_unit_reqs);
    assert_eq!(d.achievement_objectives[&11][0].record_id, 102);
    // ... and linked onto the achievement itself in that same order.
    assert_eq!(a.achievement_objectives, d.achievement_objectives[&10]);
    assert_eq!(b.achievement_objectives, d.achievement_objectives[&11]);

    let pre = &d.pre_completed_achievements[&10];
    assert_eq!(
        pre.iter().map(|p| p.my_achievement_id).collect::<Vec<_>>(),
        [11, 12]
    );
    assert_eq!(
        d.char_records[&100],
        CharRecords {
            id: 100,
            kind_id: 3,
            value1: 7,
            value2: 8
        }
    );
}

#[test]
fn load_links_objectives_in_id_order_and_keeps_orphans_grouped() {
    let c = Connection::open_in_memory().unwrap();
    schema(&c);
    c.execute_batch(
        "INSERT INTO achievements VALUES (20, 'n', '', '', 1, 1, 0, 't', 'f', 0, 'f', 'f', 0, 0, 0);
         INSERT INTO achievement_objectives VALUES
            (9, 20, 'f', 100), (4, 20, 't', 101), (7, 20, 'f', 102), (1, 99, 'f', 103);",
    )
    .unwrap();
    let d = AchievementGameData::load(&c).unwrap();

    let linked = &d.achievements[&20].achievement_objectives;
    assert_eq!(linked.iter().map(|o| o.id).collect::<Vec<_>>(), [4, 7, 9]);
    assert!(linked[0].or_unit_reqs);
    assert!(linked.iter().all(|o| o.achievement_id == 20));
    assert_eq!(linked, &d.achievement_objectives[&20]);

    // An objective whose achievement row is missing stays in the grouped map only.
    assert_eq!(d.achievement_objectives[&99].len(), 1);
    assert!(!d.achievements.contains_key(&99));
}

#[test]
fn text_boolean_columns_follow_get_boolean_from_string() {
    let c = Connection::open_in_memory().unwrap();
    schema(&c);
    c.execute_batch(
        "INSERT INTO achievements VALUES (1, 'n', '', '', 0, 0, 0, 't', 'f', 0, 'f', 't', 0, 0, 0);
         INSERT INTO achievements VALUES (2, 'n', '', '', 0, 0, 0, '1', '0', 0, '1', '0', 0, 0, 0);
         INSERT INTO achievements VALUES (3, 'n', '', '', 0, 0, 0, 'f', 't', 0, 't', 'f', 0, 0, 0);
         INSERT INTO achievements VALUES (4, 'n', '', '', 0, 0, 0, NULL, NULL, 0, NULL, NULL, 0, 0, 0);
         INSERT INTO achievement_objectives VALUES (1, 1, 't', 1), (2, 1, 'f', 1),
            (3, 1, '1', 1), (4, 1, NULL, 1);",
    )
    .unwrap();
    let d = AchievementGameData::load(&c).unwrap();

    assert!(d.achievements[&1].is_active && d.achievements[&1].complete_or);
    assert!(!d.achievements[&1].is_hidden && !d.achievements[&1].or_unit_reqs);
    assert!(d.achievements[&2].is_active && d.achievements[&2].or_unit_reqs);
    assert!(!d.achievements[&2].complete_or && !d.achievements[&2].is_hidden);
    assert!(!d.achievements[&3].is_active && d.achievements[&3].is_hidden);
    assert!(d.achievements[&3].or_unit_reqs && !d.achievements[&3].complete_or);
    let nulls = &d.achievements[&4];
    assert!(!nulls.is_active && !nulls.is_hidden && !nulls.or_unit_reqs && !nulls.complete_or);

    let objectives = &d.achievements[&1].achievement_objectives;
    assert_eq!(
        objectives
            .iter()
            .map(|o| o.or_unit_reqs)
            .collect::<Vec<_>>(),
        [true, false, true, false]
    );
}

#[test]
fn load_duplicate_char_record_fails() {
    // C# `_charRecords.Add` throws on a duplicate id.
    let c = Connection::open_in_memory().unwrap();
    schema(&c);
    c.execute_batch("INSERT INTO char_records VALUES (1, 1, 0, 0), (1, 2, 0, 0);")
        .unwrap();
    assert!(AchievementGameData::load(&c).is_err());
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn achievement_game_data_real_compact_db() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let c = Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
    let d = AchievementGameData::load(&c).unwrap();
    let count =
        |sql: &str| -> usize { c.query_row(sql, [], |r| r.get::<_, i64>(0)).unwrap() as usize };
    assert_eq!(
        d.achievements.len(),
        count("SELECT COUNT(DISTINCT id) FROM achievements")
    );
    assert_eq!(
        d.achievement_objectives
            .values()
            .map(Vec::len)
            .sum::<usize>(),
        count("SELECT COUNT(*) FROM achievement_objectives")
    );
    assert_eq!(
        d.pre_completed_achievements
            .values()
            .map(Vec::len)
            .sum::<usize>(),
        count("SELECT COUNT(*) FROM pre_completed_achievements")
    );
    assert_eq!(
        d.char_records.len(),
        count("SELECT COUNT(*) FROM char_records")
    );

    // The shipped table stores these booleans as the text 't' / 'f' (`is_active` is 't' on every
    // row, `complete_or` is 't' on 184). Reading them as text booleans is what makes them visible.
    assert_eq!(
        d.achievements.values().filter(|a| a.is_active).count(),
        count("SELECT COUNT(*) FROM achievements WHERE is_active = 't'")
    );
    assert_eq!(
        d.achievements.values().filter(|a| a.complete_or).count(),
        count("SELECT COUNT(*) FROM achievements WHERE complete_or = 't'")
    );
    assert_eq!(
        d.achievements.values().filter(|a| a.is_hidden).count(),
        count("SELECT COUNT(*) FROM achievements WHERE is_hidden = 't'")
    );
    assert!(d.achievements.values().any(|a| a.complete_or));

    // Every objective is linked onto its achievement, in ascending objective-id order, and the
    // grouped map holds exactly the same lists (no `achievement_objectives` row dangles).
    assert_eq!(
        count(
            "SELECT COUNT(*) FROM achievements a
             LEFT JOIN achievement_objectives o ON o.achievement_id = a.id WHERE o.id IS NULL"
        ),
        0
    );
    assert_eq!(
        d.achievements
            .values()
            .map(|a| a.achievement_objectives.len())
            .sum::<usize>(),
        d.achievement_objectives
            .values()
            .map(Vec::len)
            .sum::<usize>()
    );
    for (id, a) in &d.achievements {
        assert_eq!(
            &a.achievement_objectives, &d.achievement_objectives[id],
            "achievement {id}"
        );
        assert!(a
            .achievement_objectives
            .windows(2)
            .all(|w| w[0].id < w[1].id));
        assert!(a
            .achievement_objectives
            .iter()
            .all(|o| o.achievement_id == *id));
    }

    // `record_id` resolves for every objective, and the `COMPLETE_ACHIEVEMENT` (kind 9) records —
    // the largest group — always name a real achievement in `value1`.
    assert_eq!(
        count(
            "SELECT COUNT(*) FROM achievement_objectives o
             LEFT JOIN char_records r ON r.id = o.record_id WHERE r.id IS NULL"
        ),
        0
    );
    assert_eq!(
        count(
            "SELECT COUNT(*) FROM char_records r
             LEFT JOIN achievements a ON a.id = r.value1 WHERE r.kind_id = 9 AND a.id IS NULL"
        ),
        0
    );
    assert!(
        count("SELECT COUNT(*) FROM char_records WHERE kind_id = 9")
            > count("SELECT COUNT(*) FROM char_records WHERE kind_id = 30")
    );
}
