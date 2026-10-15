//! Event-schedule tests of `NpcSpawner` selection and lifecycle, mirroring the Vile Gourdhead
//! mapping (NPC 14312: AUTOCREATED 15942 + NORMAL 15948 with Halloween schedules 53/54/55):
//! the AUTOCREATED fallback next to a scheduled sibling never spawns before, during or after
//! the event, the active scheduled clone keeps its NPCs, and an expired event NPC is removed
//! through the ordinary tick / cleanup without being recreated (also from a pending respawn).
//!
//! Windows are synthetic day offsets around the current UTC date, so the tests do not sleep or
//! move the game clock. NPC creation goes through the spawn manager's [`TestBackend`] seam like
//! `spawn_tests.rs`; the clone graph comes from the real `SpawnManager::add_npc_spawner` path.

use std::sync::Arc;
use std::time::Duration;

use chrono::{Datelike, Utc};

use crate::data::GameData;
use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::spawner_data::{
    npc_spawner_category, NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate,
};
use crate::systems::npcs::schedule::{
    day_of_week, GameSchedule, GameScheduleSpawner, PeriodStatus, ScheduleData,
};
use crate::systems::npcs::spawn_manager::{self, backend::TestBackend, SpawnManager};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{DoodadRef, Entity, NpcRef, WorldInstance};

use super::NpcSpawner;

struct Backend;

impl TestBackend for Backend {
    fn npc_create(&self, server: &GameServer, obj_id: u32, template_id: u32) -> Option<NpcRef> {
        if template_id == 0 || template_id >= 90_000 {
            return None; // "not exist at db"
        }
        let id = if obj_id == 0 {
            server.ids.object.get_next_id().ok()?
        } else {
            obj_id
        };
        let mut n = Npc::new(template_id);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.level = 10;
        Some(Entity::new(id, n))
    }

    fn doodad_create(
        &self,
        _server: &GameServer,
        _world: &Arc<WorldInstance>,
        _obj_id: u32,
        _template_id: u32,
    ) -> Option<DoodadRef> {
        None
    }
}

/// One authored spawner clone (`npc_spawners` row + member) of the test world.
struct SpawnerSpec {
    id: u32,
    category: u32,
    /// Template `start_time` / `end_time` game-clock window.
    window: Option<(f32, f32)>,
    unit_id: u32,
}

/// One `game_schedule_spawners` binding to a synthetic dated window (day offsets from today).
struct ScheduleSpec {
    schedule_id: i32,
    spawner_id: u32,
    start_days: i64,
    end_days: i64,
}

fn member(id: u32, spawner: u32, npc: u32) -> NpcSpawnerNpc {
    NpcSpawnerNpc {
        id,
        npc_spawner_template_id: spawner,
        member_id: npc,
        member_type: "Npc".into(),
        weight: 1.0,
        unit_id: 0,
    }
}

/// A `GameSchedule` covering the current date ± `start_days` / `end_days` (any day of week),
/// so the status is `InProgress` / `NotStarted` / `Ended` robustly without a clock override.
fn dated_schedule(id: i32, start_days: i64, end_days: i64) -> GameSchedule {
    let date = |days: i64| Utc::now().date_naive() + chrono::Duration::days(days);
    let start = date(start_days);
    let end = date(end_days);
    GameSchedule {
        id,
        name: format!("test-{id}"),
        day_of_week_id: day_of_week::INVALID,
        start_time: 0,
        end_time: 0,
        st_year: start.year(),
        st_month: start.month() as i32,
        st_day: start.day() as i32,
        st_hour: 0,
        st_min: 0,
        ed_year: end.year(),
        ed_month: end.month() as i32,
        ed_day: end.day() as i32,
        ed_hour: 23,
        ed_min: 59,
        start_time_min: 0,
        end_time_min: 0,
    }
}

