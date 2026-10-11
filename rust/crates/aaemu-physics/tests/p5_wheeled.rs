//! P5: `CWheeledVehicleEntity` goldens replayed on the port (scenario interpreter
//! [`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit.

use aaemu_physics::math::crt;
use aaemu_physics::oracle::{check_scenario_golden, math_trace, scenario_dir};
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

/// The engine torque curve calls MSVCR100's double `sin`: [`crt::sin`] is a bit-exact port.
#[test]
fn crt_sin_is_bit_exact() {
    let t = math_trace("sin", 1000, 8).unwrap_or_else(|e| panic!("{e}"));
    let bad = t
        .rows
        .iter()
        .filter(|(i, o)| crt::sin(f64::from_bits(i[0])).to_bits() != o[0])
        .count();
    assert_eq!(bad, 0, "{bad}/1000 sin results differ from MSVCR100");
}

golden!(p5_wheeled_settle, "p5_wheeled_settle");
golden!(p5_wheeled_settle_ray, "p5_wheeled_settle_ray");
golden!(p5_wheeled_drive, "p5_wheeled_drive");
golden!(p5_wheeled_brake, "p5_wheeled_brake");
golden!(p5_wheeled_turn, "p5_wheeled_turn");
golden!(p5_wheeled_slope, "p5_wheeled_slope");
golden!(p5_wheeled_gears, "p5_wheeled_gears");
golden!(p5_wheeled_params, "p5_wheeled_params");
golden!(p5_wheeled_tank, "p5_wheeled_tank");
golden!(p5_wheeled_platform, "p5_wheeled_platform");
golden!(p5_wheeled_terrain_slope, "p5_wheeled_terrain_slope");
golden!(p5_wheeled_terrain_bumps, "p5_wheeled_terrain_bumps");
golden!(p5_wheeled_mesh, "p5_wheeled_mesh");
