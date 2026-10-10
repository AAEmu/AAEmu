//! Port of AAEmu.UnitTests/Game/GameData/CommonFarmGameDataTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/PublicFarmGuardTimeTests.cs and
//! AAEmu.UnitTests/Game/Core/Managers/PublicFarmManagerTests.cs (with the
//! `Utils/CommonFarmTestData.cs` helper).
//!
//! Time resolution: the C# `PlantTime` is a `DateTime` (100 ns ticks) and the boundary rows
//! move `now` by one tick; the Rust doodad keeps `PlantTime` in unix seconds and the checks take
//! `now` in unix milliseconds, so one tick maps to one millisecond (the boundary itself, `now <
//! PlantTime + guardTime`, is the same).

use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use rusqlite::{params, Connection};

use super::*;
use crate::data::client_files::ClientFileManager;
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::systems::npcs::doodad::templates::{DoodadGroups, DoodadTemplate};
use crate::test_support::test_server_with;
use crate::world::Entity;

/// `CommonFarmTestData`: an in-memory database with the three common farm tables.
struct CommonFarmTestData {
    connection: Connection,
}

impl CommonFarmTestData {
    fn new() -> Self {
        let connection = Connection::open_in_memory().unwrap();
        connection
            .execute_batch(
                "CREATE TABLE common_farms (
                    id INTEGER PRIMARY KEY,
                    farm_group_id INTEGER NOT NULL,
                    guard_time INTEGER NOT NULL,
                    comments TEXT NOT NULL
                );
                CREATE TABLE farm_groups (
                    id INTEGER PRIMARY KEY,
                    count INTEGER NOT NULL
                );
                CREATE TABLE farm_group_doodads (
                    id INTEGER PRIMARY KEY,
                    farm_group_id INTEGER NOT NULL,
                    doodad_id INTEGER NOT NULL,
                    item_id INTEGER NOT NULL
                );",
            )
            .unwrap();
        Self { connection }
    }

    fn insert_farm(&self, id: u32, kind: u8, guard_time: u32, comments: &str) {
        self.connection
            .execute(
                "INSERT INTO common_farms (id, farm_group_id, guard_time, comments)
                 VALUES (?1, ?2, ?3, ?4)",
                params![id, kind, guard_time, comments],
            )
            .unwrap();
    }

    fn execute(&self, sql: &str) {
        self.connection.execute_batch(sql).unwrap();
    }

    /// `new CommonFarmGameData().Load(connection)`.
    fn load(&self) -> PublicFarmData {
        let files = ClientFileManager::from_sources(&[], Path::new("."));
        let ctx = DataLoadCtx {
            conn: &self.connection,
            app_path: Path::new("."),
            files: &files,
            language: "en_us",
        };
        PublicFarmData::load(&ctx).unwrap()
    }
}

// ---------------------------------------------------------------------------------------------
// CommonFarmGameDataTests
// ---------------------------------------------------------------------------------------------

#[test]
fn get_farm_guard_time_matching_zone_and_kind_uses_matching_row() {
    let data = CommonFarmTestData::new();
    data.insert_farm(1, farm_group_kind::FARM, 3_600_000, "zone 100");
    data.insert_farm(2, farm_group_kind::RANCH, 7_200_000, "zone 200");
    data.insert_farm(3, farm_group_kind::FARM, 10_800_000, "zone 200");
    let game_data = data.load();

    assert_eq!(
        game_data.farm_guard_time(farm_group_kind::FARM, 200),
        10_800_000
    );
    assert_eq!(
        game_data.farm_guard_time(farm_group_kind::RANCH, 200),
        7_200_000
    );
}

#[test]
fn get_farm_guard_time_zone_not_found_falls_back_to_same_kind() {
    let data = CommonFarmTestData::new();
    data.insert_farm(1, farm_group_kind::RANCH, 7_200_000, "zone 100");
    data.insert_farm(2, farm_group_kind::FARM, 3_600_000, "zone 200");
    let game_data = data.load();

    assert_eq!(
        game_data.farm_guard_time(farm_group_kind::FARM, 300),
        3_600_000
    );
}

