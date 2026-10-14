//! Population tests of `NpcSpawner::do_spawn` / `CheckSpawnCountCanSpawn`: the authored
//! `Count` / `min_population` baseline, the `max_population` / `suspend_spawn_count` caps, the
//! shared alternate spawner populations and the respawn / corpse bookkeeping.
//!
//! NPC creation goes through the spawn manager's [`TestBackend`] seam like `spawn_tests.rs`.

use std::sync::Arc;
use std::time::Duration;

use crate::data::world_templates::WorldSpawnPosition;
use crate::data::GameData;
use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::spawner_data::{NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate};
use crate::systems::npcs::spawn_manager::{self, backend::TestBackend};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{DoodadRef, Entity, NpcRef, WorldInstance};

use super::{NpcSpawner, NpcSpawnerInit, NpcSpawnerShared};

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

fn server() -> Arc<GameServer> {
    let s = Arc::new(test_server_with(GameData::empty()));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

/// [`server`] with one authored spawner template (id 10, member NPC 500), so a spawner created
/// through `SpawnManager::add_npc_spawner` is visible to `active_region_tick`.
fn server_with_authored_spawner() -> Arc<GameServer> {
    let mut data = GameData::empty();
    data.systems.npcs.npc_spawners =
        NpcSpawnerData::from_parts(vec![template(10, Vec::new())], vec![member(100, 10, 500)])
            .unwrap();
    let s = Arc::new(test_server_with(data));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

fn position(x: f32, y: f32) -> WorldSpawnPosition {
    WorldSpawnPosition {
        world_id: 0,
        zone_id: TEST_ZONE,
        x,
        y,
        z: 100.0,
        ..WorldSpawnPosition::default()
    }
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

/// A template with the default `max_population = 1` and no time window.
fn template(id: u32, npcs: Vec<NpcSpawnerNpc>) -> NpcSpawnerTemplate {
    NpcSpawnerTemplate {
        id,
        spawn_delay_min: 10.0,
        spawn_delay_max: 10.0,
        npcs,
        ..NpcSpawnerTemplate::default()
    }
}

fn make_spawner_shared(
    shared: Arc<NpcSpawnerShared>,
    template: NpcSpawnerTemplate,
    unit_id: u32,
    count: u32,
) -> Arc<NpcSpawner> {
    let spawner_id = template.id;
    let spawnable_npcs = template.npcs.clone();
    Arc::new(NpcSpawner::new(
        NpcSpawnerInit {
            id: spawner_id,
            spawner_id,
            unit_id,
            follow_path: String::new(),
            follow_npc: 0,
            respawn_time: 10,
            despawn_time: 20,
            special_link: String::new(),
            count,
            npc_spawner_ids: vec![spawner_id],
            parent_world: Some(0),
            template: Some(Arc::new(template)),
            spawnable_npcs,
        },
        shared,
    ))
}

fn make_spawner(template: NpcSpawnerTemplate, unit_id: u32, count: u32) -> Arc<NpcSpawner> {
    make_spawner_shared(
        NpcSpawnerShared::new(position(100.0, 100.0)),
        template,
        unit_id,
        count,
    )
}

#[tokio::test]
async fn one_nearby_player_does_not_reduce_authored_min_population() {
    let server = server();
    let mut t = template(10, vec![member(100, 10, 500)]);
    t.min_population = 3;
    t.max_population = 10;
    let spawner = make_spawner(t, 500, 1);
    let client = TestClient::new(&server, 1, 1);
    let _player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        3,
        "the authored minimum is the baseline, not the player count"
    );
    assert_eq!(server.world.world(0).unwrap().all_npcs().len(), 3);

    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        3,
        "no overshoot on the next tick"
    );
}

#[tokio::test]
async fn multi_member_list_stops_at_max_population() {
    let server = server();
    let mut t = template(
        20,
        vec![
            member(200, 20, 500),
            member(201, 20, 501),
            member(202, 20, 502),
        ],
    );
    t.max_population = 2;
    let spawner = make_spawner(t, 500, 3);

    spawner.do_spawn(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        2,
        "authored Count 3 clamped by MaxPopulation 2"
    );
    let mut ids: Vec<u32> = spawner
        .spawned_npcs()
        .values()
        .flatten()
        .map(|n| n.data().unit.template_id)
        .collect();
    ids.sort_unstable();
    ids.dedup();
    assert_eq!(
        ids.len(),
        2,
        "two distinct members filled the capped target"
    );
    assert!(
        ids.iter().all(|id| (500..=502).contains(id)),
        "only authored members spawned: {ids:?}"
    );
    assert!(!spawner.check_spawn_count_can_spawn(&server));

    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 2, "hard cap holds");
}

