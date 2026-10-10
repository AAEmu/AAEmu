//! P5: `CWheeledVehicleEntity` goldens replayed on the port (scenario interpreter
//! [`aaemu_physics::scenario`]) must reproduce the oracle traces bit for bit.

use aaemu_physics::math::crt;
use aaemu_physics::oracle::{check_scenario_golden, math_trace, scenario_dir};
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

#[test]
fn tmp_sin_big_sample() {
    let Ok(s) = std::fs::read_to_string("D:/aaemu-target/phys-p5-helpers/wheeled-work/m/sin.txt") else { return };
    let mut bad = 0;
    let mut n = 0;
    for l in s.lines().filter(|l| l.starts_with("in ")) {
        let v: Vec<&str> = l.split_whitespace().collect();
        let i = u64::from_str_radix(v[1], 16).unwrap();
        let o = u64::from_str_radix(v[3], 16).unwrap();
        n += 1;
        if crt::sin(f64::from_bits(i)).to_bits() != o { bad += 1; if bad < 5 { eprintln!("{:e} {:x} {:x}", f64::from_bits(i), crt::sin(f64::from_bits(i)).to_bits(), o); } }
    }
    assert_eq!(bad, 0, "{bad}/{n}");
}