#[test]
fn get_farm_guard_time_kind_not_found_returns_zero() {
    for kind in [farm_group_kind::INVALID, farm_group_kind::STABLE] {
        let data = CommonFarmTestData::new();
        data.insert_farm(1, farm_group_kind::FARM, 3_600_000, "zone 100");
        let game_data = data.load();

        assert_eq!(game_data.farm_guard_time(kind, 100), 0, "kind {kind}");
    }
}

#[test]
fn load_reloaded_data_does_not_retain_old_guard_times() {
    let data = CommonFarmTestData::new();
    data.insert_farm(1, farm_group_kind::FARM, 3_600_000, "zone 100");
    let mut game_data = data.load();
    assert_eq!(
        game_data.farm_guard_time(farm_group_kind::FARM, 100),
        3_600_000
    );

    data.execute("DELETE FROM common_farms");
    data.insert_farm(1, farm_group_kind::RANCH, 7_200_000, "zone 100");
    // `Load` again: the Rust loader builds the tables from scratch (same as the C# clear).
    game_data = data.load();

    assert_eq!(game_data.farm_guard_time(farm_group_kind::FARM, 100), 0);
    assert_eq!(
        game_data.farm_guard_time(farm_group_kind::RANCH, 100),
        7_200_000
    );
}

#[test]
fn load_farm_group_data_preserves_placement_limits_and_allowed_doodads() {
    let data = CommonFarmTestData::new();
    data.execute(
        "INSERT INTO farm_groups (id, count) VALUES (1, 5), (3, 10);
         INSERT INTO farm_group_doodads (id, farm_group_id, doodad_id, item_id)
         VALUES (1, 1, 1001, 2001), (2, 1, 1002, 2002), (3, 3, 1003, 2003);",
    );
    let game_data = data.load();

    let sorted = |mut v: Vec<u32>| {
        v.sort_unstable();
        v
    };
    assert_eq!(game_data.farm_group_max_count(farm_group_kind::FARM), 5);
    assert_eq!(
        sorted(game_data.allowed_doodads(farm_group_kind::FARM)),
        vec![1001, 1002]
    );
    assert_eq!(game_data.farm_group_max_count(farm_group_kind::RANCH), 10);
    assert_eq!(
        sorted(game_data.allowed_doodads(farm_group_kind::RANCH)),
        vec![1003]
    );
}

// ---------------------------------------------------------------------------------------------
// PublicFarmGuardTimeTests
// ---------------------------------------------------------------------------------------------

/// `s_plantTime` = 2026-01-01 00:00:00 UTC (unix seconds).
const PLANT_TIME: i64 = 1_767_225_600;
/// Template of the test doodads (its group's `GuardOnFieldTime` is 86 400 s, as in C#).
const TEMPLATE: u32 = 4242;

/// `LoadFarm(guardTime)`: one farm row for zone 100.
fn load_farm(guard_time: u32) -> PublicFarmData {
    let data = CommonFarmTestData::new();
    data.insert_farm(1, farm_group_kind::FARM, guard_time, "zone 100");
    data.load()
}

/// `CreateDoodad(plantTime, kind)`: owned by character 42, zone 100.
fn create_doodad(plant_time: i64, kind: u8) -> DoodadRef {
    let mut d = Doodad::new(TEMPLATE);
    d.plant_time = plant_time;
    d.farm_type = kind;
    d.owner_id = 42;
    d.owner_type = doodad_owner_type::CHARACTER;
    d.transform.zone_id = 100;
    Entity::new(0, d)
}

/// `PublicFarmManager.IsProtectedByPublicFarm(doodad, now)` on `data` (`now` in unix ms).
fn protected(data: &PublicFarmData, doodad: &DoodadRef, now_ms: i64) -> bool {
    let (farm_type, zone_id, plant_time) = {
        let d = doodad.data();
        (d.farm_type, d.transform.zone_id, d.plant_time)
    };
    is_protected(data, farm_type, zone_id, plant_time, now_ms)
}

/// A server whose game data holds `farm` and the test template (`GuardOnFieldTime` =
/// `field_guard_time` seconds), with the spawn manager of `main_world`.
fn server_with(farm: PublicFarmData, field_guard_time: u32) -> Arc<GameServer> {
    let mut gd = GameData::empty();
    gd.systems.npcs.public_farm = farm;
    gd.systems.npcs.doodad.templates.insert(
        TEMPLATE,
        DoodadTemplate {
            id: TEMPLATE,
            group: DoodadGroups {
                guard_on_field_time: field_guard_time,
                ..DoodadGroups::default()
            },
            ..DoodadTemplate::default()
        },
    );
    let server = Arc::new(test_server_with(gd));
    server.set_self();
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    server
}