#[tokio::test]
async fn capped_selection_keeps_later_positive_weight_members_eligible() {
    let server = server();
    let zero = NpcSpawnerNpc {
        weight: 0.0,
        ..member(200, 25, 500)
    };
    let positive = member(201, 25, 501);
    let mut t = template(25, vec![zero, positive]);
    t.max_population = 1;
    let spawner = make_spawner(t, 500, 1);

    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
    let ids: Vec<u32> = spawner
        .spawned_npcs()
        .values()
        .flatten()
        .map(|n| n.data().unit.template_id)
        .collect();
    assert_eq!(
        ids,
        vec![501],
        "the later positive-weight member is selected, not the prefix"
    );
}

#[tokio::test]
async fn staggered_deaths_keep_each_authored_respawn_deadline() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let mut t = template(90, vec![member(900, 90, 500)]);
    t.min_population = 2;
    t.max_population = 2;
    let spawner = make_spawner(t, 500, 1);
    let client = TestClient::new(&server, 1, 1);
    let _player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    spawner.update(&server);
    let pair: Vec<NpcRef> = spawner.spawned_npcs().values().flatten().cloned().collect();
    assert_eq!(pair.len(), 2);

    let kill = |npc: &NpcRef| {
        {
            let mut d = npc.data();
            d.unit.hp = 0;
            d.dead_time_ms = crate::world::now_ms();
        }
        spawn_manager::on_npc_died(&server, npc);
    };
    kill(&pair[0]);
    spawn_manager::advance_test_clock(Duration::from_secs(5));
    kill(&pair[1]);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert_eq!(spawner.scheduled_count(), 2);
    assert_eq!(manager.pending_respawns().len(), 2);

    // The first deadline is due; the staggered second slot stays reserved.
    spawn_manager::advance_test_clock(Duration::from_secs(6));
    manager.check_respawns_tick(&server);
    assert_eq!(
        manager.pending_respawns().len(),
        1,
        "the second authored deadline is still pending"
    );
    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "only the due slot refills, not the reserved future one"
    );
    assert_eq!(spawner.scheduled_count(), 1);

    // The second deadline refills its own slot.
    spawn_manager::advance_test_clock(Duration::from_secs(5));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_respawns().is_empty());
    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 2);
    assert_eq!(spawner.scheduled_count(), 0);
}

#[tokio::test]
async fn corpses_are_excluded_and_pending_respawns_are_honored() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let mut t = template(30, vec![member(300, 30, 500)]);
    t.min_population = 2;
    t.max_population = 2;
    let spawner = make_spawner(t, 500, 1);
    let client = TestClient::new(&server, 1, 1);
    let _player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 2);

    let victim = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    {
        let mut d = victim.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &victim);
    assert_eq!(spawner.current_spawn_count(), 1, "corpses do not count");
    assert_eq!(spawner.scheduled_count(), 1, "pending respawn counted");
    assert_eq!(manager.pending_respawns().len(), 1);

    // While the respawn is pending the population is not replenished again.
    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
    assert_eq!(world.all_npcs().len(), 2, "corpse + one living");

    spawn_manager::advance_test_clock(Duration::from_secs(11));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_respawns().is_empty());

    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        2,
        "respawn fills the baseline back up"
    );
    assert_eq!(spawner.scheduled_count(), 0);
    assert_eq!(
        world.all_npcs().len(),
        3,
        "the lootable corpse is still there"
    );
}

