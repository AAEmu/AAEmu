//! Port of AAEmu.UnitTests/Game/Core/Managers/UnitManagers/DoodadGroupLoadingTests.cs
//!
//! `DoodadManager.LoadDoodadTemplates(connection)` is `DoodadData::load_doodad_templates`; the
//! C# fixture's empty `_templates` / `_allFuncGroups` are a default `DoodadData`. The C#
//! `GameException` is a `GameDataError`.

use super::*;

/// `CreateTestSchema()`.
fn connection() -> Connection {
    let conn = Connection::open_in_memory().unwrap();
    conn.execute_batch(
        "CREATE TABLE doodad_groups (
            id INTEGER PRIMARY KEY,
            guard_on_field_time INTEGER NOT NULL,
            is_export INTEGER NOT NULL,
            removed_by_house INTEGER NOT NULL
        );
        INSERT INTO doodad_groups (id, guard_on_field_time, is_export, removed_by_house)
        VALUES (0, 0, 0, 0), (7, 3600, 1, 0), (8, 172800, 0, 1);

        CREATE TABLE doodad_almighties (
            id INTEGER PRIMARY KEY,
            group_id INTEGER NOT NULL,
            model_kind_id INTEGER NOT NULL DEFAULT 0,
            faction_id INTEGER NOT NULL DEFAULT 0,
            once_one_man INTEGER, once_one_interaction INTEGER, mgmt_spawn INTEGER,
            percent INTEGER, min_time INTEGER, max_time INTEGER,
            use_creator_faction INTEGER, force_tod_top_priority INTEGER, milestone_id INTEGER,
            use_target_decal INTEGER, use_target_silhouette INTEGER, use_target_highlight INTEGER,
            target_decal_size REAL, sim_radius INTEGER, collide_ship INTEGER, collide_vehicle INTEGER,
            climate_id INTEGER, save_indun INTEGER, force_up_action INTEGER,
            parentable INTEGER, childable INTEGER, growth_time INTEGER,
            despawn_on_collision INTEGER, no_collision INTEGER, restrict_zone_id INTEGER
        );
        INSERT INTO doodad_almighties (id, group_id) VALUES (100, 0), (101, 7), (102, 8);",
    )
    .unwrap();
    conn
}

#[test]
fn load_doodad_templates_existing_groups_publishes_templates_with_metadata() {
    for (template_id, group_id, guard_time, is_export, removed_by_house) in [
        (100u32, 0u32, 0u32, false, false),
        (101, 7, 3600, true, false),
        (102, 8, 172_800, false, true),
    ] {
        let conn = connection();
        let mut manager = DoodadData::default();

        manager.load_doodad_templates(&conn).unwrap();

        let template = manager.template(template_id).expect("template");
        assert_eq!(template.group_id, group_id);
        assert_eq!(template.group.id, group_id);
        assert_eq!(template.group.guard_on_field_time, guard_time);
        assert_eq!(template.group.is_export, is_export);
        assert_eq!(template.group.removed_by_house, removed_by_house);
    }
}

#[test]
fn load_doodad_templates_missing_group_rejects_template_with_identifiable_error() {
    let conn = connection();
    conn.execute(
        "UPDATE doodad_almighties SET group_id = 999 WHERE id = 101",
        [],
    )
    .unwrap();
    let mut manager = DoodadData::default();

    let error = manager
        .load_doodad_templates(&conn)
        .expect_err("missing group");

    let message = error.to_string();
    assert!(message.contains("999"), "{message}");
    assert!(message.contains("101"), "{message}");
    assert!(manager.template(101).is_none());
}
