//! Authored fish controllers own displacement; direction cues must not add AI drift.

use std::sync::Arc;

use crate::data::skills::{BuffKind, BuffTemplate as NetBuffTemplate};
use crate::data::GameData;
use crate::models::buff::Buff;
use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::skills::controllers::{self, Behavior, ScState};
use crate::systems::skills::data::{BuffTemplate, SkillControllerTemplate};
use crate::systems::skills::geometry::Target;
use crate::test_support::test_server_with;
use crate::world::{now_ms, Entity, NpcRef, WorldObject};

use super::super::framework::{AiCtx, AiKind, BehaviorKind, NpcAi};
use super::super::npc;

const FISH_BUFF: u32 = 5357;
const CUES: [(u32, u32); 4] = [
    (5264, npc::tags::RIGHT),
    (5265, npc::tags::LEFT),
    (5266, npc::tags::DESCEND),
    (5267, npc::tags::BACK),
];

fn fixture(fish: bool, cue: Option<(u32, u32)>) -> (Arc<GameServer>, NpcRef, WorldObject) {
    let mut data = GameData::empty();
    for (id, tag) in std::iter::once((FISH_BUFF, npc::tags::FISH)).chain(CUES) {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            ..BuffTemplate::default()
        });
        data.systems.skills.buff_tags.insert(id, vec![tag]);
        data.systems.skills.tagged_buffs.insert(tag, vec![id]);
    }
    let server = Arc::new(test_server_with(data));
    server.set_self();
    crate::systems::skills::bind(&server);
    let mut n = Npc::new(1);
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit
        .transform
        .set_position(100.0, 100.0, 0.0, 0.0, 0.0, 1.1);
    for id in fish
        .then_some(FISH_BUFF)
        .into_iter()
        .chain(cue.map(|(id, _)| id))
    {
        n.unit.buffs.add_buff(
            Buff::new(
                NetBuffTemplate::simple(id, BuffKind::Good),
                Default::default(),
                None,
                now_ms(),
            ),
            0,
            0,
            now_ms(),
        );
    }
    let npc = Entity::new(0x300, n);
    let mut target = Npc::new(2);
    target.unit.hp = 1000;
    target.unit.transform.set_xyz(120.0, 100.0, 0.0);
    let target = WorldObject::from(Entity::new(0x301, target));
    let world = server.world.world(0).unwrap();
    world.spawn(&npc, &server.view_ctx());
    world.spawn(&target, &server.view_ctx());
    (server, npc, target)
}

fn between_controllers(server: &GameServer, npc: &NpcRef, target: &WorldObject, ticks: usize) {
    let mut ai = NpcAi::new(AiKind::AlmightyNpc, npc);
    let mut ctx = AiCtx {
        server,
        npc: npc.clone(),
        obj: WorldObject::from(npc),
        ai: &mut ai,
    };
    for _ in 0..ticks {
        super::move_in_range(&mut ctx, BehaviorKind::AlmightyAttack, Some(target), 100);
    }
}

fn self_jump() -> SkillControllerTemplate {
    // Actual1.2 controller7040: a self-relative endpoint exposes unwanted AI yaw changes.
    SkillControllerTemplate {
        id: 7040,
        kind_id: 2,
        value: [80, 4, 1000, 4000, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0, 0],
        ..SkillControllerTemplate::default()
    }
}

#[tokio::test]
async fn fish_direction_cues_do_not_drift_or_rotate_between_controllers() {
    for cue in CUES.into_iter().map(Some).chain(std::iter::once(None)) {
        let (server, npc, target) = fixture(true, cue);
        let before = npc.data().unit.transform.local();
        //30 virtual seconds formerly integrated unbounded extra movement under cue tags.
        between_controllers(&server, &npc, &target, 300);
        assert_eq!(npc.data().unit.transform.local(), before, "cue={cue:?}");
    }
}

#[tokio::test]
async fn fish_active_controller_and_next_self_relative_heading_are_preserved() {
    for cue in CUES {
        let (server, npc, target) = fixture(true, Some(cue));
        let obj = WorldObject::from(&npc);
        let make = || {
            controllers::create(
                &server,
                &self_jump(),
                &obj,
                &Target::Obj(obj.clone()),
                0.0,
                0.0,
                0.0,
                0.0,
            )
            .unwrap()
        };
        let before = make();
        let Behavior::BallisticLeap(before_arc) = before.behavior() else {
            panic!("self Leap")
        };
        between_controllers(&server, &npc, &target, 50);
        let after = make();
        let Behavior::BallisticLeap(after_arc) = after.behavior() else {
            panic!("self Leap")
        };
        assert_eq!(
            after_arc.end_position, before_arc.end_position,
            "cue={cue:?}"
        );
        controllers::activate(&server, &after);
        let position = npc.data().unit.transform.local();
        between_controllers(&server, &npc, &target, 50);
        assert_eq!(after.state(), ScState::Running);
        assert!(Arc::ptr_eq(&controllers::active(&obj).unwrap().0, &after.0));
        assert_eq!(npc.data().unit.transform.local(), position);
        let Behavior::BallisticLeap(active_arc) = after.behavior() else {
            panic!("active Leap")
        };
        assert_eq!(active_arc, after_arc);
        after.end(&server, true);
    }
}

#[tokio::test]
async fn direction_cue_without_fish_status_keeps_ordinary_npc_chase() {
    let (server, npc, target) = fixture(false, Some(CUES[2]));
    between_controllers(&server, &npc, &target, 1);
    let pos = npc.data().unit.transform.local();
    assert!((pos.x - 100.1).abs() < 0.001);
    assert_eq!(pos.y, 100.0);
    assert_eq!(pos.z, 0.0);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_carp_direction_plots_have_bounded_leap_endpoints() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let db =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    for (skill, controller, distance, angle, height) in [
        (21646, 7127, 7000, -80, -2000),
        (21647, 7128, 7000, 80, -2000),
        (21098, 7010, 4000, 0, -8000),
    ] {
        let row: (u32, i32, i32, i32, u32, i32) = db.query_row(
            "SELECT x.actual_id,p.target_update_method_param3,p.target_update_method_param4,p.target_update_method_param5,c.kind_id,c.value3 FROM skills s JOIN plot_events p ON p.plot_id=s.plot_id JOIN plot_effects x ON x.event_id=p.id AND x.actual_type='SkillController' JOIN skill_controllers c ON c.id=x.actual_id WHERE s.id=?",
            [skill], |r| Ok((r.get(0)?,r.get(1)?,r.get(2)?,r.get(3)?,r.get(4)?,r.get(5)?)),
        ).unwrap();
        assert_eq!(row, (controller, distance, angle, height, 2, 1500));
    }
}
