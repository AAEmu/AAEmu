//! The collision service on a test server: a synthetic terrain plane at z = 100, a synthetic
//! CGF box model, the doodad collider sync, the server-level queries and the ZWUnitFell gate
//! in the movement path.

use std::collections::HashMap;
use std::sync::Arc;

use aaemu_physics::geometry::primitives::BoxPrim;
use aaemu_physics::geometry::shapes::stream::{save_phys_geometry, PhysGeomHeader};
use aaemu_physics::geometry::shapes::BoxGeom;
use aaemu_physics::math::{Quat, Vec3};

use crate::models::movement::UnitMove;
use crate::test_support::{spawn_player, test_character, test_server, TestClient};
use crate::world::WorldObject;

use super::fall::FALL_VEL_UNITS_PER_MPS;
use super::objects::{apply_doodads, collision_flags, DoodadInfo};
use super::terrain::tests::Plane;
use super::world::{ObjKind, Owner};
use super::Files;

/// A CGF whose default physics is a 2 × 2 × 2 m box centred on the origin.
fn box_cgf() -> Vec<u8> {
    let g = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0)));
    let h = PhysGeomHeader {
        inertia: Vec3::splat(1.0),
        q: Quat::IDENTITY,
        origin: Vec3::ZERO,
        volume: 8.0,
        unk52: 1,
        surface_idx: 0,
    };
    let mut blob = Vec::new();
    save_phys_geometry(&h, &g, &mut blob);
    super::cgf::tests::synthetic_cgf(&blob)
}

fn server(ground: f32) -> crate::GameServer {
    let s = test_server();
    let mut files = HashMap::new();
    files.insert("game/objects/test/box.cgf".to_string(), box_cgf());
    s.systems
        .ai
        .collision
        .test_files
        .set(Arc::new(files))
        .unwrap();
    s.systems
        .ai
        .collision
        .test_terrain
        .set(Arc::new(Plane {
            a: ground,
            b: 0.0,
            c: 0.0,
            n: 1024,
        }))
        .unwrap_or_else(|_| panic!("terrain"));
    s
}

#[tokio::test]
async fn queries_and_doodad_colliders() {
    let s = server(100.0);
    // terrain
    assert_eq!(super::terrain_height(&s, 0, 500.0, 500.0), Some(100.0));
    let g = super::ground_height(&s, 0, 500.0, 500.0, 101.0).unwrap();
    assert!((g - 100.0).abs() < 1e-3);
    assert_eq!(
        super::line_of_sight(&s, 0, [400.0, 500.0, 102.0], [600.0, 500.0, 102.0]),
        Some(true)
    );
    // a doodad with the box model at (500, 500, 101): its top is at 102
    let pw = super::phys_world(&s, 0).unwrap();
    super::load_cell(&s, &pw, (0, 0));
    let files = super::files(&s).unwrap();
    let models = super::model_cache(&s, &files);
    let d = DoodadInfo {
        obj_id: 42,
        model: "cgf://objects/test/box.cgf".into(),
        pos: Vec3::new(500.0, 500.0, 101.0),
        q: Quat::IDENTITY,
        scale: 1.0,
        flags: collision_flags(false, false, false),
    };
    let mut shapes = HashMap::new();
    let (added, removed) =
        apply_doodads(&pw, &files, &models, &mut shapes, std::slice::from_ref(&d));
    assert_eq!((added, removed), (1, 0));
    let g = super::ground_height(&s, 0, 500.0, 500.0, 101.5).unwrap();
    assert!((g - 102.0).abs() < 1e-3, "{g}");
    let h = super::raycast(
        &s,
        0,
        [500.0, 500.0, 150.0],
        [500.0, 500.0, 50.0],
        super::query::mask::STATIC_TERRAIN,
        super::query::ray_flags::DEFAULT,
    )
    .unwrap()
    .unwrap();
    assert_eq!(
        h.owner,
        Some(Owner::Object {
            kind: ObjKind::Doodad,
            obj_id: 42
        })
    );
    assert_eq!(
        super::line_of_sight(&s, 0, [490.0, 500.0, 101.0], [510.0, 500.0, 101.0]),
        Some(false)
    );
    // a blink through the doodad stops in front of it (entTypes 0x107)
    let end = super::displacement::clip_move(&s, 0, [490.0, 500.0, 100.0], [510.0, 500.0, 100.0])
        .unwrap();
    assert!(end[0] < 499.0 && end[0] > 497.0, "{end:?}");
    // unchanged list: nothing rebuilt; empty list: the collider goes away
    assert_eq!(
        apply_doodads(&pw, &files, &models, &mut shapes, &[d]),
        (0, 0)
    );
    assert_eq!(
        apply_doodads(&pw, &files, &models, &mut shapes, &[]),
        (0, 1)
    );
    pw.lock().collect_garbage();
    let g = super::ground_height(&s, 0, 500.0, 500.0, 101.5).unwrap();
    assert!((g - 100.0).abs() < 1e-3);
}

#[tokio::test]
async fn zone_fall_gate_in_the_movement_path() {
    // ground above the synthetic world's ocean level (100): no swimming
    let s = server(300.0);
    let client = TestClient::new(&s, 1, 1);
    let p = spawn_player(
        &s,
        &client,
        test_character(1, "Faller", 500.0, 500.0),
        0x100,
    );
    let world = s.world.world(0).unwrap();
    let obj = WorldObject::from(&p);
    let mv = |z: f32, fall_vel: u16| UnitMove {
        x: 500.0,
        y: 500.0,
        z,
        fall_vel,
        ..UnitMove::default()
    };
    let on = |z, fv| super::movement::on_unit_moved(&s, &world, &obj, 0, &mv(z, fv));
    // a 9 m fall: √360 < 20 m/s → the zone sends nothing, no damage
    assert_eq!(on(300.0, 0), 0);
    assert_eq!(on(309.0, 0), 0);
    assert_eq!(on(304.0, 0), 0);
    assert_eq!(on(300.0, 30000), 0);
    // a 50 m fall reported at 60 m/s: capped at √(40·50)
    assert_eq!(on(350.0, 0), 0);
    let v = on(300.0, 30360);
    assert_eq!(v, (2000f32.sqrt() * FALL_VEL_UNITS_PER_MPS) as u16);
    // without the service: the client value
    let plain = crate::test_support::test_server();
    let w2 = plain.world.world(0).unwrap();
    assert_eq!(
        super::movement::on_unit_moved(&plain, &w2, &obj, 0, &mv(300.0, 30000)),
        30000
    );
    let _ = Files::Map(Arc::new(HashMap::new()));
}
