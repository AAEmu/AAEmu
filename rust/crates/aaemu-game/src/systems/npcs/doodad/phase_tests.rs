//! Phase-machine tests: synthetic func groups (Timer / Final / Tod / RatioChange / Growth /
//! PulseTrigger, the `ListGroupId` recursion guard, `NextPhase == -1`, `OverridePhase`) and the
//! real template 2336 (gated by `AAEMU_COMPACT_DB`).

use std::sync::Arc;

use super::*;
use crate::data::GameData;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::data::DoodadData;
use crate::systems::npcs::doodad::manager;
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
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Alpha", 100.0, 100.0),
        0x100,
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

fn group(d: &DoodadRef) -> u32 {
    d.data().func_group_id
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
    // Two timers were started (the Use loop runs DoChangePhase(11) twice, as in C#); both fire
    // cancelled.
    assert_eq!(f.advance(30_000), 2);
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

    // TimeManager.OnTimeOfDayChange: 19:59 -> 20:01 fires the trigger without phase funcs.
    manager::on_time_of_day_change(&f.server, 20.02, 19.98);
    assert_eq!(group(&d), 10);
    assert_eq!(f.phase_packets(), vec![10]);
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
async fn pulse_trigger_is_one_shot_per_shared_row() {
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
    // InitDoodad has no caster: PulseTrigger stops without changing the phase.
    assert_eq!(group(&d), 10);
    let caster = f.caster();
    let ctx = DoodadCtx::new(&f.server, f.world.clone(), d.clone());
    do_change_phase(&ctx, Some(&caster), 10);
    assert_eq!(group(&d), 11);
    assert!(f.server.systems.npcs.doodads.pulse_triggered(10, 0));
    f.use_(&d); // 11 -> 10: the row was triggered already, stays in 10
    assert_eq!(group(&d), 10);
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
