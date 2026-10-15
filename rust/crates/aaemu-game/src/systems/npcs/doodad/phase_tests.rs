//! Phase-machine tests: synthetic func groups (Timer / Final / Tod / RatioChange / Growth /
//! PulseTrigger, the `ListGroupId` recursion guard, `NextPhase == -1`, `OverridePhase`) and the
//! real template 2336 (gated by `AAEMU_COMPACT_DB`).

use std::sync::Arc;

use super::*;
use crate::data::world_templates::WorldSpawnPosition;
use crate::data::GameData;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::data::DoodadData;
use crate::systems::npcs::doodad::logic;
use crate::systems::npcs::doodad::manager;
use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
use crate::systems::npcs::doodad::templates::*;
use crate::test_support::*;
use crate::world::{PlayerRef, WorldInstance};

const TEMPLATE: u32 = 100;
const FAKE_SKILL: u32 = 999;

/// Synthetic `DoodadData` builder.
#[derive(Default)]
struct Builder {
    data: DoodadData,
}

impl Builder {
    fn template(mut self, id: u32, groups: &[(u32, u32)]) -> Self {
        let mut t = DoodadTemplate {
            id,
            ..DoodadTemplate::default()
        };
        for &(gid, kind) in groups {
            let g = DoodadFuncGroups {
                id: gid,
                almighty: id,
                group_kind_id: kind,
                ..DoodadFuncGroups::default()
            };
            t.func_groups.push(g.clone());
            self.data
                .all_func_groups_index
                .insert(gid, self.data.all_func_groups.len());
            self.data.all_func_groups.push(g);
        }
        self.data.template_order.push(id);
        self.data.templates.insert(id, t);
        self
    }

    /// A `DoodadFuncFakeUse` (both skill ids 0: always sets `ToNextPhase`).
    fn fake_use(mut self, group: u32, func_id: u32, next_phase: i32) -> Self {
        self.data
            .func_templates
            .entry("DoodadFuncFakeUse")
            .or_default()
            .insert(
                func_id,
                FuncTemplate::FakeUse(DoodadFuncFakeUse {
                    id: func_id,
                    ..DoodadFuncFakeUse::default()
                }),
            );
        let key = self.data.funcs_by_id.len() as u32 + 1;
        let f = DoodadFunc {
            group_id: group,
            func_id,
            func_key: key,
            func_type: "DoodadFuncFakeUse".into(),
            next_phase,
            ..DoodadFunc::default()
        };
        self.data.funcs_by_id.insert(key, f.clone());
        self.data.funcs_by_groups.entry(group).or_default().push(f);
        self
    }

    fn phase_func(mut self, group: u32, t: FuncTemplate) -> Self {
        let (class, id) = (t.func_type(), t.id());
        self.data
            .phase_func_templates
            .entry(class)
            .or_default()
            .insert(id, t);
        self.data
            .phase_funcs
            .entry(group)
            .or_default()
            .push(DoodadPhaseFunc {
                group_id: group,
                func_id: id,
                func_type: class.into(),
            });
        self
    }

    fn server(self) -> GameServer {
        let mut gd = GameData::empty();
        gd.systems.npcs.doodad = self.data;
        let s = test_server_with(gd);
        s.systems.npcs.doodads.tasks.set_manual(true);
        s
    }
}

struct Fx {
    server: GameServer,
    client: TestClient,
    player: PlayerRef,
    world: Arc<WorldInstance>,
}

fn fx(b: Builder) -> Fx {
    let server = b.server();
    let mut client = TestClient::new(&server, 1, 1001);
    let player_obj_id = server.ids.object.get_next_id().unwrap();
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Alpha", 100.0, 100.0),
        player_obj_id,
    );
    client.drain();
    let world = server.world.world(0).unwrap();
    Fx {
        server,
        client,
        player,
        world,
    }
}

impl Fx {
    /// `Create(skipPhaseInitialization: true)`, placed and spawned, then `InitDoodad`.
    fn doodad(&mut self, template: u32) -> DoodadRef {
        let d = manager::create(&self.server, &self.world, 0x500, template, None, true).unwrap();
        d.data().transform.set_xyz(120.0, 130.0, 100.0);
        manager::spawn(&self.server, &self.world, &d);
        manager::init_doodad(&self.server, &self.world, &d);
        d
    }

