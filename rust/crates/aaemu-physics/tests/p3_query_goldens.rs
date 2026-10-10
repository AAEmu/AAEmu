//! P3: the RayWorldIntersection goldens replayed through the port's scenario interpreter
//! ([`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit (the whole trace,
//! including the step lines). The full query coverage (skip lists, hit-last, pierceable
//! hits, water, queues, PWI, entity queries, stress worlds) is in the crate's
//! `world::queries::tests`.

use aaemu_physics::oracle::{check_scenario_golden, scenario_dir};
use aaemu_physics::scenario::Scenario;

fn replay(name: &str) -> Result<(), String> {
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn"))).map_err(|e| e.to_string())?;
    let golden = check_scenario_golden(name)?.replace("\r\n", "\n");
    let port = Scenario::run(&scn)?;
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        if a != b {
            return Err(format!("{name}: line {} differs\n port:   {a}\n golden: {b}", i + 1));
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

golden!(ray_box, "ray_box");
golden!(p2a1_rwi_box, "p2a1_rwi_box");
golden!(p2a4_ray_round, "p2a4_ray_round");

/// Needs the P1c heightfield (`geom type=heightfield … worldhf=1`).
#[test]
#[ignore = "needs the P1c heightfield geometry"]
fn ray_heightfield() {
    if let Err(e) = replay("ray_heightfield") {
        panic!("{e}");
    }
}