#[tokio::test]
async fn shared_alternate_spawners_count_each_others_living_npcs() {
    let server = server();
    let mut a_template = template(40, vec![member(400, 40, 500)]);
    a_template.min_population = 2;
    a_template.max_population = 2;
    let mut b_template = template(41, vec![member(410, 41, 501)]);
    b_template.max_population = 1;
    let shared = NpcSpawnerShared::new(position(100.0, 100.0));
    let a = make_spawner_shared(shared.clone(), a_template, 500, 1);
    let b = make_spawner_shared(shared.clone(), b_template, 501, 1);

    b.do_spawn(&server);
    assert_eq!(b.current_spawn_count(), 1);
    assert_eq!(
        a.are_other_npcs_in_spawn_zone(),
        (true, 1),
        "the alternate spawner's actual living NPCs, not this spawner's count"
    );

    a.do_spawn(&server);
    assert_eq!(
        a.current_spawn_count(),
        1,
        "shared map still respects the cap"
    );
    b.do_spawn(&server);
    assert_eq!(
        b.current_spawn_count(),
        1,
        "no overshoot through the shared map"
    );
    let total = a
        .spawned_npcs()
        .values()
        .flatten()
        .filter(|n| !n.data().unit.is_dead())
        .count();
    assert_eq!(
        total, 2,
        "one shared population across the alternate spawner ids"
    );
}

#[tokio::test]
async fn authored_count_is_the_baseline_and_default_singleton_is_unchanged() {
    let server = server();
    let mut counted = template(50, vec![member(500, 50, 500)]);
    counted.max_population = 5;
    let spawner = make_spawner(counted, 500, 2);
    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 2, "authored Count filled");
    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 2);

    let singleton = make_spawner(template(60, vec![member(600, 60, 501)]), 501, 1);
    singleton.do_spawn(&server);
    assert_eq!(singleton.current_spawn_count(), 1, "default singleton");
    singleton.do_spawn(&server);
    assert_eq!(
        singleton.current_spawn_count(),
        1,
        "default singleton stays one"
    );
}

#[tokio::test]
async fn positive_suspend_spawn_count_is_a_hard_cap() {
    let server = server();
    let mut t = template(70, vec![member(700, 70, 500)]);
    t.max_population = 10;
    t.suspend_spawn_count = 2;
    let spawner = make_spawner(t, 500, 3);

    spawner.do_spawn(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        2,
        "authored Count 3 clamped by SuspendSpawnCount 2"
    );
    assert!(!spawner.check_spawn_count_can_spawn(&server));
    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 2);
}

#[tokio::test]
async fn failed_members_leave_the_spawner_retryable() {
    let server = server();
    let broken = template(80, vec![member(800, 80, 90_001)]);
    let spawner = make_spawner(broken, 90_001, 1);
    spawner.set_spawn_scheduled(true);
    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(
        !spawner.is_spawn_scheduled(),
        "a pass with no spawn must not stay permanently scheduled"
    );

    let fixed = template(80, vec![member(800, 80, 500)]);
    spawner.set_template(Some(Arc::new(fixed.clone())));
    spawner.initialize_spawnable_npcs(Some(&fixed));
    spawner.do_spawn(&server);
    assert_eq!(spawner.current_spawn_count(), 1, "retry succeeds");
}

#[tokio::test]
async fn nearby_player_population_growth_refills_within_the_cap() {
    let server = server();
    let mut t = template(15, vec![member(150, 15, 500)]);
    t.max_population = 3;
    let spawner = make_spawner(t, 500, 1);
    let client1 = TestClient::new(&server, 1, 1);
    let _p1 = spawn_player(
        &server,
        &client1,
        test_character(1, "P", 100.0, 100.0),
        5000,
    );

    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 1, "authored baseline first");

    let _p2 = spawn_player(
        &server,
        &TestClient::new(&server, 2, 2),
        test_character(2, "Q", 110.0, 100.0),
        5001,
    );
    // Wait out the 10 s player-count cache so the new player is seen.
    spawn_manager::advance_test_clock(Duration::from_secs(10));
    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        2,
        "normal Update grows toward the nearby players without a flag reset"
    );

    let _p3 = spawn_player(
        &server,
        &TestClient::new(&server, 3, 3),
        test_character(3, "R", 120.0, 100.0),
        5002,
    );
    spawn_manager::advance_test_clock(Duration::from_secs(10));
    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        3,
        "growth continues to the cap"
    );

    let _p4 = spawn_player(
        &server,
        &TestClient::new(&server, 4, 4),
        test_character(4, "S", 130.0, 100.0),
        5003,
    );
    spawn_manager::advance_test_clock(Duration::from_secs(10));
    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        3,
        "max_population caps the adaptive growth"
    );
}

