//! Dynamic housing geometry uses the existing CryPhysics model reader and query service.

use std::collections::{BTreeMap, HashMap};
use std::sync::Arc;

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