fn data_with(spawners: &[SpawnerSpec], schedules: &[ScheduleSpec]) -> GameData {
    let mut templates = Vec::new();
    let mut members = Vec::new();
    for (i, s) in spawners.iter().enumerate() {
        let (start_time, end_time) = s.window.unwrap_or((0.0, 0.0));
        templates.push(NpcSpawnerTemplate {
            id: s.id,
            npc_spawner_category_id: s.category,
            start_time,
            end_time,
            spawn_delay_min: 10.0,
            spawn_delay_max: 10.0,
            ..NpcSpawnerTemplate::default()
        });
        members.push(member(i as u32 + 1, s.id, s.unit_id));
    }
    let mut schedule_rows = Vec::new();
    let mut schedule_spawners = Vec::new();
    for s in schedules {
        schedule_rows.push(dated_schedule(s.schedule_id, s.start_days, s.end_days));
        schedule_spawners.push(GameScheduleSpawner {
            id: s.schedule_id,
            game_schedule_id: s.schedule_id,
            spawner_id: s.spawner_id as i32,
        });
    }
    let mut data = GameData::empty();
    data.systems.npcs.npc_spawners =
        NpcSpawnerData::from_parts(templates, members).expect("spawner data");
    data.systems.npcs.schedule =
        ScheduleData::from_rows(schedule_rows, schedule_spawners, Vec::new(), Vec::new());
    data
}

fn test_server(data: GameData) -> Arc<GameServer> {
    let s = Arc::new(test_server_with(data));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

fn manager(server: &Arc<GameServer>) -> Arc<SpawnManager> {
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world)
}

fn npc_def(unit_id: u32, x: f32, y: f32) -> NpcSpawnerDef {
    let mut base = SpawnerBase {
        unit_id,
        ..SpawnerBase::default()
    };
    base.position.x = x;
    base.position.y = y;
    base.position.z = 100.0;
    base.position.zone_id = TEST_ZONE;
    NpcSpawnerDef {
        base,
        count: 1,
        npc_spawner_ids: Vec::new(),
    }
}

fn clones(m: &SpawnManager, unit_id: u32) -> Vec<Arc<NpcSpawner>> {
    let mut v: Vec<_> = m
        .get_all_spawners()
        .into_values()
        .flatten()
        .filter(|s| s.unit_id == unit_id)
        .collect();
    v.sort_by_key(|s| s.spawner_id);
    v
}

/// The AUTOCREATED fallback and the scheduled NORMAL clone of one placement.
fn pair(
    m: &SpawnManager,
    unit_id: u32,
    auto_id: u32,
    scheduled_id: u32,
) -> (Arc<NpcSpawner>, Arc<NpcSpawner>) {
    let all = clones(m, unit_id);
    assert_eq!(all.len(), 2, "the member rows produced both clones");
    let auto = all
        .iter()
        .find(|s| s.spawner_id == auto_id)
        .expect("fallback clone")
        .clone();
    let scheduled = all
        .iter()
        .find(|s| s.spawner_id == scheduled_id)
        .expect("scheduled clone")
        .clone();
    assert!(
        Arc::ptr_eq(auto.shared(), scheduled.shared()),
        "the clones share one authored placement"
    );
    (auto, scheduled)
}

fn player(server: &Arc<GameServer>, id: u32, x: f32, y: f32) {
    let client = TestClient::new(server, id, id);
    let _p = spawn_player(server, &client, test_character(id, "P", x, y), 5000 + id);
}

/// `(name, day offsets, NPCs the scheduled clone may create)`.
const WINDOWS: [(&str, (i64, i64), i32); 3] = [
    ("not started", (5, 10), 0),
    ("in progress", (-2, 2), 1),
    ("ended", (-10, -5), 0),
];

/// The Vile Gourdhead mapping: 14312's member rows produce the AUTOCREATED 15942 fallback and
/// the NORMAL 15948 clone; only 15948 has Halloween schedule rows.
fn gourdhead_data(start_days: i64, end_days: i64) -> GameData {
    data_with(
        &[
            SpawnerSpec {
                id: 15942,
                category: npc_spawner_category::AUTOCREATED,
                window: None,
                unit_id: 14312,
            },
            SpawnerSpec {
                id: 15948,
                category: npc_spawner_category::NORMAL,
                window: None,
                unit_id: 14312,
            },
        ],
        &[ScheduleSpec {
            schedule_id: 53,
            spawner_id: 15948,
            start_days,
            end_days,
        }],
    )
}

