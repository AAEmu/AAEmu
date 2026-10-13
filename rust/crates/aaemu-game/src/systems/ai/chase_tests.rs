//! Regression tests for combat route regeneration, waypoint arrival and NPC headings.

use std::collections::HashMap;
use std::sync::Arc;

use crate::data::world_templates::WorldTemplate;
use crate::data::GameData;
use crate::models::npc::Npc;
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::skills::controllers::Vec3;
use crate::test_support::test_server_with;
use crate::world::{Entity, NpcRef, WorldObject};

use super::super::framework::{AiCtx, AiKind, BehaviorKind, NpcAi};
use super::super::geodata::bai::test_files::net_mission;
use super::super::geodata::bai::{BaiFiles, UsedAreaNames};
use super::super::geodata::manager::AiGeoData;
use super::super::{geodata, npc};

struct Files(HashMap<String, Vec<u8>>);

impl BaiFiles for Files {
    fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
        let prefix = pattern.split('*').next().unwrap_or("");
        let mut files: Vec<_> = self
            .0
            .keys()
            .filter(|p| {
                p.starts_with(dir) && p.rsplit('/').next().unwrap_or("").starts_with(prefix)
            })
            .cloned()
            .collect();
        files.sort();
        files
    }

    fn file_exists(&self, name: &str) -> bool {
        self.0.contains_key(name)
    }

    fn read_file(&self, name: &str) -> Option<Vec<u8>> {
        self.0.get(name).cloned()
    }
}

fn fixture() -> (crate::server::GameServer, NpcRef, WorldObject) {
    let mut data = GameData::empty();
    data.systems.npcs.npc.templates.insert(
        1,
        NpcTemplate {
            id: 1,
            attack_start_range_scale: 2.0,
            ..NpcTemplate::default()
        },
    );
    let server = test_server_with(data);
    server.config.update(|cfg| cfg.world.geo_data_mode = true);
    let files = Files(HashMap::from([(
        "game/worlds/main_world/paths/000_000/netmission0.bai".into(),
        // The start and goal graph nodes are deliberately off the actual chase line.
        net_mission(
            &[(1, [97.0, 102.0, 100.0]), (2, [121.0, 102.0, 100.0])],
            &[(1, 2), (2, 1)],
        ),
    )]));
    server.systems.ai.geodata.install(Arc::new(AiGeoData::new(
        &WorldTemplate::synthetic(0, "main_world", 2, 2, 100),
        true,
        Arc::new(files),
        Arc::new(UsedAreaNames::default()),
        None,
    )));
    let mut n = Npc::new(1);
    n.unit.hp = 1000;
    n.unit.max_hp = 1000;
    n.unit.transform.set_xyz(100.0, 100.0, 100.0);
    n.spawner_z = Some(100.0);
    let npc = Entity::new(0x300, n);
    let mut target = Npc::new(2);
    target.unit.hp = 1000;
    target.unit.transform.set_xyz(120.0, 100.0, 100.0);
    let target = WorldObject::from(Entity::new(0x301, target));
    let world = server.world.world(0).unwrap();
    world.spawn(&npc, &server.view_ctx());
    world.spawn(&target, &server.view_ctx());
    (server, npc, target)
}

fn tick(c: &mut AiCtx<'_>, target: &WorldObject) {
    super::move_in_range(c, BehaviorKind::Attack, Some(target), 100);
}

