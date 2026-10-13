//! Dynamic housing geometry uses the existing CryPhysics model reader and query service.

use std::collections::{BTreeMap, HashMap};
use std::sync::Arc;
use std::time::{Duration, Instant};

use aaemu_physics::geometry::primitives::BoxPrim;
use aaemu_physics::geometry::shapes::stream::{save_phys_geometry, PhysGeomHeader};
use aaemu_physics::geometry::shapes::BoxGeom;
use aaemu_physics::math::{Quat, Vec3};

use crate::data::GameData;
use crate::systems::ai::collision::{self, query};
use crate::systems::housing::{
    data::{HousingBuildStep, HousingTemplate},
    manager,
};
use crate::test_support::test_server_with;
use collision::world::{ObjKind, Owner};

fn moving_box_shape(looped: bool) -> super::ColliderShape {
    let bytes =
        collision::cga::tests::fixture(&[(0, [0.0, 0.0, 1000.0]), (30 * 160, [0.0; 3])], &[]);
    let mut clip = collision::cga::RootClip::read(&bytes).unwrap();
    clip.end_frame = 30.0;
    super::ColliderShape {
        parts: vec![super::ShapePart {
            model: collision::tests::box_model(Vec3::splat(1.0), false),
            pos: Vec3::new(3.0, 0.0, 0.0),
            q: Quat::from_vw(
                Vec3::new(0.0, 0.0, std::f32::consts::FRAC_1_SQRT_2),
                std::f32::consts::FRAC_1_SQRT_2,
            ),
            scale: 2.0,
            animation: Some(super::ShapeAnimation {
                track: Arc::new(collision::cga::RootAnimation {
                    clip,
                    proxy_position: Vec3::X,
                    proxy_quaternion: Quat::IDENTITY,
                }),
                looped,
                speed: 1.0,
                rigid: true,
            }),
        }],
    }
}

fn animated_key() -> collision::world::ColliderKey {
    super::shape_key(
        "test/animated-house",
        Vec3::new(500.0, 500.0, 100.0),
        Quat::IDENTITY,
        1.0,
        (0, u32::MAX),
    )
}

fn animated_top(state: &mut collision::world::WorldState) -> query::Hit {
    state
        .raycast(
            Vec3::new(503.0, 502.0, 150.0),
            Vec3::new(503.0, 502.0, 99.0),
            query::mask::SOLID_TERRAIN,
            query::ray_flags::DEFAULT,
            &[],
        )
        .unwrap()
}

#[test]
fn animated_housing_proxy_updates_rays_without_replacing_entity_or_geometry() {
    let world = collision::tests::flat_world(100.0);
    let mut state = world.lock();
    let shape = moving_box_shape(false);
    let start = Instant::now();
    assert!(state.set_object_shape_at(
        ObjKind::House,
        7,
        animated_key(),
        Some(&shape),
        true,
        start,
    ));
    let entity = state.objects[&(ObjKind::House, 7)].ent;
    assert_eq!(state.world.body_state(entity).sim_class, 2);
    let part_geometry = state.world.ent(entity).parts[0].pgeom.clone();
    assert!((animated_top(&mut state).pos.z - 122.0).abs() < 1e-3);
    // Native tower proxies are ent_rigid (mask4); static-only floor queries exclude them.
    let static_hit = state
        .raycast(
            Vec3::new(503.0, 502.0, 150.0),
            Vec3::new(503.0, 502.0, 99.0),
            query::mask::STATIC_TERRAIN,
            query::ray_flags::DEFAULT,
            &[],
        )
        .unwrap();
    assert_eq!(static_hit.owner, Some(Owner::Terrain));
    assert_eq!(state.update_animated_houses_at(start), 0);
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_millis(500)),
        1
    );
    let hit = animated_top(&mut state);
    assert!((hit.pos.z - 112.0).abs() < 1e-3, "{hit:?}");
    assert_eq!(
        hit.owner,
        Some(Owner::Object {
            kind: ObjKind::House,
            obj_id: 7
        })
    );
    assert_eq!(state.objects[&(ObjKind::House, 7)].ent, entity);
    assert_eq!(state.world.ent(entity).parts.len(), 1);
    assert!(Arc::ptr_eq(
        &state.world.ent(entity).parts[0].pgeom,
        &part_geometry
    ));
    assert_eq!(state.stats.object_colliders, 1);
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_secs(1)),
        1
    );
    assert!((animated_top(&mut state).pos.z - 102.0).abs() < 1e-3);
    // A settled collapse remains physicalized; subsequent queries do no pose work.
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_secs(99)),
        0
    );
    assert!((animated_top(&mut state).pos.z - 102.0).abs() < 1e-3);
}

