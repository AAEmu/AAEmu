//! P5: `CParticleEntity` goldens replayed on the port (scenario interpreter
//! [`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit.

use aaemu_physics::oracle::{check_scenario_golden, scenario_dir};
use aaemu_physics::scenario::Scenario;

fn replay(name: &str) -> Result<(), String> {
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn")))
        .map_err(|e| e.to_string())?;
    let golden = check_scenario_golden(name)?.replace("\r\n", "\n");
    let port = Scenario::run(&scn)?;
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

golden!(bounce, "p5_particle_bounce");
golden!(slide, "p5_particle_slide");
golden!(rigid, "p5_particle_rigid");
golden!(carry, "p5_particle_carry");
golden!(water, "p5_particle_water");
golden!(flags, "p5_particle_flags");
golden!(flight, "p5_particle_flight");
golden!(collide, "p5_particle_collide");
golden!(params, "p5_particle_params");
golden!(spawn, "p5_particle_spawn");