#[tokio::test]
async fn capacity_blocked_shared_clone_recovers_when_the_population_disappears() {
    let server = server();
    let mut a_template = template(45, vec![member(450, 45, 500)]);
    a_template.max_population = 1;
    let mut b_template = template(46, vec![member(460, 46, 501)]);
    b_template.max_population = 1;
    let shared = NpcSpawnerShared::new(position(100.0, 100.0));
    let a = make_spawner_shared(shared.clone(), a_template, 500, 1);
    let b = make_spawner_shared(shared.clone(), b_template, 501, 1);

    b.do_spawn(&server);
    assert_eq!(b.current_spawn_count(), 1);

    // The shared cap is full: the other clone's Update is blocked and must not mark it
    // scheduled (that used to leave it frozen even after the blocker disappeared).
    a.update(&server);
    assert_eq!(a.current_spawn_count(), 0);
    assert!(
        !a.is_spawn_scheduled(),
        "a capacity block is not a scheduled spawn"
    );

    // The other clone's only NPC disappears (despawn, so no respawn timer keeps the slot).
    let b_npc = b.spawned_npcs().get(&b.spawner_id).unwrap()[0].clone();
    b.despawn(&server, &b_npc);
    assert_eq!(b.current_spawn_count(), 0);

    a.update(&server);
    assert_eq!(
        a.current_spawn_count(),
        1,
        "the freed shared slot refills without a manual flag reset"
    );
}

#[tokio::test]
async fn force_spawn_cancels_only_its_placement_and_the_new_npc_respawns() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let mut t = template(95, vec![member(950, 95, 500)]);
    t.max_population = 1;
    let spawner = make_spawner(t, 500, 1);
    // Same spawner template id, different position/placement: only the shared `Arc` may scope
    // the cancellation (a template id is reused at different positions in real data).
    let mut other_t = template(95, vec![member(960, 95, 501)]);
    other_t.max_population = 1;
    let other = make_spawner_shared(
        NpcSpawnerShared::new(position(200.0, 200.0)),
        other_t,
        501,
        1,
    );
    assert_eq!(spawner.spawner_id, other.spawner_id);
    assert!(!Arc::ptr_eq(spawner.shared(), other.shared()));
    let client = TestClient::new(&server, 1, 1);
    let _player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    // One pending respawn per placement.
    spawner.update(&server);
    other.update(&server);
    let victim = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    let other_victim = other.spawned_npcs().values().next().unwrap()[0].clone();
    for npc in [&victim, &other_victim] {
        {
            let mut d = npc.data();
            d.unit.hp = 0;
            d.dead_time_ms = crate::world::now_ms();
        }
        spawn_manager::on_npc_died(&server, npc);
    }
    assert_eq!(manager.pending_respawns().len(), 2);
    assert_eq!(spawner.scheduled_count(), 1);

    // The forced fill drops its own obsolete deadline; the other placement keeps its timer.
    let forced = spawner.force_spawn(&server, 0).expect("forced NPC");
    let pending = manager.pending_respawns();
    assert_eq!(
        pending.len(),
        1,
        "only the same placement's respawn is cancelled"
    );
    assert_eq!(pending[0].0, other_victim.obj_id);
    assert!(!Arc::ptr_eq(&forced, &victim));
    assert_eq!(spawner.current_spawn_count(), 1);
    assert_eq!(
        spawner.scheduled_count(),
        0,
        "the cancelled reservation leaves no stale credit"
    );

    // The forced NPC dies and gets its own normal 10 s respawn.
    {
        let mut d = forced.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &forced);
    assert_eq!(
        manager.pending_respawns().len(),
        2,
        "the forced NPC's own respawn is queued"
    );
    spawn_manager::advance_test_clock(Duration::from_secs(11));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_respawns().is_empty());
    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "the forced NPC respawns after the normal delay"
    );
}