#[test]
fn animated_housing_loop_keeps_clock_on_unchanged_sync_and_cleans_up_on_removal() {
    let world = collision::tests::flat_world(100.0);
    let mut state = world.lock();
    let shape = moving_box_shape(true);
    let start = Instant::now();
    assert!(state.set_object_shape_at(
        ObjKind::House,
        8,
        animated_key(),
        Some(&shape),
        true,
        start,
    ));
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_millis(500)),
        1
    );
    assert!(!state.set_object_shape_at(
        ObjKind::House,
        8,
        animated_key(),
        Some(&shape),
        true,
        start + Duration::from_millis(500),
    ));
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_secs(1)),
        1
    );
    assert!((animated_top(&mut state).pos.z - 122.0).abs() < 1e-3);
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_millis(1500)),
        1
    );
    assert!((animated_top(&mut state).pos.z - 112.0).abs() < 1e-3);
    assert!(state.remove_object_collider(ObjKind::House, 8));
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_secs(2)),
        0
    );
    assert_eq!(state.stats.object_colliders, 0);
}

#[test]
fn housing_prefab_accepts_authored_static_animobject_and_excludes_dynamic_or_delayed_physics() {
    let xml = r#"<PrefabsLibrary><Prefab Name="tower"><Objects>
      <Object Type="Entity" EntityClass="AnimObject" Pos="1,2,3" Scale="2,2,2">
        <Properties object_Model="game/Objects/tower.cga"><Physics bPhysicalize="1" bArticulated="0" Mass="-1" Density="-1"/></Properties>
      </Object>
      <Object Type="Entity" EntityClass="AnimObject"><Properties object_Model="dynamic.cga">
        <Physics bPhysicalize="1" bArticulated="0" Mass="100" Density="-1"/></Properties></Object>
      <Object Type="Entity" EntityClass="AnimObject"><Properties object_Model="delayed.cga">
        <Physics bPhysicalize="1" bArticulated="0" Mass="-1" Density="-1"/>
        <Animation bPhysicalizeAfterAnimation="1"/></Properties></Object>
      <Object Type="Entity" EntityClass="AnimObject"><Properties object_Model="articulated.cga">
        <Physics bPhysicalize="1" bArticulated="1" Mass="-1" Density="-1"/></Properties></Object>
      <Object Type="Entity" EntityClass="AnimObject"><Properties object_Model="disabled.cga">
        <Physics bPhysicalize="0" bArticulated="0" Mass="-1" Density="-1"/></Properties></Object>
      </Objects></Prefab></PrefabsLibrary>"#;
    let parts = super::prefab_house_geometry(xml, "tower");
    assert_eq!(parts.len(), 1);
    assert_eq!(parts[0].0, "game/objects/tower.cga");
    assert_eq!(parts[0].1, Vec3::new(1.0, 2.0, 3.0));
    assert_eq!(parts[0].3, 2.0);
    // Doodad and vehicle brush-only selection retains its existing behavior.
    assert!(super::prefab_brushes(xml, "tower").is_empty());
}

fn box_cgf() -> Vec<u8> {
    let geometry = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0)));
    let header = PhysGeomHeader {
        inertia: Vec3::splat(1.0),
        q: Quat::IDENTITY,
        origin: Vec3::ZERO,
        volume: 8.0,
        unk52: 1,
        surface_idx: 0,
    };
    let mut blob = Vec::new();
    save_phys_geometry(&header, &geometry, &mut blob);
    collision::cgf::tests::synthetic_cgf(&blob)
}

