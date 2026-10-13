//! Unit tests of the collision world on synthetic data (terrain plane, box models).

use std::sync::Arc;

use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
use aaemu_physics::math::{Mat33, Quat, Vec3};
use aaemu_physics::world::World;

use super::cells::BrushRecord;
use super::models::{ModelPart, ModelPhys};
use super::objects::{collision_flags, ColliderShape, ShapePart};
use super::query::{capsule, mask, ray_flags};
use super::terrain::tests::Plane;
use super::world::{
    brush_default_flags, brush_instance, ObjKind, Owner, PhysWorld, StaticInstance, WorldState,
};

pub(crate) fn box_model(half: Vec3, no_collide_slot: bool) -> Arc<ModelPhys> {
    let w = World::new();
    let g = w
        .register_primitive(&Primitive::Box(BoxPrim {
            size: half,
            ..BoxPrim::default()
        }))
        .unwrap();
    let mut parts = vec![ModelPart {
        slot: 0,
        geom: g.clone(),
    }];
    if no_collide_slot {
        parts.push(ModelPart { slot: 1, geom: g });
    }
    Arc::new(ModelPhys {
        path: "test/box.cgf".into(),
        parts,
        blob_bytes: 0,
    })
}

pub(crate) fn flat_world(z: f32) -> PhysWorld {
    PhysWorld::new(
        1,
        0,
        "test",
        2,
        2,
        50.0,
        Some(Arc::new(Plane {
            a: z,
            b: 0.0,
            c: 0.0,
            n: 1024,
        })),
    )
}

fn down(st: &mut WorldState, x: f32, y: f32, objtypes: u32) -> Option<super::query::Hit> {
    st.raycast(
        Vec3::new(x, y, 200.0),
        Vec3::new(x, y, 0.0),
        objtypes,
        ray_flags::DEFAULT,
        &[],
    )
}

#[test]
fn ray_hits_terrain_heightfield() {
    let pw = flat_world(120.0);
    let mut st = pw.lock();
    let h = down(&mut st, 500.0, 500.0, mask::STATIC_TERRAIN).expect("terrain hit");
    assert!((h.pos.z - 120.0).abs() < 1e-3, "{h:?}");
    assert_eq!(h.owner, Some(Owner::Terrain));
    assert!(h.normal.z > 0.99);
    // one-sided terrain: an upward ray from below misses
    assert!(st
        .raycast(
            Vec3::new(500.0, 500.0, 100.0),
            Vec3::new(500.0, 500.0, 150.0),
            mask::TERRAIN,
            ray_flags::DEFAULT,
            &[]
        )
        .is_none());
}

#[test]
fn ray_hits_ocean_with_water_mask_only() {
    let pw = flat_world(10.0);
    let mut st = pw.lock();
    let from = Vec3::new(300.0, 300.0, 80.0);
    let to = Vec3::new(300.0, 300.0, 0.0);
    let w = st
        .raycast(
            from,
            to,
            mask::STATIC_TERRAIN_WATER,
            ray_flags::DEFAULT,
            &[],
        )
        .expect("water");
    assert!(w.water, "{w:?}");
    assert!((w.pos.z - 50.0).abs() < 1e-3, "{w:?}");
    let t = st
        .raycast(from, to, mask::STATIC_TERRAIN, ray_flags::DEFAULT, &[])
        .expect("terrain");
    assert!(!t.water);
    assert!((t.pos.z - 10.0).abs() < 1e-3);
}