    fn caster(&self) -> WorldObject {
        WorldObject::from(&self.player)
    }

    fn use_(&self, d: &DoodadRef) {
        manager::use_doodad(
            &self.server,
            &self.world,
            d,
            Some(&self.caster()),
            FAKE_SKILL,
            0,
        );
    }

    fn advance(&self, ms: u64) -> usize {
        self.server
            .systems
            .npcs
            .doodads
            .tasks
            .advance(&self.server, ms)
    }

    /// Func group ids of the drained `SCDoodadPhaseChangedPacket`s.
    fn phase_packets(&mut self) -> Vec<u32> {
        self.client
            .drain()
            .into_iter()
            .filter(|f| f.0 == sc::SCDoodadPhaseChangedPacket)
            .map(|f| u32::from_le_bytes(f.1[3..7].try_into().unwrap()))
            .collect()
    }
}

fn timer(id: u32, delay: i32, next_phase: i32) -> FuncTemplate {
    FuncTemplate::Timer(DoodadFuncTimer {
        id,
        delay,
        next_phase,
        ..DoodadFuncTimer::default()
    })
}

fn ratio(id: u32, ratio: i32, next_phase: i32) -> FuncTemplate {
    FuncTemplate::RatioChange(DoodadFuncRatioChange {
        id,
        ratio,
        next_phase,
    })
}

fn ratio_respawn(id: u32, ratio: i32, spawn_doodad_id: u32) -> FuncTemplate {
    FuncTemplate::RatioRespawn(DoodadFuncRatioRespawn {
        id,
        ratio,
        spawn_doodad_id,
    })
}

/// A real spawner for `unit_id` in the test world; `id > 0` because `RatioRespawn` ignores
/// spawners without an index.
fn new_spawner(unit_id: u32, id: u32) -> Arc<DoodadSpawner> {
    Arc::new(DoodadSpawner::new(
        DoodadSpawnerDef {
            base: SpawnerBase {
                id,
                unit_id,
                position: WorldSpawnPosition {
                    world_id: 0,
                    zone_id: TEST_ZONE,
                    x: 120.0,
                    y: 130.0,
                    z: 100.0,
                    ..Default::default()
                },
                ..Default::default()
            },
            scale: 1.0,
            count: 1,
            related_ids: None,
            respawn_doodad_template_id: 0,
        },
        Some(0),
    ))
}

fn group(d: &DoodadRef) -> u32 {
    d.data().func_group_id
}

#[tokio::test]
async fn loaded_player_doodad_is_spawned_before_its_immediate_final_phase() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .phase_func(
            10,
            FuncTemplate::Final(DoodadFuncFinal {
                id: 1,
                after: 0,
                respawn: false,
                ..Default::default()
            }),
        );
    let f = fx(b);
    let doodad = manager::create(&f.server, &f.world, 0, TEMPLATE, None, true).unwrap();
    doodad.data().is_persistent = true;
    let spawns = f.server.systems.npcs.spawns.get_or_create(&f.world);
    spawns.add_player_doodad(doodad.clone());
    spawns.spawn_player_doodads(&f.server);
    assert!(doodad.data().phase.deleted);
    assert!(f.world.doodad(doodad.obj_id).is_none());
    assert!(spawns.get_all_player_doodads().is_empty());
}