fn fixture() -> (crate::GameServer, crate::world::HouseRef) {
    let mut data = GameData::empty();
    data.systems.housing.add_template(HousingTemplate {
        id: 300,
        main_model_id: 401,
        hp: 1000,
        build_steps: BTreeMap::from([(
            0,
            HousingBuildStep {
                id: 1,
                housing_id: 300,
                step: 0,
                model_id: 400,
                skill_id: 11000,
                num_actions: 1,
            },
        )]),
        ..Default::default()
    });
    data.systems
        .ai
        .collision
        .prefab_models
        .insert(400, vec!["prefab://prefabs/test.xml/foundation".into()]);
    data.systems.ai.collision.prefab_models.insert(
        401,
        vec![
            "prefab://prefabs/test.xml/walls".into(),
            "prefab://prefabs/test.xml/roof".into(),
        ],
    );
    data.systems.ai.collision.prefab_model_states.insert(
        (401, 2),
        vec![
            "prefab://prefabs/test.xml/walls".into(),
            "prefab://prefabs/test.xml/roof".into(),
        ],
    );
    data.systems
        .ai
        .collision
        .prefab_model_states
        .insert((401, 3), vec!["prefab://prefabs/test.xml/roof".into()]);
    data.systems.ai.collision.prefab_model_states.insert(
        (401, 4),
        vec!["prefab://prefabs/test.xml/foundation".into()],
    );
    let server = test_server_with(data);
    let files = HashMap::from([
        ("game/objects/test/box.cgf".into(), box_cgf()),
        (
            "game/prefabs/test.xml".into(),
            br#"<PrefabsLibrary><Prefab Name="foundation"><Objects>
          <Object Type="Brush" Prefab="Objects/test/box.cgf" Pos="0,0,0"/>
          </Objects></Prefab><Prefab Name="walls"><Objects>
          <Object Type="Brush" Prefab="Objects/test/box.cgf" Pos="3,0,0"/>
          </Objects></Prefab><Prefab Name="roof"><Objects>
          <Object Type="Brush" Prefab="Objects/test/box.cgf" Pos="0,0,2"/>
          </Objects></Prefab></PrefabsLibrary>"#
                .to_vec(),
        ),
    ]);
    server
        .systems
        .ai
        .collision
        .test_files
        .set(Arc::new(files))
        .unwrap();
    server
        .systems
        .ai
        .collision
        .test_terrain
        .set(Arc::new(collision::terrain::tests::Plane {
            a: 100.0,
            b: 0.0,
            c: 0.0,
            n: 1024,
        }))
        .unwrap_or_else(|_| panic!("terrain"));
    let world = server.world.world(0).unwrap();
    let house = manager::create(&server, 300, 101, &world, 0, 0).unwrap();
    house.data().id = server.ids.housing.get_next_id().unwrap();
    house.data().unit.transform.set_xyz(500.0, 500.0, 101.0);
    manager::set_current_step(&server, &house, 0, false);
    assert!(server.systems.housing.manager.insert(&house));
    manager::spawn_house(&server, &house);
    (server, house)
}

#[tokio::test]
async fn housing_floor_loaded_on_first_query_swaps_at_completion_and_disappears_on_delete() {
    let (server, house) = fixture();
    // No residency tick ran. Loading the cell for this ray must include the placed foundation.
    let z = collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!((z - 102.0).abs() < 1e-3, "foundation {z}");
    let hit = collision::raycast(
        &server,
        0,
        [500.0, 500.0, 150.0],
        [500.0, 500.0, 50.0],
        query::mask::STATIC_TERRAIN,
        query::ray_flags::DEFAULT,
    )
    .unwrap()
    .unwrap();
    assert_eq!(
        hit.owner,
        Some(Owner::Object {
            kind: ObjKind::House,
            obj_id: house.obj_id
        })
    );
    manager::add_build_action(&server, &house);
    assert_eq!(house.data().current_step, -1);
    let z = collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!((z - 104.0).abs() < 1e-3, "finished roof {z}");
    assert_eq!(
        collision::line_of_sight(&server, 0, [503.0, 490.0, 101.0], [503.0, 510.0, 101.0]),
        Some(false)
    );
    let pw = collision::phys_world(&server, 0).unwrap();
    assert_eq!(
        pw.lock().objects[&(ObjKind::House, house.obj_id)]
            .models
            .len(),
        2
    );
    let before = pw.lock().stats.object_colliders;
    super::sync_world(&server, &pw);
    assert_eq!(pw.lock().stats.object_colliders, before);
    manager::delete_house(&server, &house);
    assert!(!pw
        .lock()
        .objects
        .contains_key(&(ObjKind::House, house.obj_id)));
    let z = collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!((z - 100.0).abs() < 1e-3, "terrain after deletion {z}");
}