#[tokio::test]
async fn stationary_target_is_chased_straight_without_restarting_at_graph_centroids() {
    let (server, npc, target) = fixture();
    let mut ai = NpcAi::new(AiKind::HoldPosition, &npc);
    let mut c = AiCtx {
        server: &server,
        npc: npc.clone(),
        obj: WorldObject::from(&npc),
        ai: &mut ai,
    };
    geodata::npc_find_path(&mut c, &target);
    assert_eq!(c.ai.path_node.end_point_pos, Vec3::new(120.0, 100.0, 100.0));
    assert_eq!(
        c.ai.path_node.found_path.front(),
        Some(&Vec3::new(100.0, 100.0, 100.0))
    );
    let mut last_x = 100.0;
    for _ in 0..240 {
        tick(&mut c, &target);
        let pos = npc.data().unit.transform;
        assert!(pos.x >= last_x, "reversed at {pos:?}");
        assert_eq!(pos.y, 100.0, "strafe at {pos:?}");
        last_x = pos.x;
    }
    assert!((last_x - 118.0).abs() < 0.11, "stuck at {last_x}");
}

#[tokio::test]
async fn moving_target_replaces_the_old_route_and_failed_lookup_discards_it() {
    let (server, npc, target) = fixture();
    let mut ai = NpcAi::new(AiKind::HoldPosition, &npc);
    let mut c = AiCtx {
        server: &server,
        npc: npc.clone(),
        obj: WorldObject::from(&npc),
        ai: &mut ai,
    };
    geodata::npc_find_path(&mut c, &target);
    target.with_transform(|t| t.set_xyz(120.0, 110.0, 100.0));
    tick(&mut c, &target);
    assert_eq!(c.ai.path_node.end_point_pos, Vec3::new(120.0, 110.0, 100.0));
    assert_eq!(
        c.ai.path_node.found_path.back(),
        Some(&Vec3::new(120.0, 110.0, 100.0))
    );
    assert!(npc.data().unit.transform.y > 100.0);

    // A route from an earlier world must not survive a missing geodata lookup.
    server.world.world(0).unwrap().delete(&npc);
    npc.data().unit.transform.instance_id = 99;
    geodata::npc_find_path(&mut c, &target);
    assert!(c.ai.path_node.found_path.is_empty());
}

#[tokio::test]
async fn waypoint_height_and_attack_range_do_not_skip_corners_or_stall_arrival() {
    let (server, npc, target) = fixture();
    let mut ai = NpcAi::new(AiKind::HoldPosition, &npc);
    let mut c = AiCtx {
        server: &server,
        npc: npc.clone(),
        obj: WorldObject::from(&npc),
        ai: &mut ai,
    };
    c.ai.path_node.end_point_pos = Vec3::new(120.0, 100.0, 100.0);
    c.ai.path_node.found_path = [
        Vec3::new(100.0, 100.0, 107.0), // already reached in XY; navmesh is above the floor
        Vec3::new(100.0, 101.5, 107.0), // within melee range, but still a required corner
        Vec3::new(120.0, 100.0, 100.0),
    ]
    .into();
    tick(&mut c, &target);
    let pos = npc.data().unit.transform;
    assert_eq!(pos.x, 100.0);
    assert!((pos.y - 100.1).abs() < 0.001, "{pos:?}");
    assert_eq!(c.ai.path_node.found_path.len(), 2);
    for _ in 0..20 {
        tick(&mut c, &target);
    }
    assert!(
        npc.data().unit.transform.x > 100.0,
        "stuck on waypoint height"
    );

    // Once the victim is in range, stale route corners cannot make the NPC orbit it.
    target.with_transform(|t| t.set_xyz(pos.x + 1.0, pos.y, pos.z));
    let before = npc.data().unit.transform;
    tick(&mut c, &target);
    assert_eq!(npc.data().unit.transform, before);
}

#[tokio::test]
async fn landing_on_a_waypoint_keeps_the_travelled_heading() {
    let (server, npc, _) = fixture();
    npc::move_towards(
        &server,
        &npc,
        Some(npc::MoveAi {
            hold_or_idle: false,
        }),
        Vec3::new(100.0, 102.0, 100.0),
        2.0,
        4,
        0.25,
    );
    let pos = npc.data().unit.transform;
    assert_eq!(pos.y, 102.0);
    assert!(pos.rot_z.abs() < 0.001, "arrival turned sideways: {pos:?}");
}