#[tokio::test]
async fn family_pulses_follow_flags_and_owner_scope_in_both_load_orders() {
    let b = Builder::default()
        .template(100, &[(10, 1)])
        .template(101, &[(20, 1), (21, 2)])
        .phase_func(
            10,
            FuncTemplate::LogicFamilyProvider(DoodadFuncLogicFamilyProvider {
                id: 1,
                family_id: 7,
            }),
        )
        .phase_func(
            20,
            FuncTemplate::LogicFamilySubscriber(DoodadFuncLogicFamilySubscriber {
                id: 2,
                family_id: 7,
            }),
        )
        .phase_func(
            21,
            FuncTemplate::LogicFamilySubscriber(DoodadFuncLogicFamilySubscriber {
                id: 2,
                family_id: 7,
            }),
        )
        .phase_func(
            20,
            FuncTemplate::PulseTrigger(DoodadFuncPulseTrigger {
                id: 3,
                flag: true,
                next_phase: 21,
            }),
        )
        .phase_func(
            21,
            FuncTemplate::PulseTrigger(DoodadFuncPulseTrigger {
                id: 4,
                flag: false,
                next_phase: 20,
            }),
        );
    let f = fx(b);
    let make = |template| {
        let d = manager::create(&f.server, &f.world, 0, template, None, true).unwrap();
        d.data().transform.set_xyz(101.0, 100.0, 100.0);
        manager::spawn(&f.server, &f.world, &d);
        manager::init_doodad(&f.server, &f.world, &d);
        d
    };
    let subscriber = make(101);
    let other_owner = make(101);
    other_owner.data().owner_id = 99;
    let provider = make(100);
    let ctx = DoodadCtx::new(&f.server, f.world.clone(), provider.clone());
    assert_eq!(
        group(&subscriber),
        20,
        "registration alone must not activate a trigger"
    );
    logic::emit(&ctx, None, true);
    assert_eq!(group(&subscriber), 21);
    assert_eq!(group(&other_owner), 20);
    let late_subscriber = make(101);
    assert_eq!(
        group(&late_subscriber),
        21,
        "subscriber must adopt an existing provider"
    );
    logic::emit(&ctx, None, false);
    assert_eq!(group(&subscriber), 20);
    assert_eq!(group(&late_subscriber), 20);
}

#[tokio::test]
async fn use_then_timer_returns_to_start_phase() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(10, 1, 11)
        .phase_func(11, timer(5, 30_000, 10));
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    assert_eq!(group(&d), 10);
    assert_eq!(f.phase_packets(), vec![10]); // InitDoodad -> DoChangePhase(start)

    f.use_(&d);
    assert_eq!(group(&d), 11);
    assert_eq!(f.phase_packets(), vec![11]);
    assert_eq!(
        f.server.systems.npcs.doodads.tasks.pending_delays(),
        vec![30_000]
    );
    assert!(d.data().phase.func_task != 0);
    // TimeLeft shows the growth time of the timer.
    assert!(d.data().time_left(crate::world::now_ms()) > 29_000);

    assert_eq!(f.advance(29_999), 0);
    assert_eq!(group(&d), 11);
    assert_eq!(f.advance(1), 1);
    assert_eq!(group(&d), 10);
    assert_eq!(f.phase_packets(), vec![10]);
    assert_eq!(d.data().phase.func_task, 0);
}

#[tokio::test]
async fn phase_change_cancels_pending_timer() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(10, 1, 11)
        .fake_use(11, 2, 10)
        .phase_func(11, timer(5, 30_000, 10));
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    f.use_(&d);
    assert_eq!(group(&d), 11);
    f.use_(&d); // back to 10 by hand: the timer is cancelled
    assert_eq!(group(&d), 10);
    f.phase_packets();
    // The phase is entered once, and its canceled timer cannot change the new phase.
    assert_eq!(f.advance(30_000), 1);
    assert_eq!(group(&d), 10);
    assert!(f.phase_packets().is_empty());
}

#[tokio::test]
async fn tod_overrides_phase_and_registers_triggers() {
    // 12:00 game time: tod 600 (6:00) has passed, tod 2000 (20:00) has not.
    let tod = |id, tod: u32, next_phase| {
        FuncTemplate::Tod(DoodadFuncTod {
            id,
            tod,
            next_phase,
            tod_as_hours: crate::systems::npcs::doodad::data::tod_as_hours(id, tod),
        })
    };
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (12, 2), (13, 2)])
        .phase_func(10, tod(1, 2000, 13))
        .phase_func(10, tod(2, 600, 12))
        .phase_func(12, tod(3, 2000, 10));
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    assert_eq!(group(&d), 12);
    assert_eq!(f.phase_packets(), vec![12]);
    assert_eq!(d.data().phase.tod_triggers, vec![(20.0, 10)]);

    // Enter the triggered phase, including its functions, on the scheduled task.
    manager::on_time_of_day_change(&f.server, 20.02, 19.98);
    f.advance(0);
    assert_eq!(group(&d), 12); // the synthetic server is still at 12:00
    assert_eq!(f.phase_packets(), vec![12]);
}