#[tokio::test]
async fn discretionary_growth_preserves_an_own_future_respawn_credit() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let mut t = template(97, vec![member(970, 97, 500)]);
    t.min_population = 2;
    t.max_population = 4;
    let spawner = make_spawner(t, 500, 1);

    spawner.update(&server);
    let pair: Vec<NpcRef> = spawner.spawned_npcs().values().flatten().cloned().collect();
    assert_eq!(pair.len(), 2, "authored baseline");
    // One death with a 50 s delay: a future reservation plus one own credit.
    spawner.set_respawn_time(50);
    {
        let mut d = pair[0].data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &pair[0]);
    assert_eq!(manager.pending_respawns().len(), 1);
    assert_eq!(spawner.scheduled_count(), 1);
    let deadline = manager.pending_respawns()[0].1;

    // Four nearby players arrive; after the 10 s player cache the group grows.
    for i in 0..4u32 {
        let _p = spawn_player(
            &server,
            &TestClient::new(&server, 10 + i, 10 + i),
            test_character(10 + i, "P", 100.0 + i as f32, 100.0),
            5100 + i,
        );
    }
    spawn_manager::advance_test_clock(Duration::from_secs(11));
    spawner.update(&server);

    assert_eq!(
        spawner.current_spawn_count(),
        3,
        "two discretionary births fill the two free slots"
    );
    assert_eq!(
        spawner.scheduled_count(),
        1,
        "the own future timer's credit is preserved, never negative"
    );
    let pending = manager.pending_respawns();
    assert_eq!(pending.len(), 1, "the future reservation is still pending");
    assert_eq!(pending[0].1, deadline, "its deadline is unchanged");
}

#[tokio::test]
async fn force_spawn_resets_each_cancelled_clone_credit_by_shared_placement() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    // a and b are clones of one placement; c reuses a's template id at another position.
    let mut a_template = template(45, vec![member(450, 45, 500)]);
    a_template.max_population = 1;
    let mut b_template = template(46, vec![member(460, 46, 501)]);
    b_template.max_population = 1;
    let c_template = template(45, vec![member(450, 45, 502)]);
    let shared = NpcSpawnerShared::new(position(100.0, 100.0));
    let a = make_spawner_shared(shared.clone(), a_template, 500, 1);
    let b = make_spawner_shared(shared.clone(), b_template, 501, 1);
    let c = make_spawner_shared(
        NpcSpawnerShared::new(position(300.0, 300.0)),
        c_template,
        502,
        1,
    );
    assert_eq!(
        a.spawner_id, c.spawner_id,
        "same template id at two positions"
    );

    a.do_spawn(&server);
    c.do_spawn(&server);
    let a_victim = a.spawned_npcs().get(&a.spawner_id).unwrap()[0].clone();
    let c_victim = c.spawned_npcs().get(&c.spawner_id).unwrap()[0].clone();
    for npc in [&a_victim, &c_victim] {
        {
            let mut d = npc.data();
            d.unit.hp = 0;
            d.dead_time_ms = crate::world::now_ms();
        }
        spawn_manager::on_npc_died(&server, npc);
    }
    assert_eq!(a.scheduled_count(), 1);
    assert_eq!(c.scheduled_count(), 1);
    assert_eq!(manager.pending_respawns().len(), 2);
    let c_deadline = manager
        .pending_respawns()
        .into_iter()
        .find(|(id, _)| *id == c_victim.obj_id)
        .unwrap()
        .1;

    // b forces the shared placement: a's credit is reset even though b is the caller, and the
    // other placement keeps its timer despite using the same spawner template id.
    let forced = b.force_spawn(&server, 0).expect("forced NPC");
    assert_eq!(
        a.scheduled_count(),
        0,
        "the cancelled clone's credit is reset, not just the caller's"
    );
    assert_eq!(b.scheduled_count(), 0, "the caller has no stale credit");
    assert_eq!(
        c.scheduled_count(),
        1,
        "the other placement keeps its credit"
    );
    let pending = manager.pending_respawns();
    assert_eq!(pending.len(), 1, "only the shared placement was cancelled");
    assert_eq!(pending[0].0, c_victim.obj_id);
    assert_eq!(pending[0].1, c_deadline, "the other deadline is unchanged");
    assert_eq!(forced.data().unit.template_id, 501);
}