fn spawns(server: &GameServer) -> Arc<crate::systems::npcs::spawn_manager::SpawnManager> {
    server.systems.npcs.spawns.get(0).unwrap()
}

#[test]
fn is_protected_by_public_farm_at_expiry_boundary_expires_at_deadline() {
    // (ticks from expiry -> ms from expiry, expected)
    for (ms_from_expiry, expected) in [(-1i64, true), (0, false), (1, false)] {
        let data = load_farm(1500);
        let doodad = create_doodad(PLANT_TIME, farm_group_kind::FARM);
        let now = PLANT_TIME * 1000 + 1500 + ms_from_expiry;

        assert_eq!(protected(&data, &doodad, now), expected, "{ms_from_expiry}");
    }
}

#[test]
fn is_protected_by_public_farm_configured_duration_uses_milliseconds() {
    for (guard_time, age_seconds, expected) in [
        (3_600_000u32, 1800i64, true),
        (3_600_000, 7200, false),
        (172_800_000, 129_600, true),
        (u32::MAX, 2_147_484, true),
    ] {
        let data = load_farm(guard_time);
        let doodad = create_doodad(PLANT_TIME, farm_group_kind::FARM);

        assert_eq!(
            protected(&data, &doodad, (PLANT_TIME + age_seconds) * 1000),
            expected,
            "{guard_time} {age_seconds}"
        );
    }
}

#[test]
fn is_protected_by_public_farm_zero_duration_is_never_protected() {
    for age_seconds in [-3600i64, 3600] {
        let data = load_farm(0);
        let doodad = create_doodad(PLANT_TIME, farm_group_kind::FARM);

        assert!(!protected(
            &data,
            &doodad,
            (PLANT_TIME + age_seconds) * 1000
        ));
    }
}

#[test]
fn is_protected_by_public_farm_missing_mapping_is_not_protected() {
    for kind in [farm_group_kind::INVALID, farm_group_kind::STABLE] {
        let data = load_farm(3_600_000);
        let doodad = create_doodad(PLANT_TIME, kind);

        assert!(!protected(&data, &doodad, PLANT_TIME * 1000), "kind {kind}");
    }
}

#[tokio::test]
async fn is_protected_by_public_farm_expired_farm_ignores_doodad_group_duration() {
    for field_guard_time in [0u32, 86_400] {
        // The doodad's template group (`Template.Group.GuardOnFieldTime`) is set on the server
        // the public entry point reads; it must not extend the farm's guard time.
        let server = server_with(load_farm(3_600_000), field_guard_time);
        let doodad = create_doodad(PLANT_TIME, farm_group_kind::FARM);

        assert!(
            !is_protected_by_public_farm(&server, &doodad, (PLANT_TIME + 2 * 3600) * 1000),
            "{field_guard_time}"
        );
    }
}

#[tokio::test]
async fn public_farm_tick_configured_durations_releases_only_expired_farm_doodads() {
    let data = CommonFarmTestData::new();
    data.insert_farm(1, farm_group_kind::FARM, 3_600_000, "zone 100");
    data.insert_farm(2, farm_group_kind::RANCH, 172_800_000, "zone 100");
    let server = server_with(data.load(), 86_400);
    let now = now_ms();
    let secs_ago = |s: i64| now / 1000 - s;
    let expired = create_doodad(secs_ago(2 * 3600), farm_group_kind::FARM);
    let expired_ranch = create_doodad(secs_ago(3 * 86_400), farm_group_kind::RANCH);
    let protected_farm = create_doodad(secs_ago(30 * 60), farm_group_kind::FARM);
    let protected_ranch = create_doodad(secs_ago(36 * 3600), farm_group_kind::RANCH);
    let outside_farm = create_doodad(secs_ago(3 * 86_400), farm_group_kind::INVALID);
    for d in [
        &expired,
        &protected_farm,
        &expired_ranch,
        &protected_ranch,
        &outside_farm,
    ] {
        spawns(&server).add_player_doodad(d.clone());
    }

    public_farm_tick(&server, now);

    for d in [&expired, &expired_ranch] {
        let d = d.data();
        assert_eq!(d.owner_id, 0);
        assert_eq!(d.owner_type, doodad_owner_type::SYSTEM);
        assert_eq!(d.farm_type, farm_group_kind::INVALID);
    }
    // IsEquivalentTo: the same doodads, in any order.
    let remaining = spawns(&server).get_all_player_doodads();
    assert_eq!(remaining.len(), 3);
    for d in [&protected_farm, &protected_ranch, &outside_farm] {
        assert!(remaining.iter().any(|r| Arc::ptr_eq(r, d)));
    }
    for d in [&protected_farm, &protected_ranch, &outside_farm] {
        let d = d.data();
        assert_eq!(d.owner_id, 42);
        assert_eq!(d.owner_type, doodad_owner_type::CHARACTER);
    }
}