#[tokio::test]
async fn housing_colliders_follow_rotation_and_are_removed_with_unloaded_cells() {
    let (server, house) = fixture();
    collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    manager::add_build_action(&server, &house);
    house.data().unit.transform.rot_z = std::f32::consts::FRAC_PI_2;
    super::sync_house(&server, &house);
    let hit = collision::raycast(
        &server,
        0,
        [500.0, 503.0, 110.0],
        [500.0, 503.0, 99.0],
        query::mask::STATIC_TERRAIN,
        query::ray_flags::DEFAULT,
    )
    .unwrap()
    .unwrap();
    assert_eq!(
        hit.owner,
        Some(Owner::Object {
            kind: ObjKind::House,
            obj_id: house.obj_id
        })
    );
    let pw = collision::phys_world(&server, 0).unwrap();
    pw.lock().remove_cell((0, 0));
    super::sync_world(&server, &pw);
    assert!(!pw
        .lock()
        .objects
        .contains_key(&(ObjKind::House, house.obj_id)));
    assert!(server.world.world(0).unwrap().house(house.obj_id).is_some());
    // Querying the cell again restores the same still-live house collider.
    collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!(pw
        .lock()
        .objects
        .contains_key(&(ObjKind::House, house.obj_id)));
}

#[test]
fn housing_hp_states_match_adjacent_native_cottage_samples_and_integer_rounding() {
    // Actual 1.2 Cottage model1116 native state readouts at each adjacent boundary.
    for (hp, state) in [(1140, 1), (1139, 2), (765, 2), (764, 3), (390, 3), (389, 4)] {
        assert_eq!(super::house_model_state(hp, 1500), state, "hp {hp}");
    }
    assert_eq!(super::house_model_state(1, 1500), 4);
    assert_eq!(super::house_model_state(0, 1500), 6);
    assert_eq!(super::house_model_state(1500, 0), 6);
    assert_eq!(super::house_model_state(i32::MAX, i32::MAX), 1);
}

#[test]
fn housing_construction_collision_max_hp_uses_native_wire_step_and_truncated_division() {
    let mut house = crate::models::house::House::new(1, 172);
    house.unit.max_hp = 1500;
    house.build_step_count = 3;
    house.all_action = 3;
    // Original Cottage1083 native modelparams were HP1500/maxHP375 at wire step-3.
    for (step, maximum) in [(0, 375), (1, 750), (2, 1125), (-1, 1500)] {
        house.current_step = step;
        assert_eq!(super::house_collision_max_hp(&house), maximum);
    }
    // Some house steps consume several packs. Native maximum uses the sum of actions
    // and the signed SCUnitStatePacket step, with truncation before multiplication.
    house.unit.max_hp = 1003;
    house.all_action = 5;
    house.current_step = 0;
    assert_eq!(house.net_build_step(), -3);
    assert_eq!(super::house_collision_max_hp(&house), 501); // (1003 / 6) * 3
    house.current_step = -1;
    assert_eq!(super::house_collision_max_hp(&house), 1003);
    house.current_step = 0;
    house.all_action = 0;
    assert_eq!(super::house_collision_max_hp(&house), 1003);
}