#[tokio::test]
async fn direct_delete_reconciles_membership_and_refills_after_leave_return() {
    let server = server_with_authored_spawner();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let mut def = NpcSpawnerDef {
        base: SpawnerBase {
            unit_id: 500,
            ..SpawnerBase::default()
        },
        count: 1,
        npc_spawner_ids: Vec::new(),
    };
    def.base.position.x = 100.0;
    def.base.position.y = 100.0;
    def.base.position.z = 100.0;
    assert_eq!(manager.add_npc_spawner(&server, def), Ok(1));
    let spawner = manager
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    spawner.update(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    assert_eq!(spawner.current_spawn_count(), 1);

    // A quest script removes the alive static NPC without its spawner's despawn path.
    crate::systems::npcs::npc::manager::delete(&server, &world, &npc);
    assert!(world.npc(npc.obj_id).is_none());
    assert_eq!(
        spawner.spawned_npcs().values().flatten().count(),
        0,
        "the exact NPC left the shared membership"
    );
    assert_eq!(spawner.current_spawn_count(), 0);

    // The player leaves (production movement: transform + `add_visible_object`), which
    // invalidates the proximity caches immediately; the placement is not ticked while away.
    player.data().unit.transform.set_xyz(1200.0, 1200.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    spawn_manager::active_region_tick(&server);
    assert_eq!(world.all_npcs().len(), 0, "nothing spawns without a player");

    // Returning refills the placement through the normal active-region tick (no 10 s wait).
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "replacement spawned after the player returned"
    );
    let replacement = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    assert!(!Arc::ptr_eq(&replacement, &npc));
    assert!(world.npc(replacement.obj_id).is_some());
}

#[tokio::test]
async fn despawn_with_respawn_reserves_the_slot_until_its_deadline() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let spawner = make_spawner(template(120, vec![member(1200, 120, 500)]), 500, 1);

    spawner.update(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    spawner.set_respawn_time(30);
    spawner.despawn_with_respawn(&server, &npc);
    assert!(world.npc(npc.obj_id).is_none(), "deleted immediately");
    assert_eq!(
        spawner.spawned_npcs().values().flatten().count(),
        0,
        "membership reconciled before the respawn was queued"
    );
    assert_eq!(spawner.scheduled_count(), 1, "slot credit queued");
    assert_eq!(manager.pending_respawns().len(), 1);

    // Before the deadline the reserved slot must not refill.
    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 0, "no early fill");
    assert_eq!(world.all_npcs().len(), 0);

    // The pending respawn owns the id until it fires.
    let probe = server.ids.object.get_next_id().unwrap();
    assert_ne!(probe, npc.obj_id, "id reserved by the pending respawn");
    server.ids.object.release_id(probe);

    spawn_manager::advance_test_clock(Duration::from_secs(31));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_respawns().is_empty());
    let recycled = server.ids.object.get_next_id().unwrap();
    assert_eq!(
        recycled, npc.obj_id,
        "id released when the respawn timer finished"
    );
    server.ids.object.release_id(recycled);

    spawner.update(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "refilled after the deadline"
    );
    assert_eq!(
        spawner.scheduled_count(),
        0,
        "the queued credit is consumed by the refill"
    );
}

#[tokio::test]
async fn delete_isolates_shared_clones_and_same_template_placements() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let mut a_template = template(130, vec![member(1300, 130, 500)]);
    a_template.min_population = 2;
    a_template.max_population = 2;
    let b_template = template(131, vec![member(1310, 131, 501)]);
    let shared = NpcSpawnerShared::new(position(100.0, 100.0));
    let a = make_spawner_shared(shared.clone(), a_template, 500, 1);
    let b = make_spawner_shared(shared.clone(), b_template, 501, 1);
    // The same spawner template id and member at another position: its own shared placement.
    let other = make_spawner_shared(
        NpcSpawnerShared::new(position(300.0, 300.0)),
        template(131, vec![member(1320, 131, 501)]),
        501,
        1,
    );
    assert_eq!(b.spawner_id, other.spawner_id);
    assert!(!Arc::ptr_eq(b.shared(), other.shared()));
    let client = TestClient::new(&server, 1, 1);
    let _player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    b.do_spawn(&server);
    a.do_spawn(&server);
    other.do_spawn(&server);
    assert_eq!((b.current_spawn_count(), a.current_spawn_count()), (1, 1));
    assert_eq!(other.current_spawn_count(), 1);

    // Populate a's "other populations" cache while b's NPC is alive, then delete b's NPC.
    assert_eq!(a.are_other_npcs_in_spawn_zone(), (true, 1));
    let b_npc = b.spawned_npcs().get(&b.spawner_id).unwrap()[0].clone();
    crate::systems::npcs::npc::manager::delete(&server, &world, &b_npc);

    assert!(world.npc(b_npc.obj_id).is_none());
    assert_eq!(
        b.current_spawn_count(),
        0,
        "only the deleted clone lost its NPC"
    );
    assert_eq!(
        a.current_spawn_count(),
        1,
        "the shared clone keeps its own NPC"
    );
    assert_eq!(
        other.current_spawn_count(),
        1,
        "the other placement keeps its own NPC"
    );
    assert_eq!(
        a.are_other_npcs_in_spawn_zone(),
        (false, 0),
        "the population cache was invalidated by the removal"
    );
    let a_npc = a.spawned_npcs().get(&a.spawner_id).unwrap()[0].clone();
    let other_npc = other.spawned_npcs().get(&other.spawner_id).unwrap()[0].clone();
    assert!(world.npc(a_npc.obj_id).is_some());
    assert!(world.npc(other_npc.obj_id).is_some());

    // The freed shared slot refills through the surviving clone, not the other placement.
    a.update(&server);
    assert_eq!(a.current_spawn_count(), 2, "the shared placement refills");
    assert_eq!(other.current_spawn_count(), 1);
}