#[tokio::test]
async fn public_farm_tick_zero_or_missing_duration_releases_ownership() {
    for missing_mapping in [false, true] {
        let farm = if missing_mapping {
            CommonFarmTestData::new().load()
        } else {
            load_farm(0)
        };
        let server = server_with(farm, 86_400);
        // Planted one hour in the future.
        let doodad = create_doodad(now_ms() / 1000 + 3600, farm_group_kind::FARM);
        spawns(&server).add_player_doodad(doodad.clone());

        public_farm_tick(&server, now_ms());

        {
            let d = doodad.data();
            assert_eq!(d.owner_id, 0, "{missing_mapping}");
            assert_eq!(d.owner_type, doodad_owner_type::SYSTEM);
            assert_eq!(d.farm_type, farm_group_kind::INVALID);
        }
        assert!(spawns(&server).get_all_player_doodads().is_empty());
    }
}

#[test]
fn is_protected_by_public_farm_different_zones_uses_doodad_zone() {
    for (zone_id, expected) in [(100u32, false), (200, true)] {
        let data = CommonFarmTestData::new();
        data.insert_farm(1, farm_group_kind::FARM, 3_600_000, "zone 100");
        data.insert_farm(2, farm_group_kind::FARM, 172_800_000, "zone 200");
        let game_data = data.load();
        let doodad = create_doodad(PLANT_TIME, farm_group_kind::FARM);
        doodad.data().transform.zone_id = zone_id;

        assert_eq!(
            protected(&game_data, &doodad, (PLANT_TIME + 2 * 3600) * 1000),
            expected,
            "zone {zone_id}"
        );
    }
}

// ---------------------------------------------------------------------------------------------
// PublicFarmManagerTests
// ---------------------------------------------------------------------------------------------

/// `Initialize()` schedules exactly one task: `PublicFarmTickStartTask` after one minute,
/// repeated every minute. The Rust manager runs it as one Tokio task ([`start`] ->
/// `schedule_tick(TICK_START_DELAY, TICK_INTERVAL)`); the schedule is checked on the
/// constants and the task itself with a shortened delay / interval (its effect: an expired
/// farm doodad is released once per interval, not before the start delay).
#[tokio::test]
async fn initialize_schedules_tick() {
    assert_eq!(TICK_START_DELAY, Duration::from_secs(60));
    assert_eq!(TICK_INTERVAL, Duration::from_secs(60));

    let server = server_with(load_farm(3_600_000), 86_400);
    let expired = |n: i64| create_doodad(now_ms() / 1000 - 2 * 3600 - n, farm_group_kind::FARM);
    let first = expired(0);
    spawns(&server).add_player_doodad(first.clone());

    let step = Duration::from_millis(400);
    schedule_tick(&server, step, step);
    tokio::time::sleep(step / 4).await;
    assert_eq!(first.data().owner_id, 42, "no tick before the start delay");

    tokio::time::sleep(step).await;
    assert_eq!(first.data().owner_id, 0, "first tick after the start delay");
    assert!(spawns(&server).get_all_player_doodads().is_empty());

    // Halfway to the next tick: add another expired doodad; the repeat releases it.
    let second = expired(1);
    spawns(&server).add_player_doodad(second.clone());
    tokio::time::sleep(step).await;
    assert_eq!(second.data().owner_id, 0, "repeats every interval");
}