#[tokio::test]
async fn ratio_change_by_roll() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (21, 2)])
        .phase_func(10, ratio(1, 500, 21));
    let mut f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(9000));
    let d = f.doodad(TEMPLATE);
    assert_eq!(group(&d), 10);

    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(500));
    let d2 = manager::create(&f.server, &f.world, 0x501, TEMPLATE, None, true).unwrap();
    manager::init_doodad(&f.server, &f.world, &d2);
    assert_eq!(group(&d2), 21);
    assert_eq!(d2.data().phase.override_phase, 0);
    f.phase_packets();
}

#[tokio::test]
async fn recursion_guard_stops_ping_pong() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(10, 1, 11)
        .phase_func(10, ratio(1, 10_000, 11))
        .phase_func(11, ratio(2, 10_000, 10));
    let mut f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let d = f.doodad(TEMPLATE);
    // 10 -> 11 -> 10 (in ListGroupId, has funcs) -> stop
    assert_eq!(group(&d), 10);
    assert!(d.data().phase.list_group_id.is_empty());
    assert_eq!(f.phase_packets(), vec![10]);
}

#[tokio::test]
async fn ratio_respawn_self_reference_keeps_one_live_doodad_and_resets_guard() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .phase_func(10, ratio_respawn(1, 10_000, TEMPLATE));
    let f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let spawner = new_spawner(TEMPLATE, 501);
    let first = spawner.spawn(&f.server, 0).expect("spawns the template");
    let last = spawner.last().expect("the replacement is tracked");
    assert!(!Arc::ptr_eq(&first, &last));
    assert!(first.data().phase.deleted);
    assert!(!f.world.all_doodads().iter().any(|d| Arc::ptr_eq(d, &first)));
    assert!(!last.data().phase.deleted);
    assert_eq!(last.data().func_group_id, 10);
    assert!(f
        .world
        .doodad(last.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &last)));
    assert_eq!(f.world.all_doodads().len(), 1);
    assert_eq!(spawner.spawned().len(), 1);
    assert!(Arc::ptr_eq(&spawner.spawned()[0], &last));
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);
}

#[tokio::test]
async fn ratio_respawn_two_template_cycle_is_bounded_with_a_live_last_doodad() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .template(TEMPLATE + 1, &[(20, 1)])
        .phase_func(10, ratio_respawn(1, 10_000, TEMPLATE + 1))
        .phase_func(20, ratio_respawn(2, 10_000, TEMPLATE));
    let f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let spawner = new_spawner(TEMPLATE, 502);
    let first = spawner.spawn(&f.server, 0).expect("spawns the template");
    let last = spawner.last().expect("the replacement is tracked");
    assert!(!Arc::ptr_eq(&first, &last));
    assert!(first.data().phase.deleted);
    assert!(!f.world.all_doodads().iter().any(|d| Arc::ptr_eq(d, &first)));
    // T1 -> T2 -> T1 is rejected on the repeated (T1, 10) source, so the newest T1 survives.
    assert_eq!(last.data().template_id, TEMPLATE);
    assert_eq!(last.data().func_group_id, 10);
    assert!(!last.data().phase.deleted);
    assert!(f
        .world
        .doodad(last.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &last)));
    assert_eq!(f.world.all_doodads().len(), 1);
    assert_eq!(spawner.spawned().len(), 1);
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);
}

