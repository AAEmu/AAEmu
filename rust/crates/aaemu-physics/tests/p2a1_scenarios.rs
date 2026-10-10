//! P2a-1 scenario goldens: world simulations in which the box pairs collide (box stacks and
//! rotated drops, sphere / cylinder / capsule against boxes in both argument orders, rays against
//! rotated boxes). The traces are recorded from the real DLL now; once the world (P3) and the
//! rigid body / solver (P4) exist, the port's motion must reproduce them bit for bit.
//!
//! Until then these tests pin the reference data (regenerated and byte-compared when
//! `AAEMU_PHYS_ORACLE` is set) and check what the pair functions alone can already explain:
//! every RayWorldIntersection hit on a box is, bit for bit, the contact [`intersect_box_ray`]
//! computes for the world-space box (basis rows from the DLL's quat→matrix).

use aaemu_physics::collision::contact::PrimInters;
use aaemu_physics::collision::pairs::box_pairs::intersect_box_ray;
use aaemu_physics::geometry::primitives::{BoxPrim, RayPrim};
use aaemu_physics::math::{Mat33, Quat, Vec3};
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::trace::Trace;

fn trace(name: &str) -> Trace {
    scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"))
}

/// Every traced state is finite; at the end every body is above `zmin` and, when `sleeps`,
/// asleep (round bodies on the flat ground keep rolling: the engine has no rolling friction).
fn settles(name: &str, ents: &[(&str, f32, bool)]) {
    let t = trace(name);
    assert!(t.steps.len() > 100, "{name}: steps");
    for (ent, zmin, sleeps) in ents {
        let states = t.entity_states(ent);
        assert!(!states.is_empty(), "{name}/{ent}: no states");
        for (step, s) in &states {
            let p = s.pos_f();
            assert!(p.is_finite(), "{name}/{ent}: non-finite at step {step}");
        }
        let last = states.last().unwrap().1;
        if *sleeps {
            assert_eq!(last.awake, 0, "{name}/{ent}: still awake at the end");
        }
        assert!(
            last.pos_f().z > *zmin,
            "{name}/{ent}: fell through (z = {})",
            last.pos_f().z
        );
    }
}

#[test]
fn box_stack_rotated() {
    // pedestal top at z = 1; cube half-size 0.4, slab half-height 0.15
    settles(
        "p2a1_box_stack_rotated",
        &[("a", 0.0, true), ("b", 0.0, true), ("c", 0.0, true)],
    );
}

#[test]
fn box_edge_drop() {
    settles(
        "p2a1_box_edge_drop",
        &[("edge", 0.1, true), ("vertex", 0.2, true)],
    );
}

#[test]
fn sphere_on_box() {
    settles(
        "p2a1_sphere_on_box",
        &[
            ("b1", 0.25, false),
            ("b2", 0.25, false),
            ("crate", 0.4, true),
        ],
    );
}

#[test]
fn cylinder_on_box() {
    settles(
        "p2a1_cylinder_on_box",
        &[
            ("tilted", 0.2, false),
            ("side", 0.2, false),
            ("onpost", 0.2, false),
        ],
    );
}

#[test]
fn capsule_on_box() {
    settles(
        "p2a1_capsule_on_box",
        &[
            ("across", 0.2, false),
            ("tilted", 0.2, false),
            ("oncap", 0.2, true),
        ],
    );
}

/// The world box of a static entity whose geometry is an axis-aligned box at the origin:
/// basis rows = the rotated axes (Mat33::from_quat is the DLL's quat→matrix, bit exact).
fn world_box(pos: Vec3, q: Quat, size: Vec3) -> BoxPrim {
    BoxPrim {
        basis: Mat33::from_quat(q).transpose(),
        oriented: 1,
        center: pos,
        size,
    }
}

#[test]
fn ray_hits_lie_on_the_box_ray_contact() {
    let t = trace("p2a1_rwi_box");
    let crate_box = world_box(
        Vec3::new(32.0, 32.0, 0.75),
        Quat::new(0.1, 0.2, 0.3, 0.927_361_8),
        Vec3::new(1.0, 0.5, 0.75),
    );
    let plate = world_box(
        Vec3::new(36.0, 36.0, 1.0),
        Quat::new(0.382_683_4, 0.0, 0.0, 0.923_879_5),
        Vec3::new(2.0, 2.0, 0.05),
    );
    let rays: &[(&str, [f32; 3], [f32; 3], &BoxPrim)] = &[
        ("down", [32.0, 32.0, 10.0], [0.0, 0.0, -20.0], &crate_box),
        ("diag", [29.0, 29.0, 4.0], [6.0, 6.0, -6.0], &crate_box),
        ("graze", [30.0, 32.5, 0.75], [4.0, 0.0, 0.0], &crate_box),
        ("plate_x", [33.0, 36.0, 1.0], [6.0, 0.0, 0.0], &plate),
        ("plate_dn", [36.5, 36.5, 5.0], [0.0, 0.0, -6.0], &plate),
        ("plate_up", [36.5, 35.5, 0.2], [0.0, 0.3, 3.0], &plate),
        ("edge_on", [36.0, 30.0, 1.0], [0.0, 12.0, 0.0], &plate),
    ];
    for (name, o, dir, bx) in rays {
        let rec = t.ray(name).unwrap_or_else(|| panic!("ray {name}"));
        assert_eq!(rec.n_hits, 1, "{name}");
        let hit = &rec.hits[0];
        let ray = RayPrim {
            origin: Vec3::from_array(*o),
            dir: Vec3::from_array(*dir),
        };
        let mut out = PrimInters::default();
        assert!(intersect_box_ray(bx, &ray, &mut out), "{name}: no contact");
        // bit-exact: the engine's RWI on a box goes through INTERSECT[box][ray] and reports
        // the contact point and normal unchanged
        assert_eq!(out.pt[0].to_bits(), hit.pt, "{name}: point");
        assert_eq!(out.n.to_bits(), hit.n, "{name}: normal");
    }
}
