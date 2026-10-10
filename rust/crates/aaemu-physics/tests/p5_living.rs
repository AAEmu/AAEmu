//! P5: the living-entity goldens replayed on the port (scenario interpreter
//! [`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit.

use aaemu_physics::oracle::{check_scenario_golden, scenario_dir};
use aaemu_physics::scenario::Scenario;

fn replay(name: &str) -> Result<(), String> {
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn")))
        .map_err(|e| e.to_string())?;
    let golden = check_scenario_golden(name)?.replace("\r\n", "\n");
    let port = Scenario::run_in(&scn, Some(&scenario_dir()))?;
    let mut step = String::new();
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        if b.starts_with("step ") {
            step = b.to_string();
        }
        if a != b {
            return Err(format!(
                "{name}: line {} ({step}) differs\n port:   {a}\n golden: {b}",
                i + 1
            ));
        }
    }
    if port.lines().count() != golden.lines().count() {
        return Err(format!(
            "{name}: {} lines vs golden {}",
            port.lines().count(),
            golden.lines().count()
        ));
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

golden!(living_walk, "p5_living_walk");
golden!(living_slope, "p5_living_slope");
golden!(living_stairs, "p5_living_stairs");
golden!(living_jump, "p5_living_jump");
golden!(living_rigid, "p5_living_rigid");
golden!(living_push, "p5_living_push");
golden!(living_swim, "p5_living_swim");
golden!(living_dims, "p5_living_dims");
golden!(living_terrain, "p5_living_terrain");
golden!(living_bumps, "p5_living_bumps");
golden!(living_mesh, "p5_living_mesh");
golden!(living_pair, "p5_living_pair");