#[tokio::test]
async fn ratio_respawn_finite_chain_reaches_the_last_template() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .template(TEMPLATE + 1, &[(20, 1)])
        .template(TEMPLATE + 2, &[(30, 1)])
        .phase_func(10, ratio_respawn(1, 10_000, TEMPLATE + 1))
        .phase_func(20, ratio_respawn(2, 10_000, TEMPLATE + 2));
    let f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let spawner = new_spawner(TEMPLATE, 503);
    let first = spawner.spawn(&f.server, 0).expect("spawns the template");
    let last = spawner.last().expect("the replacement is tracked");
    assert!(!Arc::ptr_eq(&first, &last));
    assert!(first.data().phase.deleted);
    assert!(!f.world.all_doodads().iter().any(|d| Arc::ptr_eq(d, &first)));
    assert_eq!(last.data().template_id, TEMPLATE + 2);
    assert_eq!(last.data().func_group_id, 30);
    assert!(!last.data().phase.deleted);
    assert!(f
        .world
        .doodad(last.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &last)));
    assert_eq!(f.world.all_doodads().len(), 1);
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);
}

#[tokio::test]
async fn ratio_respawn_deep_acyclic_chain_stops_at_the_depth_limit() {
    let mut b = Builder::default();
    for i in 0..10u32 {
        b = b.template(TEMPLATE + i, &[(10 + i, 1)]);
    }
    for i in 0..9u32 {
        b = b.phase_func(10 + i, ratio_respawn(i + 1, 10_000, TEMPLATE + i + 1));
    }
    let f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let spawner = new_spawner(TEMPLATE, 505);
    let first = spawner.spawn(&f.server, 0).expect("spawns the template");
    let last = spawner.last().expect("the replacement is tracked");
    assert!(!Arc::ptr_eq(&first, &last));
    assert!(first.data().phase.deleted);
    assert!(!f.world.all_doodads().iter().any(|d| Arc::ptr_eq(d, &first)));
    // Sources T0..T7 occupy the 8 entries; T8 survives rejection and T9 is never reached.
    assert_eq!(last.data().template_id, TEMPLATE + 8);
    assert_eq!(last.data().func_group_id, 18);
    assert!(!last.data().phase.deleted);
    assert!(f
        .world
        .doodad(last.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &last)));
    assert_eq!(f.world.all_doodads().len(), 1);
    assert_eq!(spawner.spawned().len(), 1);
    assert!(Arc::ptr_eq(&spawner.spawned()[0], &last));
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);
}

#[tokio::test]
async fn ratio_respawn_guard_cleans_up_for_a_later_independent_respawn() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .template(TEMPLATE + 1, &[(20, 1)])
        .phase_func(10, ratio_respawn(1, 10_000, TEMPLATE + 1))
        .phase_func(20, ratio_respawn(2, 10_000, TEMPLATE));
    let f = fx(b);
    f.server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(0));
    let spawner = new_spawner(TEMPLATE, 504);
    let first = spawner.spawn(&f.server, 0).expect("spawns the template");
    assert!(!Arc::ptr_eq(&first, &spawner.last().unwrap()));
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);

    // Despawn the retained survivor and respawn independently: the same cycle must run again
    // (a leaked chain entry would reject the first hop and keep the first doodad).
    let stopped = spawner.last().unwrap();
    spawner.despawn(&f.server, &stopped);
    assert!(stopped.data().phase.deleted);
    assert!(!f
        .world
        .all_doodads()
        .iter()
        .any(|d| Arc::ptr_eq(d, &stopped)));
    let first_again = spawner.spawn(&f.server, 0).expect("respawns the template");
    let last = spawner.last().expect("the replacement is tracked");
    assert!(!Arc::ptr_eq(&first_again, &last));
    assert!(!last.data().phase.deleted);
    assert_eq!(last.data().template_id, TEMPLATE);
    assert!(f
        .world
        .doodad(last.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &last)));
    assert_eq!(f.world.all_doodads().len(), 1);
    assert_eq!(spawner.ratio_respawn_chain_depth(), 0);
}

#[tokio::test]
async fn next_phase_minus_one_deletes() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(10, 1, -1);
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    f.client.drain();
    f.use_(&d);
    assert!(d.data().phase.deleted);
    assert!(f.world.doodad(0x500).is_none());
    assert!(f.client.drain_ops().contains(&sc::SCDoodadRemovedPacket));

    // A doodad with only a start group stays (Windstone id=1473).
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .fake_use(10, 1, -1);
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    f.use_(&d);
    assert!(!d.data().phase.deleted);
    assert_eq!(group(&d), 10);
}

