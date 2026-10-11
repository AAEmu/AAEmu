//! Golden oracle traces: parse every scenario trace and check the facts later phases rely on.
//! With `AAEMU_PHYS_ORACLE` set, each golden is first regenerated from the real DLL and must be
//! byte-identical (the engine is deterministic single-threaded).
//!
//! The physics port itself starts in P1+; these tests pin the reference data and its meaning.

use aaemu_physics::ids::{ACTION_SIZES, GEOMPARAMS_SIZES, PARAMS_SIZES};
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::physvars::PhysVars;
use aaemu_physics::trace::{EntityState, Trace};

fn trace(name: &str) -> Trace {
    scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"))
}

fn last(t: &Trace, ent: &str) -> EntityState {
    t.entity_states(ent)
        .last()
        .map(|(_, e)| (*e).clone())
        .unwrap_or_else(|| panic!("no states for {ent}"))
}

fn hexwords(l: &[String]) -> Vec<u32> {
    l.iter()
        .map(|w| u32::from_str_radix(w, 16).unwrap())
        .collect()
}

#[test]
fn engine_tables_match_the_crate_constants() {
    let t = trace("engine_tables");
    let vars = t.other_line("vars").expect("vars line");
    assert_eq!(vars[1], "104");
    assert_eq!(
        hexwords(&vars[2..]),
        PhysVars::default().to_words().to_vec(),
        "physics-vars defaults"
    );
    let dll: Vec<&Vec<String>> = t.other.iter().filter(|l| l[0] == "peekdll").collect();
    assert_eq!(hexwords(&dll[0][2..]), PARAMS_SIZES.to_vec());
    assert_eq!(hexwords(&dll[1][2..]), ACTION_SIZES.to_vec());
    assert_eq!(hexwords(&dll[2][2..]), GEOMPARAMS_SIZES.to_vec());
    let world: Vec<u32> = t
        .other
        .iter()
        .filter(|l| l[0] == "peekworld")
        .map(|l| hexwords(&l[2..])[0])
        .collect();
    use aaemu_physics::physvars::surface_defaults as sd;
    assert_eq!(
        world[..4],
        [
            sd::BOUNCINESS.to_bits(),
            sd::FRICTION.to_bits(),
            sd::DYNAMIC_FRICTION.to_bits(),
            0
        ]
    );
}

#[test]
fn box_on_ground_reproduces_the_original_harness() {
    let t = trace("box_on_ground");
    // expected_box_on_ground.txt: z = 0.480746 (0x3EF62457), x drifts by one ulp, asleep.
    let s90 = t.step(90).unwrap().entities[0].clone();
    assert_eq!(s90.pos, [0x41FF_FFFF, 0x4200_0000, 0x3EF6_2457]);
    assert_eq!(s90.q, [0xB50A_A508, 0xB5A2_A84A, 0xB816_A43E, 0x3F80_0000]);
    assert_eq!(s90.sim_class, 1, "sleeping rigid");
    // free fall before the impact: z(5) = 0x409C211D as in the original harness
    assert_eq!(t.step(5).unwrap().entities[0].pos[2], 0x409C_211D);
}

#[test]
fn floating_boxes() {
    let z = |name: &str| last(&trace(name), "box").pos_f().z;
    // 300 kg floats at 2.214693 (expected_box_float_300kg.txt, bits 0x400DBD88)
    assert_eq!(last(&trace("box_float_300kg"), "box").pos[2], 0x400D_BD88);
    let (z700, z1500) = (z("box_float_700kg"), z("box_float_1500kg"));
    assert!(
        z700 > 1.5 && z700 < 2.0,
        "700 kg settles around 1.8: {z700}"
    );
    assert!(
        z1500 < -18.9 && z1500 > -19.1,
        "1500 kg sinks onto the ground (top -19.5): {z1500}"
    );
}

#[test]
fn stacked_boxes_come_to_rest() {
    let t = trace("box_on_box");
    let (lo, up) = (last(&t, "lower"), last(&t, "upper"));
    assert_eq!(lo.v_f(), Some(Vec3::ZERO));
    assert_eq!(up.v_f(), Some(Vec3::ZERO));
    let dz = up.pos_f().z - lo.pos_f().z;
    assert!((dz - 1.0).abs() < 0.05, "upper rests on lower: dz {dz}");
}

#[test]
fn round_primitives_drop() {
    let ball = last(&trace("sphere_drop"), "ball");
    assert!(
        (ball.pos_f().z - 0.5).abs() < 0.03,
        "sphere rests on its radius: {:?}",
        ball.pos_f()
    );
    assert!(ball.pos_f().x > 33.0, "and keeps rolling along +x");
    let cap = last(&trace("capsule_drop"), "cap");
    assert!(
        (cap.pos_f().z - 0.3).abs() < 0.03,
        "capsule lies on its side: {:?}",
        cap.pos_f()
    );
    let cyl = last(&trace("cylinder_drop"), "cyl");
    assert!(
        (cyl.pos_f().z - 0.5).abs() < 0.03,
        "cylinder stands on its base: {:?}",
        cyl.pos_f()
    );
}

#[test]
fn ray_world_intersection_against_boxes() {
    let t = trace("ray_box");
    let down = t.ray("down").unwrap();
    assert_eq!(down.n_hits, 1);
    let h = &down.hits[0];
    assert_eq!((h.entity.as_str(), f32::from_bits(h.dist)), ("crate", 8.5));
    assert_eq!(Vec3::from_bits(h.n), Vec3::Z);
    assert_eq!(t.ray("miss").unwrap().n_hits, 0);
    assert_eq!(
        t.ray("static_mask_off").unwrap().n_hits,
        0,
        "objtypes without the static bit"
    );
    assert_eq!(
        t.ray("noray_default").unwrap().hits[0].entity,
        "ground",
        "part without 0x8000 is skipped"
    );
    assert_eq!(
        t.ray("noray_flags1").unwrap().hits[0].entity,
        "noray",
        "geomFlagsAll = 1 from the high word"
    );
    assert_eq!(
        t.ray("short").unwrap().n_hits,
        0,
        "segment ends above the crate"
    );
    let side = &t.ray("side").unwrap().hits[0];
    assert_eq!(side.entity, "crate");
}

#[test]
fn ray_world_intersection_against_terrain() {
    let t = trace("ray_heightfield");
    let h = &t.ray("d1").unwrap().hits[0];
    assert_eq!(h.entity, "terrain");
    // h = 1 + 0.05·ix + 0.02·iy, linear inside the cell: 1 + 0.525 + 0.405
    assert!((Vec3::from_bits(h.pt).z - 1.93).abs() < 1e-5);
    assert_eq!(t.ray("terrain_only").unwrap().n_hits, 1);
    assert_eq!(
        t.ray("no_terrain").unwrap().n_hits,
        0,
        "terrain needs objtype 0x100"
    );
    assert_eq!(t.ray("outside").unwrap().n_hits, 0);
    let b = last(&t, "box");
    assert!(
        (b.pos_f().z - 2.9).abs() < 0.1,
        "box rests on the slope: {:?}",
        b.pos_f()
    );
}