#[tokio::test]
async fn scheduled_sibling_suppresses_automatic_fallback_in_every_status() {
    for (name, (start, end), expected) in WINDOWS {
        let server = test_server(gourdhead_data(start, end));
        let m = manager(&server);
        assert_eq!(
            m.add_npc_spawner(&server, npc_def(14312, 100.0, 100.0)),
            Ok(2),
            "{name}"
        );
        let (auto, scheduled) = pair(&m, 14312, 15942, 15948);

        auto.update(&server);
        assert_eq!(
            auto.current_spawn_count(),
            0,
            "{name}: the AUTOCREATED fallback must never spawn year-round"
        );
        assert!(auto.spawned_npcs().is_empty(), "{name}");

        scheduled.update(&server);
        assert_eq!(
            scheduled.current_spawn_count(),
            expected,
            "{name}: only the scheduled clone may spawn, and only while in progress"
        );
        assert_eq!(
            server.world.world(0).unwrap().all_npcs().len(),
            expected as usize,
            "{name}"
        );
    }
}

#[tokio::test]
async fn only_scheduled_clone_spawns_when_active_and_survives_updates() {
    let server = test_server(gourdhead_data(-2, 2));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(14312, 100.0, 100.0)),
        Ok(2)
    );
    let (auto, scheduled) = pair(&m, 14312, 15942, 15948);
    player(&server, 1, 100.0, 100.0);

    auto.update(&server);
    assert_eq!(
        auto.current_spawn_count(),
        0,
        "no fallback during the event"
    );
    scheduled.update(&server);
    assert_eq!(scheduled.current_spawn_count(), 1);

    let world = server.world.world(0).unwrap();
    let npc = world.all_npcs().pop().expect("the event NPC");
    assert_eq!(npc.data().unit.template_id, 14312);
    assert!(!npc.data().unit.is_dead());
    let link = server
        .systems
        .npcs
        .spawns
        .npc_spawner_of(npc.obj_id)
        .expect("spawner link");
    assert!(
        Arc::ptr_eq(&link, &scheduled),
        "the scheduled clone owns the NPC, the fallback does not"
    );

    // The next ordinary tick keeps the in-progress event NPC.
    scheduled.update(&server);
    assert_eq!(
        scheduled.current_spawn_count(),
        1,
        "InProgress must not despawn"
    );
    assert_eq!(world.all_npcs().len(), 1);
    assert!(
        !npc.data().unit.is_dead(),
        "the live event NPC is not torn down on the next tick"
    );
}

#[tokio::test]
async fn inactive_event_schedules_spawn_nothing() {
    for (name, (start, end)) in [("not started", (5, 10)), ("ended", (-10, -5))] {
        let server = test_server(gourdhead_data(start, end));
        let m = manager(&server);
        assert_eq!(
            m.add_npc_spawner(&server, npc_def(14312, 100.0, 100.0)),
            Ok(2),
            "{name}"
        );
        let (auto, scheduled) = pair(&m, 14312, 15942, 15948);
        player(&server, 1, 100.0, 100.0);

        auto.update(&server);
        scheduled.update(&server);
        assert_eq!(scheduled.current_spawn_count(), 0, "{name}");
        assert_eq!(
            server.world.world(0).unwrap().all_npcs().len(),
            0,
            "{name}: an inactive event produces no NPC"
        );
        assert!(!scheduled.is_spawn_scheduled(), "{name}");
        assert!(!auto.is_spawn_scheduled(), "{name}");
    }
}

#[tokio::test]
async fn expired_event_npc_is_removed_by_the_ordinary_tick_and_cleanup() {
    let server = test_server(gourdhead_data(-10, -5));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(14312, 100.0, 100.0)),
        Ok(2)
    );
    let (auto, scheduled) = pair(&m, 14312, 15942, 15948);
    player(&server, 1, 100.0, 100.0);
    let world = server.world.world(0).unwrap();

    // Seed what the fallback used to spawn year-round, then run the ordinary tick.
    scheduled.do_spawn(&server);
    assert_eq!(scheduled.current_spawn_count(), 1);
    assert_eq!(world.all_npcs().len(), 1);
    assert!(
        scheduled.can_despawn_npcs(&server),
        "an ended event schedules removal"
    );
    assert!(
        !auto.can_despawn_npcs(&server),
        "the fallback owns nothing to remove"
    );

    scheduled.update(&server);
    assert_eq!(
        m.pending_despawns().len(),
        1,
        "the corpse cleanup is queued"
    );
    assert_eq!(
        m.pending_respawns().len(),
        1,
        "the respawn gate is queued, not bypassed"
    );

    // The one-second cleanup tick removes the expired event NPC.
    spawn_manager::advance_test_clock(Duration::from_secs(25));
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    assert!(m.pending_despawns().is_empty());
    assert!(world.all_npcs().is_empty(), "the expired event NPC is gone");

    // Later ticks cannot resurrect it through either clone.
    scheduled.update(&server);
    auto.update(&server);
    assert_eq!(world.all_npcs().len(), 0);
    assert!(!scheduled.is_spawn_scheduled());
}

