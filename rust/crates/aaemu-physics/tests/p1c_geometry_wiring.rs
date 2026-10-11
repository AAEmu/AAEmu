//! P1c: the `Geometry` slots of `CTriMesh` reach the ported functions (slot 10 through the
//! trait gives the same contacts as the protocol entry point; the primitives' slot 10 falls back
//! to the generic `Intersect` for a mesh partner; the cached slots equal the computing ones).

use aaemu_physics::collision::contact::IntersectionParams;
use aaemu_physics::geometry::primitives::RayPrim;
use aaemu_physics::geometry::shapes::RayGeom;
use aaemu_physics::geometry::trimesh::build::{MeshDesc, MeshIndices};
use aaemu_physics::geometry::trimesh::geometry_impl::{tables, with_arena};
use aaemu_physics::geometry::trimesh::intersect::{intersect, IntersectExtra};
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::Vec3;

fn cube() -> TriMesh {
    let v: Vec<Vec3> = (0..8)
        .map(|i| {
            Vec3::new(
                if i & 1 != 0 { 1.0 } else { -1.0 },
                if i & 2 != 0 { 1.0 } else { -1.0 },
                if i & 4 != 0 { 1.0 } else { -1.0 },
            )
        })
        .collect();
    let idx: Vec<u16> = vec![
        0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0,
        4, 5, 7, 7, 6, 4,
    ];
    TriMesh::from_data(&MeshDesc {
        vertices: &v,
        indices: MeshIndices::U16(&idx),
        mats: None,
        foreign_idx: None,
        n_tris: 12,
        flags: 0x40,
        min_tris: 2,
        max_tris: 4,
        obb_factor: 1.0,
    })
}

#[test]
fn slot10_through_the_trait() {
    let mut m = cube();
    let ray = RayGeom::new(&RayPrim {
        origin: Vec3::new(0.2, 0.3, 5.0),
        dir: Vec3::new(0.0, 0.0, -10.0),
    });
    let g = GeomWorldData::default();
    for grid in [false, true] {
        if grid {
            assert!(m.prepare_for_ray_test(10.0));
        }
        let mut ip = IntersectionParams::default();
        let mut via_trait = Vec::new();
        let n = Geometry::intersect(&m, &ray, &g, &g, &mut ip, &mut via_trait);
        let mut ip2 = IntersectionParams::default();
        let mut ga = g;
        let direct = with_arena(|a| {
            m.intersect_slot10(
                tables(),
                a,
                &ray,
                Some(&mut ga),
                Some(&g),
                Some(&mut ip2),
                IntersectExtra::default(),
            )
        });
        assert!(n > 0);
        assert_eq!(via_trait, direct.contacts);
        // the ray's slot 10 is the generic Intersect
        let mut ip3 = IntersectionParams::default();
        let mut from_ray = Vec::new();
        let n2 = Geometry::intersect(&ray, &m, &g, &g, &mut ip3, &mut from_ray);
        let mut ip4 = IntersectionParams::default();
        let mut gb = g;
        let generic = with_arena(|a| {
            intersect(
                tables(),
                a,
                &ray,
                &m,
                Some(&mut gb),
                Some(&g),
                Some(&mut ip4),
                IntersectExtra::default(),
            )
        });
        assert_eq!(n2, generic.contacts.len());
        assert_eq!(from_ray, generic.contacts);
    }
}

#[test]
fn cached_slots_match() {
    let mut m = cube();
    let v = Geometry::volume(&m);
    let c = Geometry::center(&m);
    m.warm_caches();
    assert_eq!(Geometry::volume(&m).to_bits(), v.to_bits());
    assert_eq!(Geometry::center(&m), c);
    assert_eq!(v, 8.0);
}