#[tokio::test]
async fn direct_delete_keeps_pending_corpse_and_id_ownership() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let spawner = make_spawner(template(140, vec![member(1400, 140, 500)]), 500, 1);

    spawner.update(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    {
        let mut d = npc.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &npc);
    assert_eq!(manager.pending_respawns().len(), 1);
    assert_eq!(manager.pending_despawns().len(), 1);

    // The corpse is deleted directly (script) before its timers finish.
    crate::systems::npcs::npc::manager::delete(&server, &world, &npc);
    assert!(world.npc(npc.obj_id).is_none());
    assert_eq!(spawner.spawned_npcs().values().flatten().count(), 0);

    // Both timers still own the id.
    let probe = server.ids.object.get_next_id().unwrap();
    assert_ne!(
        probe, npc.obj_id,
        "id owned while corpse/respawn timers run"
    );
    server.ids.object.release_id(probe);

    // The respawn deadline passes first; the corpse timer keeps owning the id.
    spawn_manager::advance_test_clock(Duration::from_secs(11));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_respawns().is_empty());
    assert_eq!(manager.pending_despawns().len(), 1);
    let probe = server.ids.object.get_next_id().unwrap();
    assert_ne!(probe, npc.obj_id, "corpse timer still owns the id");
    server.ids.object.release_id(probe);

    // The corpse deadline releases the id; the next fill reuses it for a fresh NPC.
    spawn_manager::advance_test_clock(Duration::from_secs(10));
    manager.check_respawns_tick(&server);
    assert!(manager.pending_despawns().is_empty());
    let recycled = server.ids.object.get_next_id().unwrap();
    assert_eq!(
        recycled, npc.obj_id,
        "id released when both timers finished"
    );
    server.ids.object.release_id(recycled);

    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
    let replacement = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    assert_eq!(replacement.obj_id, npc.obj_id);
    assert!(world.npc(replacement.obj_id).is_some());
    assert!(!Arc::ptr_eq(&replacement, &npc));
}