#[tokio::test]
async fn pending_event_respawn_does_not_recreate_outside_the_schedule() {
    let server = test_server(gourdhead_data(-10, -5));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(14312, 100.0, 100.0)),
        Ok(2)
    );
    let (auto, scheduled) = pair(&m, 14312, 15942, 15948);
    player(&server, 1, 100.0, 100.0);
    let world = server.world.world(0).unwrap();

    scheduled.do_spawn(&server);
    let npc = world.all_npcs().pop().expect("seeded NPC");
    {
        let mut d = npc.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &npc);
    assert_eq!(m.pending_respawns().len(), 1);
    assert_eq!(scheduled.scheduled_count(), 1);

    // Before the deadline: the tick neither spawns a replacement nor drops the reservation.
    scheduled.update(&server);
    assert_eq!(
        scheduled.current_spawn_count(),
        0,
        "no live replacement outside the schedule"
    );
    assert_eq!(world.all_npcs().len(), 1, "the corpse remains for cleanup");
    assert_eq!(
        m.pending_respawns().len(),
        1,
        "the respawn slot stays reserved"
    );

    // The respawn timer fires; the schedule gate must keep the slot empty.
    spawn_manager::advance_test_clock(Duration::from_secs(11));
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    assert!(!scheduled.is_spawn_scheduled());
    scheduled.update(&server);
    assert_eq!(
        scheduled.current_spawn_count(),
        0,
        "the freed respawn slot is not filled outside the schedule"
    );

    // The corpse cleanup finishes and no later tick recreates the NPC.
    spawn_manager::advance_test_clock(Duration::from_secs(25));
    m.check_respawns_tick(&server);
    assert!(world.all_npcs().is_empty());
    scheduled.update(&server);
    auto.update(&server);
    assert_eq!(world.all_npcs().len(), 0);
    assert!(!scheduled.is_spawn_scheduled());
}

#[tokio::test]
async fn ordinary_and_single_spawner_lifecycles_are_unchanged() {
    // Single ordinary spawner with no schedule rows and no time window.
    let server = test_server(data_with(
        &[SpawnerSpec {
            id: 6001,
            category: npc_spawner_category::NORMAL,
            window: None,
            unit_id: 5000,
        }],
        &[],
    ));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(5000, 100.0, 100.0)),
        Ok(1)
    );
    let single = clones(&m, 5000).pop().expect("single clone");
    player(&server, 1, 100.0, 100.0);

    single.update(&server);
    assert_eq!(single.current_spawn_count(), 1, "ordinary NPC still spawns");
    single.update(&server);
    assert_eq!(
        single.current_spawn_count(),
        1,
        "and is retained on the next tick"
    );

    // It still dies and respawns on its authored delay.
    let world = server.world.world(0).unwrap();
    let npc = world.all_npcs().pop().expect("ordinary NPC");
    {
        let mut d = npc.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &npc);
    assert_eq!(m.pending_respawns().len(), 1);
    spawn_manager::advance_test_clock(Duration::from_secs(11));
    m.check_respawns_tick(&server);
    single.update(&server);
    assert_eq!(
        single.current_spawn_count(),
        1,
        "ordinary respawn still works"
    );

    // Ordinary multi-spawner group without schedules: the AUTOCREATED fallback still spawns.
    let server = test_server(data_with(
        &[
            SpawnerSpec {
                id: 7001,
                category: npc_spawner_category::AUTOCREATED,
                window: None,
                unit_id: 5100,
            },
            SpawnerSpec {
                id: 7002,
                category: npc_spawner_category::NORMAL,
                window: None,
                unit_id: 5100,
            },
        ],
        &[],
    ));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(5100, 100.0, 100.0)),
        Ok(2)
    );
    let (auto, scheduled) = pair(&m, 5100, 7001, 7002);
    player(&server, 1, 100.0, 100.0);

    auto.update(&server);
    assert_eq!(
        auto.current_spawn_count(),
        1,
        "with no schedule anywhere the fallback still spawns"
    );
    scheduled.update(&server);
    assert_eq!(
        auto.current_spawn_count(),
        1,
        "the NORMAL sibling does not add a second NPC"
    );
    assert_eq!(server.world.world(0).unwrap().all_npcs().len(), 1);
}