#[tokio::test]
async fn override_phase_wins_over_next_phase() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2), (30, 2)])
        .fake_use(10, 1, 11);
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    d.data().phase.override_phase = 30; // e.g. DoodadFuncConditionalUse
    f.use_(&d);
    assert_eq!(group(&d), 30);
    assert_eq!(d.data().phase.override_phase, 0);
    assert_eq!(f.phase_packets(), vec![10, 30]);
}

#[tokio::test]
async fn final_deletes_now_or_after() {
    let fin = |id, after| {
        FuncTemplate::Final(DoodadFuncFinal {
            id,
            after,
            ..DoodadFuncFinal::default()
        })
    };
    // After = 0: deleted at once, no phase packet.
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .phase_func(10, fin(1, 0));
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    assert!(d.data().phase.deleted);
    assert!(f.phase_packets().is_empty());
    assert_eq!(f.server.systems.npcs.doodads.tasks.pending(), 0);

    // After = 5000: TimeLeft counts down, deleted when the task fires.
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1)])
        .phase_func(10, fin(1, 5000));
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    assert!(!d.data().phase.deleted);
    let left = d.data().time_left(crate::world::now_ms());
    assert!((4000..=5000).contains(&left), "{left}");
    assert_eq!(
        f.server.systems.npcs.doodads.tasks.pending_delays(),
        vec![5000]
    );
    f.advance(5000);
    assert!(d.data().phase.deleted);
    assert!(f.world.doodad(0x500).is_none());
}

#[tokio::test]
async fn growth_waits_then_changes_phase() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .phase_func(
            10,
            FuncTemplate::Growth(DoodadFuncGrowth {
                id: 1,
                delay: 8000,
                start_scale: 500,
                end_scale: 1000,
                next_phase: 11,
            }),
        );
    let mut f = fx(b);
    f.server.systems.npcs.doodads.set_growth_rate(2.0);
    let d = f.doodad(TEMPLATE);
    assert_eq!(group(&d), 10);
    assert_eq!(
        f.server.systems.npcs.doodads.tasks.pending_delays(),
        vec![4000]
    );
    f.advance(4000);
    assert_eq!(group(&d), 11);
    assert_eq!(d.data().scale, 1.0); // C# Scale writes have no effect
    assert_eq!(f.phase_packets(), vec![10, 11]);
}

#[tokio::test]
async fn pulse_triggers_require_matching_signal_and_do_not_affect_other_objects() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(11, 1, 10)
        .phase_func(
            10,
            FuncTemplate::PulseTrigger(DoodadFuncPulseTrigger {
                id: 7,
                flag: true,
                next_phase: 11,
            }),
        );
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    // Entering a phase registers a listener; a character's presence is not a pulse.
    assert_eq!(group(&d), 10);
    let caster = f.caster();
    let ctx = DoodadCtx::new(&f.server, f.world.clone(), d.clone());
    do_change_phase(&ctx, Some(&caster), 10);
    assert_eq!(group(&d), 10);
    super::super::logic::receive(&ctx, &d, Some(&caster), true);
    assert_eq!(group(&d), 11);
    f.use_(&d); // 11 -> 10: the row was triggered already, stays in 10
    assert_eq!(group(&d), 10);
    let other = manager::create(&f.server, &f.world, 0x501, TEMPLATE, None, true).unwrap();
    let other_ctx = DoodadCtx::new(&f.server, f.world.clone(), other.clone());
    do_change_phase(&other_ctx, Some(&caster), 10);
    assert_eq!(group(&other), 10);
    super::super::logic::receive(&ctx, &other, Some(&caster), true);
    assert_eq!(group(&other), 11);
}

#[tokio::test]
async fn tod_enters_phase_functions_cancels_old_timer_and_crosses_midnight() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2), (12, 2), (13, 2)])
        .phase_func(
            10,
            FuncTemplate::Timer(DoodadFuncTimer {
                id: 1,
                delay: 5000,
                next_phase: 13,
                ..Default::default()
            }),
        )
        .phase_func(
            11,
            FuncTemplate::Timer(DoodadFuncTimer {
                id: 2,
                delay: 1000,
                next_phase: 12,
                ..Default::default()
            }),
        );
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    d.data().phase.tod_triggers.push((23.99, 11));
    manager::on_time_of_day_change(&f.server, 0.01, 23.98);
    f.advance(0);
    assert_eq!(group(&d), 11);
    f.advance(1000);
    assert_eq!(group(&d), 12);
    f.advance(4000);
    assert_eq!(group(&d), 12);
}