#[tokio::test]
async fn damage_and_healing_swap_housing_wall_and_floor_queries_only_when_geometry_changes() {
    use crate::systems::skills::{combat, types::KillReason};

    let (server, house) = fixture();
    collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    manager::add_build_action(&server, &house);
    let pw = collision::phys_world(&server, 0).unwrap();
    let obj = crate::world::WorldObject::from(&house);
    let normal_ent = pw.lock().objects[&(ObjKind::House, house.obj_id)].ent;
    let damage = |value| {
        combat::reduce_current_hp(&server, &obj, None, value, KillReason::Damage);
    };
    // State 2 reuses the normal geometry, as real Nuian Cottages do. Crossing the HP
    // boundary must not destroy/recreate an unchanged physics entity.
    damage(241);
    assert_eq!(house.data().unit.hp, 759);
    assert_eq!(
        pw.lock().objects[&(ObjKind::House, house.obj_id)].ent,
        normal_ent
    );
    damage(249);
    assert_eq!(house.data().unit.hp, 510);
    assert_eq!(
        pw.lock().objects[&(ObjKind::House, house.obj_id)].ent,
        normal_ent
    );
    assert_eq!(
        collision::line_of_sight(&server, 0, [503.0, 490.0, 101.0], [503.0, 510.0, 101.0]),
        Some(false)
    );
    damage(1);
    assert_eq!(house.data().unit.hp, 509);
    // The damaged fixture has no wall, so an immediate query must see the new opening.
    assert_eq!(
        collision::line_of_sight(&server, 0, [503.0, 490.0, 101.0], [503.0, 510.0, 101.0]),
        Some(true)
    );
    let damaged_ent = pw.lock().objects[&(ObjKind::House, house.obj_id)].ent;
    damage(249);
    assert_eq!(house.data().unit.hp, 260);
    assert_eq!(
        pw.lock().objects[&(ObjKind::House, house.obj_id)].ent,
        damaged_ent
    );
    damage(1);
    assert_eq!(house.data().unit.hp, 259);
    let z = collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!((z - 102.0).abs() < 1e-3, "damaged foundation {z}");
    // The GM heal command uses this same positive-HP callback after refilling the unit.
    house.data().unit.hp = 1000;
    combat::post_update_current_hp(&server, &obj, None, 259, 1000, KillReason::Unknown);
    let z = collision::ground_height(&server, 0, 500.0, 500.0, 103.0).unwrap();
    assert!((z - 104.0).abs() < 1e-3, "healed roof {z}");
    assert_eq!(
        collision::line_of_sight(&server, 0, [503.0, 490.0, 101.0], [503.0, 510.0, 101.0]),
        Some(false)
    );
    assert_eq!(pw.lock().stats.object_colliders, 1);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB and AAEMU_GAME_PAK"]
fn real_housing_prefab_geometry_loads_all_floor_wall_and_roof_elements() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = collision::data::CollisionData::load(&conn).unwrap();
    let app = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = collision::Files::Client(Arc::new(
        crate::data::client_files::ClientFileManager::from_sources(&[pak], &app),
    ));
    let models = collision::models::ModelCache::new(Arc::new(files.clone()));
    assert_eq!(data.prefab_models[&1082].len(), 3);
    // 103/305 are obsolete prototypes whose brush assets were removed from this client.
    // Exercise actual 1.2 design items: Thatched Farmhouse, Nuian Cottage and bungalow.
    for id in [1082, 1116, 1382, 894] {
        let designs: i64 = conn.query_row(
            "SELECT COUNT(*) FROM housings h JOIN item_housings i ON i.design_id = h.id WHERE h.main_model_id = ?",
            [id], |row| row.get(0)).unwrap();
        assert!(designs > 0, "model {id} must have a usable housing design");
        let mut prefab_xml = HashMap::new();
        let parts = data.prefab_models[&id]
            .iter()
            .map(|reference| {
                super::resolve_house_shape(
                    &files,
                    &models,
                    &mut prefab_xml,
                    &super::parse_model_ref(reference),
                )
                .unwrap_or_else(|| panic!("missing authored housing geometry {id}: {reference}"))
                .parts
                .len()
            })
            .sum::<usize>();
        assert!(parts > 0, "model {id}");
        if id == 894 {
            assert_eq!(
                parts, 2,
                "both authored static guard-tower CGAs must have physics"
            );
        }
    }
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB and AAEMU_GAME_PAK"]
fn real_housing_damaged_prefabs_resolve_authored_geometry_and_missing_state_fallback() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = collision::data::CollisionData::load(&conn).unwrap();
    let app = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = collision::Files::Client(Arc::new(
        crate::data::client_files::ClientFileManager::from_sources(&[pak], &app),
    ));
    let models = collision::models::ModelCache::new(Arc::new(files.clone()));
    // Current usable designs: farmhouse, cottage, large Nuian house, scarecrow and gazebo.
    // Include the origin tower whose absent state4 uses its state3 prefab in the native client.
    for id in [1082, 1116, 1179, 987, 1550, 894] {
        let mut prefab_xml = HashMap::new();
        for state in [1, 3, 4] {
            let references = data.house_prefabs(id, state).unwrap();
            let parts = references
                .iter()
                .map(|reference| {
                    super::resolve_house_shape(
                        &files,
                        &models,
                        &mut prefab_xml,
                        &super::parse_model_ref(reference),
                    )
                    .unwrap_or_else(|| {
                        panic!("missing housing geometry model{id} state{state}: {reference}")
                    })
                    .parts
                    .len()
                })
                .sum::<usize>();
            assert!(parts > 0, "model{id} state{state}");
        }
    }
    assert_eq!(data.house_prefabs(894, 4), data.house_prefabs(894, 3));
    assert_ne!(data.house_prefabs(987, 1), data.house_prefabs(987, 3));
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_guard_tower_root_tracks_match_native_bones_and_physical_ray() {
    let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
        return;
    };
    let app = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = collision::Files::Client(Arc::new(
        crate::data::client_files::ClientFileManager::from_sources(&[pak], &app),
    ));
    let directory = "game/objects/env/03_origin_continent/001_ocom";
    let model_bytes = files
        .read(&format!("{directory}/ocom_guardtower.cga"))
        .unwrap();
    let mut standing = None;
    for (model_name, clip, end_z, moving) in [
        ("ocom_guardtower", "stand", 36.32, true),
        ("ocom_guardtower", "stand75", 25.5, true),
        ("ocom_guardtower", "stand50", 10.0, false),
        ("ocom_guardtower", "down", 0.0, true),
        ("ocom_guardtower_add", "stand", 8.3, false),
        ("ocom_guardtower_add", "down", 0.355_546_9, true),
    ] {
        let model = files
            .read(&format!("{directory}/{model_name}.cga"))
            .unwrap();
        let animation = files
            .read(&format!("{directory}/{model_name}_{clip}.anm"))
            .unwrap();
        let track = collision::cga::RootAnimation::read(&model, &animation).unwrap();
        assert_eq!(track.clip.has_motion(), moving, "{clip} end{end_z}");
        let pose = track
            .sample(track.clip.duration_seconds(), false, 1.0)
            .unwrap();
        assert!((pose.position.z - end_z).abs() < 1e-4, "{clip}: {pose:?}");
        if clip == "stand" && model_name == "ocom_guardtower" {
            standing = Some(track);
        }
    }
    let standing = standing.unwrap();
    let models = collision::models::ModelCache::new(Arc::new(files.clone()));
    let dying = super::resolve_house_shape(
        &files,
        &models,
        &mut HashMap::new(),
        &super::parse_model_ref("prefab://Prefabs/origin_continent.xml/tower.origin_tower_dying"),
    )
    .unwrap();
    assert_eq!(dying.parts.len(), 2);
    for part in &dying.parts {
        let animation = part.animation.as_ref().expect("authored down track");
        assert!(!animation.rigid, "native dying proxies are static");
        assert!(!animation.looped);
        let terminal = animation.track.sample(99.0, false, 1.0).unwrap();
        let z = if part.model.path.ends_with("_add.cga") {
            assert!((part.pos.z - 0.006_599_426_3).abs() < 1e-8);
            0.355_546_9
        } else {
            0.0
        };
        assert!((terminal.position.z - z).abs() < 1e-5);
    }
    // Read-only native client42536 samples046–048. Animation time is normalized;
    // stand's authored range is frames200..500 (ten seconds at30 FPS).
    for (time, root_z, direction) in [
        (
            0.226906732_f64,
            35.805328369_f64,
            [0.978632033_f64, -0.205619574, -0.000000190],
        ),
        (
            0.196078017,
            35.791671753,
            [0.909835637, -0.414968729, -0.000000190],
        ),
        (
            0.212801978,
            35.797882080,
            [0.952851295, -0.303437471, -0.000000190],
        ),
    ] {
        let root = standing.clip.sample(200.0 + 300.0 * time as f32).unwrap();
        assert!(
            (f64::from(root.position.z) - root_z).abs() < 2e-5,
            "{time}: {root:?}"
        );
        let direction = Vec3::new(
            direction[0] as f32,
            direction[1] as f32,
            direction[2] as f32,
        );
        assert!(
            (root.quaternion.rotate(Vec3::Y) - direction).len() < 2e-5,
            "{time}: {root:?}"
        );
        let proxy = standing.sample((time * 10.0) as f32, false, 1.0).unwrap();
        assert!((proxy.quaternion.rotate(Vec3::Y) + direction).len() < 2e-5);
    }
    let model = collision::models::load_model("tower.cga", &model_bytes)
        .unwrap()
        .unwrap();
    let shape = super::ColliderShape {
        parts: vec![super::ShapePart {
            model: Arc::new(model),
            pos: Vec3::ZERO,
            q: Quat::IDENTITY,
            scale: 1.0,
            animation: Some(super::ShapeAnimation {
                track: Arc::new(standing),
                looped: true,
                speed: 1.0,
                rigid: true,
            }),
        }],
    };
    let world = collision::tests::flat_world(100.0);
    let mut state = world.lock();
    let start = Instant::now();
    let key = super::shape_key(
        "native-tower-stand",
        Vec3::new(500.0, 500.0, 169.39969),
        Quat::IDENTITY,
        1.0,
        (0, u32::MAX),
    );
    assert!(state.set_object_shape_at(ObjKind::House, 100, key, Some(&shape), true, start));
    state.update_animated_houses_at(start + Duration::from_secs_f64(2.26906732));
    let hit = state
        .raycast(
            Vec3::new(500.0, 500.0, 229.4),
            Vec3::new(500.0, 500.0, 139.4),
            4,
            query::ray_flags::DEFAULT,
            &[],
        )
        .unwrap();
    assert!(
        (hit.pos.z - 210.498_5).abs() < 1e-4,
        "native sample046: {hit:?}"
    );
    assert_eq!(
        hit.owner,
        Some(Owner::Object {
            kind: ObjKind::House,
            obj_id: 100
        })
    );
    // Native owner42784 request037: genuine active-siege destruction, sampled after
    // both down animations settled. The proxies become static and remain collidable.
    let key = super::shape_key(
        "native-tower-down",
        Vec3::new(500.0, 500.0, 140.90012),
        Quat::IDENTITY,
        1.0,
        (0, u32::MAX),
    );
    assert!(state.set_object_shape_at(ObjKind::House, 100, key, Some(&dying), false, start));
    state.update_animated_houses_at(start + Duration::from_secs(5));
    let from = Vec3::new(500.0, 500.0, 220.90012);
    let to = Vec3::new(500.0, 500.0, 120.90012);
    let hit = state
        .raycast(from, to, 1, query::ray_flags::DEFAULT, &[])
        .unwrap();
    assert!(
        (f64::from(hit.pos.z) - 146.193603516).abs() < 1e-4,
        "{hit:?}"
    );
    assert!(state
        .raycast(from, to, 4, query::ray_flags::DEFAULT, &[])
        .is_none());
    let side = state
        .raycast(
            Vec3::new(490.0, 500.0, 141.90012),
            Vec3::new(510.0, 500.0, 141.90012),
            1,
            query::ray_flags::DEFAULT,
            &[],
        )
        .unwrap();
    // Native absolute coordinates have coarser float rounding than this synthetic
    // origin; compare translated hit distance within1mm, not raw float equality.
    assert!((side.pos.x - 498.80273).abs() < 1e-3, "{side:?}");
    assert!((side.normal.x + 0.813_260_26).abs() < 1e-5, "{side:?}");
    assert!((side.normal.z - 0.581_900_1).abs() < 1e-5, "{side:?}");
    assert!(state
        .raycast(
            Vec3::new(490.0, 500.0, 160.90012),
            Vec3::new(510.0, 500.0, 160.90012),
            query::mask::ALL_TERRAIN,
            query::ray_flags::DEFAULT,
            &[],
        )
        .is_none());
    assert_eq!(
        state.update_animated_houses_at(start + Duration::from_secs(99)),
        0
    );
}