#[test]
fn brush_instances_block_rays_and_unload() {
    let pw = flat_world(100.0);
    let m = box_model(Vec3::new(2.0, 2.0, 2.0), false);
    let rec = BrushRecord {
        bbox: [Vec3::ZERO; 2],
        rnd_flags: 0x2000_0008,
        rot_scale: Mat33::from_array([2.0, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, 2.0]),
        pos: Vec3::new(10.0, 10.0, 104.0),
        material_id: 0,
        statobj: 0,
    };
    let inst = brush_instance(m.clone(), Vec3::new(1024.0, 0.0, 0.0), &rec);
    assert_eq!(inst.scale, 2.0);
    assert_eq!(inst.flags_default, 0x18FFF);
    let content = super::world::CellContent {
        cell: (1, 0),
        instances: vec![inst],
        ..Default::default()
    };
    let mut st = pw.lock();
    assert!(st.insert_cell(content));
    // the box spans z 100..108 (half 2 × scale 2)
    let h = down(&mut st, 1034.0, 10.0, mask::STATIC_TERRAIN).unwrap();
    assert!((h.pos.z - 108.0).abs() < 1e-3, "{h:?}");
    assert_eq!(h.owner, Some(Owner::Brush { cell: (1, 0) }));
    // terrain-only mask ignores the brush
    let t = down(&mut st, 1034.0, 10.0, mask::TERRAIN).unwrap();
    assert!((t.pos.z - 100.0).abs() < 1e-3);
    // a capsule sweep sideways stops at the box (x from 1020 → face at 1030, radius 0.35)
    let sw = st
        .sweep(
            capsule(Vec3::new(1020.0, 10.0, 104.0), 0.35, 0.5),
            Vec3::new(20.0, 0.0, 0.0),
            mask::STATIC_TERRAIN,
            0xFFFF_FFFF,
            &[],
        )
        .expect("sweep hit");
    assert!((sw.dist - (10.0 - 0.35)).abs() < 0.05, "{sw:?}");
    assert!(st.remove_cell((1, 0)));
    st.collect_garbage();
    assert_eq!(
        down(&mut st, 1034.0, 10.0, mask::STATIC_TERRAIN)
            .unwrap()
            .owner,
        Some(Owner::Terrain)
    );
    assert_eq!(st.stats.static_entities, 0);
}

#[test]
fn brush_flags_follow_cbrush_physicalize() {
    let m = box_model(Vec3::splat(1.0), true);
    assert_eq!(brush_default_flags(&m, 0), 0x10FFF);
    assert_eq!(brush_default_flags(&m, 0x40_0000), 0x10FFF | 0xA);
}

#[test]
fn no_collision_doodads_are_ray_only() {
    let pw = flat_world(100.0);
    let m = box_model(Vec3::splat(1.0), false);
    let shape = ColliderShape {
        parts: vec![ShapePart {
            model: m,
            pos: Vec3::ZERO,
            q: Quat::IDENTITY,
            scale: 1.0,
            animation: None,
        }],
    };
    let mut st = pw.lock();
    let key = |flags| {
        super::objects::shape_key(
            "x",
            Vec3::new(50.0, 50.0, 101.0),
            Quat::IDENTITY,
            1.0,
            flags,
        )
    };
    assert!(st.set_object_shape(
        ObjKind::Doodad,
        7,
        key(collision_flags(false, false, true)),
        Some(&shape),
        false
    ));
    let h = down(&mut st, 50.0, 50.0, mask::STATIC_TERRAIN).unwrap();
    assert_eq!(
        h.owner,
        Some(Owner::Object {
            kind: ObjKind::Doodad,
            obj_id: 7
        })
    );
    let sweep = |st: &mut WorldState| {
        st.sweep(
            capsule(Vec3::new(40.0, 50.0, 101.0), 0.3, 0.3),
            Vec3::new(20.0, 0.0, 0.0),
            mask::STATIC_TERRAIN & !mask::TERRAIN,
            0x0F,
            &[],
        )
    };
    // the capsule sweep (collision classes 0x0F) passes through a ray-only doodad
    assert!(sweep(&mut st).is_none());
    // same key: no rebuild; new flags: rebuilt and now collides
    assert!(!st.set_object_shape(
        ObjKind::Doodad,
        7,
        key(collision_flags(false, false, true)),
        Some(&shape),
        false
    ));
    assert!(st.set_object_shape(
        ObjKind::Doodad,
        7,
        key(collision_flags(true, true, false)),
        Some(&shape),
        false
    ));
    assert!(sweep(&mut st).is_some());
    assert!(st.remove_object_collider(ObjKind::Doodad, 7));
    st.collect_garbage();
    assert_eq!(
        down(&mut st, 50.0, 50.0, mask::STATIC_TERRAIN)
            .unwrap()
            .owner,
        Some(Owner::Terrain)
    );
}

#[test]
fn vegetation_instance_has_density_and_flags() {
    let m = box_model(Vec3::splat(1.0), false);
    let v = super::cells::VegetationRecord {
        bbox: [Vec3::ZERO; 2],
        rnd_flags: 0,
        pos: Vec3::new(1.0, 2.0, 3.0),
        scale: 1.5,
        group: 1,
        angle_x: 0,
        angle_y: 0,
        angle_z: 0,
    };
    let i: StaticInstance = super::world::vegetation_instance(m, Vec3::new(1024.0, 0.0, 0.0), &v);
    assert_eq!(i.pos, Vec3::new(1025.0, 2.0, 3.0));
    assert_eq!(i.flags_default, 0x0401_8FFF);
    assert_eq!(i.density, 800.0);
    assert_eq!(i.q, Quat::IDENTITY);
}