#[tokio::test]
async fn interaction_count_waits_without_restarting_phase_timer() {
    let mut b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2), (12, 2)])
        .fake_use(10, 1, 11)
        .phase_func(
            10,
            FuncTemplate::Timer(DoodadFuncTimer {
                id: 1,
                delay: 5000,
                next_phase: 12,
                ..Default::default()
            }),
        );
    b.data.funcs_by_groups.get_mut(&10).unwrap()[0].count = 2;
    let mut f = fx(b);
    let d = f.doodad(TEMPLATE);
    let timer = d.data().phase.func_task;
    f.use_(&d);
    assert_eq!(group(&d), 10);
    assert_eq!(d.data().phase.func_task, timer);
    f.use_(&d);
    assert_eq!(group(&d), 11);
    f.advance(5000);
    assert_eq!(group(&d), 11);
}

#[tokio::test]
async fn old_timer_and_delete_do_not_mutate_replacement_object() {
    let b = Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .phase_func(
            10,
            FuncTemplate::Timer(DoodadFuncTimer {
                id: 1,
                delay: 1000,
                next_phase: 11,
                ..Default::default()
            }),
        );
    let mut f = fx(b);
    let old = f.doodad(TEMPLATE);
    tasks::schedule_delete(&f.server, &old);
    f.world.delete(&old);
    let replacement =
        manager::create(&f.server, &f.world, old.obj_id, TEMPLATE, None, true).unwrap();
    manager::spawn(&f.server, &f.world, &replacement);
    f.advance(1000);
    assert_eq!(group(&replacement), 10);
    assert!(f
        .world
        .doodad(replacement.obj_id)
        .is_some_and(|d| Arc::ptr_eq(&d, &replacement)));
    manager::delete(&f.server, &f.world, &old);
    assert!(f.world.doodad(replacement.obj_id).is_some());
}

#[tokio::test]
async fn phase_reservation_rejects_concurrent_use_but_allows_nested_calls() {
    let mut f = fx(Builder::default()
        .template(TEMPLATE, &[(10, 1), (11, 2)])
        .fake_use(10, 1, 11));
    let d = f.doodad(TEMPLATE);
    let guard = Operation::enter(&d).unwrap();
    assert!(Operation::enter(&d).is_some());
    std::thread::scope(|scope| {
        scope.spawn(|| f.use_(&d)).join().unwrap();
    });
    assert_eq!(group(&d), 10);
    drop(guard);
    f.use_(&d);
    assert_eq!(group(&d), 11);
}

/// Template 2336 (water the flowerbed, see the header of `Doodad.cs`) from the real DB.
#[tokio::test]
#[ignore]
async fn real_compact_db_template_2336() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = DoodadData::load_from(&conn).unwrap();
    let fake_skill = match data.func_template(543, "DoodadFuncFakeUse") {
        Some(FuncTemplate::FakeUse(t)) if t.fake_skill_id > 0 => t.fake_skill_id,
        _ => FAKE_SKILL,
    };
    let mut f = fx(Builder { data });
    let d = f.doodad(2336);
    // Start phase 4651; its ToD (20:00) has not passed at 12:00.
    assert_eq!(group(&d), 4651);
    assert_eq!(f.phase_packets(), vec![4651]);
    assert!(d.data().phase.tod_triggers.contains(&(20.0, 5136)));

    manager::use_doodad(&f.server, &f.world, &d, Some(&f.caster()), fake_skill, 0);
    assert_eq!(group(&d), 4652);
    assert_eq!(f.phase_packets(), vec![4652]);
    assert_eq!(
        f.server.systems.npcs.doodads.tasks.pending_delays(),
        vec![30_000]
    );

    f.advance(30_000);
    assert_eq!(group(&d), 4651);
    assert_eq!(f.phase_packets(), vec![4651]);
}