#[tokio::test]
async fn authored_game_clock_windows_still_gate_spawning_and_removal() {
    // The test server's game clock sits at 12:00 (`TimeManager::default`).
    let server = test_server(data_with(
        &[SpawnerSpec {
            id: 6100,
            category: npc_spawner_category::NORMAL,
            window: Some((9.0, 18.0)),
            unit_id: 5200,
        }],
        &[],
    ));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(5200, 100.0, 100.0)),
        Ok(1)
    );
    let inside = clones(&m, 5200).pop().expect("windowed clone");
    player(&server, 1, 100.0, 100.0);
    inside.update(&server);
    assert_eq!(
        inside.current_spawn_count(),
        1,
        "an authored window still allows spawning inside it"
    );

    // Outside the window: the window blocks spawning and schedules removal.
    let server = test_server(data_with(
        &[SpawnerSpec {
            id: 6101,
            category: npc_spawner_category::NORMAL,
            window: Some((1.0, 2.0)),
            unit_id: 5201,
        }],
        &[],
    ));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(5201, 100.0, 100.0)),
        Ok(1)
    );
    let outside = clones(&m, 5201).pop().expect("windowed clone");
    player(&server, 1, 100.0, 100.0);
    outside.update(&server);
    assert_eq!(
        outside.current_spawn_count(),
        0,
        "an authored window still blocks spawning outside it"
    );

    // Seed a leaked NPC (spawned while the window was open) and run the ordinary tick.
    outside.do_spawn(&server);
    assert_eq!(outside.current_spawn_count(), 1);
    assert!(
        outside.can_despawn_npcs(&server),
        "outside the window removal is scheduled"
    );
    outside.update(&server);
    assert_eq!(m.pending_despawns().len(), 1);

    let world = server.world.world(0).unwrap();
    spawn_manager::advance_test_clock(Duration::from_secs(25));
    m.check_respawns_tick(&server);
    assert!(world.all_npcs().is_empty());
    outside.update(&server);
    assert_eq!(
        world.all_npcs().len(),
        0,
        "the closed window keeps the slot empty"
    );
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_gourdhead_mapping_and_expired_schedule() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let spawners = NpcSpawnerData::load_from(&conn).unwrap();
    let ids = spawners
        .get_spawner_ids(14312)
        .expect("Vile Gourdhead member rows");
    assert!(ids.contains(&15942), "AUTOCREATED fallback row: {ids:?}");
    assert!(ids.contains(&15948), "scheduled NORMAL row: {ids:?}");
    let auto = spawners.get_npc_spawner_template(15942).unwrap();
    let scheduled = spawners.get_npc_spawner_template(15948).unwrap();
    assert_eq!(
        auto.npc_spawner_category_id,
        npc_spawner_category::AUTOCREATED
    );
    assert_eq!(
        scheduled.npc_spawner_category_id,
        npc_spawner_category::NORMAL
    );
    assert!(
        auto.start_time <= 0.0 && auto.end_time <= 0.0,
        "the fallback has no game-clock window"
    );

    let files = crate::data::client_files::ClientFileManager::default();
    let ctx = crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: std::path::Path::new("."),
        files: &files,
        language: "en_us",
    };
    let schedules = ScheduleData::load(&ctx).unwrap();
    let now = spawn_manager::unix_now();
    assert_eq!(
        schedules.period_status_npc(15942, now),
        PeriodStatus::NotFound,
        "the fallback is not in any schedule"
    );
    assert_ne!(
        schedules.period_status_npc(15948, now),
        PeriodStatus::NotFound
    );
    assert_eq!(
        schedules.period_status_npc(15948, now),
        PeriodStatus::Ended,
        "the shipped 2013 Halloween window stays expired"
    );
    let rows = schedules
        .spawner_schedule_ids(15948)
        .expect("scheduled sibling has schedule rows");
    assert!(!rows.is_empty());
    for id in rows {
        let gs = &schedules.game_schedules[id];
        assert_eq!(gs.ed_year, 2013, "authored dates are not retargeted");
    }
}
