//! P3/P4: the dynamics goldens replayed on the port (scenario interpreter
//! [`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit.

use aaemu_physics::oracle::{check_scenario_golden, scenario_dir};
use aaemu_physics::scenario::Scenario;

fn replay(name: &str) -> Result<(), String> {
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn"))).map_err(|e| e.to_string())?;
    let golden = check_scenario_golden(name)?.replace("
", "
");
    let port = Scenario::run(&scn)?;
    let mut step = String::new();
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        if b.starts_with("step ") {
            step = b.to_string();
        }
        if a != b {
            return Err(format!("{name}: line {} ({step}) differs\n port:   {a}\n golden: {b}", i + 1));
        }
    }
    if port.lines().count() != golden.lines().count() {
        return Err(format!("{name}: {} lines vs golden {}", port.lines().count(), golden.lines().count()));
    }
    Ok(())
}

macro_rules! golden {
    ($t:ident, $n:literal) => {
        #[test]
        fn $t() {
            if let Err(e) = replay($n) {
                panic!("{e}");
            }
        }
    };
}

golden!(box_on_ground, "box_on_ground");
golden!(box_on_box, "box_on_box");
golden!(sphere_drop, "sphere_drop");
golden!(capsule_drop, "capsule_drop");
golden!(cylinder_drop, "cylinder_drop");
golden!(box_float_300kg, "box_float_300kg");
golden!(box_float_700kg, "box_float_700kg");
golden!(box_float_1500kg, "box_float_1500kg");
golden!(p2a1_box_edge_drop, "p2a1_box_edge_drop");
golden!(p2a1_box_stack_rotated, "p2a1_box_stack_rotated");
golden!(p2a1_capsule_on_box, "p2a1_capsule_on_box");
golden!(p2a1_cylinder_on_box, "p2a1_cylinder_on_box");
golden!(p2a1_sphere_on_box, "p2a1_sphere_on_box");
golden!(p7_stack10, "p7_stack10");
golden!(p7_many_bodies, "p7_many_bodies");
golden!(exp_buoyancy_same_step, "exp_buoyancy_same_step");
golden!(p1a_float, "p1a_float");
golden!(p7_ray_part, "p7_ray_part");
golden!(p7_rays_dynamic, "p7_rays_dynamic");