#[tokio::test]
async fn stale_delete_of_a_reused_id_leaves_the_replacement_untouched() {
    let server = server();
    // The AI hook is installed so a stale call would really detach the registered AI if the
    // Arc-identity guard were missing.
    crate::systems::ai::hooks::install(&server);
    let world = server.world.world(0).unwrap();
    let spawner = make_spawner(template(150, vec![member(1500, 150, 500)]), 500, 1);

    spawner.update(&server);
    let stale = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    crate::systems::npcs::npc::manager::delete(&server, &world, &stale);
    assert!(world.npc(stale.obj_id).is_none());

    // Repeating the deletion of the same already removed NPC stays harmless.
    crate::systems::npcs::npc::manager::delete(&server, &world, &stale);
    assert!(world.npc(stale.obj_id).is_none());
    assert_eq!(spawner.spawned_npcs().values().flatten().count(), 0);

    // Release the id and refill: the replacement reuses the object id with a new Arc.
    server.ids.object.release_id(stale.obj_id);
    spawn_manager::forget_object(&server, stale.obj_id);
    spawner.update(&server);
    let replacement = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    assert_eq!(replacement.obj_id, stale.obj_id);
    assert!(!Arc::ptr_eq(&replacement, &stale));
    server.systems.ai.ais.add_ai(
        &replacement,
        crate::systems::ai::framework::AiKind::HoldPosition,
    );

    // The stale Arc must not detach the replacement's AI (nor its simulation) or remove its
    // world object / membership entry.
    crate::systems::npcs::npc::manager::delete(&server, &world, &stale);
    assert!(
        world
            .npc(stale.obj_id)
            .is_some_and(|n| Arc::ptr_eq(&n, &replacement)),
        "the replacement's world object is untouched"
    );
    assert!(
        server.systems.ai.ais.get(stale.obj_id).is_some(),
        "the replacement keeps its AI"
    );
    assert!(
        server.systems.ai.simulations.contains_key(&stale.obj_id),
        "the replacement keeps its simulation"
    );
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "the replacement keeps its population slot"
    );
    assert_eq!(
        spawner
            .spawned_npcs()
            .values()
            .flatten()
            .filter(|n| Arc::ptr_eq(n, &replacement))
            .count(),
        1,
        "the replacement keeps its membership entry"
    );

    // Object ids are global: the replacement can live in another instance while the stale ref
    // is deleted through its old world (the passed world stays the removal target).
    crate::systems::npcs::npc::manager::delete(&server, &world, &replacement);
    assert!(world.npc(stale.obj_id).is_none());
    server.ids.object.release_id(stale.obj_id);
    spawn_manager::forget_object(&server, stale.obj_id);
    let other_world = server
        .world
        .create_world_instance(world.template.clone(), 1);
    let cross = Entity::new(stale.obj_id, Npc::new(500));
    other_world.spawn(&cross, &server.view_ctx());
    server
        .systems
        .ai
        .ais
        .add_ai(&cross, crate::systems::ai::framework::AiKind::HoldPosition);

    crate::systems::npcs::npc::manager::delete(&server, &world, &stale);
    assert!(
        server
            .world
            .object(stale.obj_id)
            .is_some_and(|o| o.as_npc().is_some_and(|n| Arc::ptr_eq(n, &cross))),
        "the replacement in the other instance is untouched"
    );
    assert!(
        server.systems.ai.ais.get(stale.obj_id).is_some(),
        "the other instance's AI is not detached"
    );
    assert!(world.object(stale.obj_id).is_none());
}

#[tokio::test]
async fn direct_delete_serializes_with_the_spawn_lock() {
    let server = server();
    let world = server.world.world(0).unwrap();
    let spawner = make_spawner(template(160, vec![member(1600, 160, 500)]), 500, 1);
    spawner.update(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();

    // Hold the spawners' reentrant lock like an activation tick in progress.
    let guard = server.systems.npcs.spawns.spawn_lock();
    let (started_tx, started_rx) = std::sync::mpsc::channel();
    let (done_tx, done_rx) = std::sync::mpsc::channel();
    let deleter = {
        let server = Arc::clone(&server);
        let world = Arc::clone(&world);
        let npc = npc.clone();
        std::thread::spawn(move || {
            started_tx.send(()).unwrap();
            crate::systems::npcs::npc::manager::delete(&server, &world, &npc);
            done_tx.send(()).unwrap();
        })
    };
    started_rx.recv().expect("deleter started");
    // Completion requires the lock this thread holds, so neither the population slot nor the
    // world object may change yet (not a timing assertion: the lock makes it impossible).
    assert!(
        matches!(
            done_rx.try_recv(),
            Err(std::sync::mpsc::TryRecvError::Empty)
        ),
        "the deletion ran while the spawn lock was held"
    );
    assert!(world.npc(npc.obj_id).is_some());
    assert_eq!(spawner.current_spawn_count(), 1);

    drop(guard);
    done_rx
        .recv_timeout(Duration::from_secs(5))
        .expect("the deletion resumed after the spawn lock was released");
    deleter.join().unwrap();
    assert!(world.npc(npc.obj_id).is_none());
    assert_eq!(spawner.spawned_npcs().values().flatten().count(), 0);
}
