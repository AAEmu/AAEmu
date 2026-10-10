//! P3 areas: creation, SetParams, lists and CheckAreas (world slot 87) replayed on the port
//! and compared word for word with the oracle (`oracle/p3_areas.c`).

mod p3_support;

fn run(name: &str) {
    let (n, fails) = p3_support::check_scenario(name);
    assert!(
        fails.is_empty(),
        "{name}: {} of {n} lines differ:\n{}",
        fails.len(),
        fails.join("\n")
    );
}

#[test]
fn ellipsoid_and_geometry_areas() {
    run("p3_areas_ellipsoid");
}

#[test]
fn spline_areas() {
    run("p3_areas_spline");
}

#[test]
fn callback_areas() {
    run("p3_areas_callback");
}

#[test]
fn big_areas() {
    run("p3_areas_big");
}

/// Debug helper: print the port's output of a scenario (`P3_DBG=name`).
#[test]
#[ignore]
fn dbg_print() {
    let name = std::env::var("P3_DBG").unwrap();
    let text =
        std::fs::read_to_string(aaemu_physics::oracle::scenario_dir().join(format!("{name}.scn")))
            .unwrap();
    print!("{}", p3_support::Replay::run(&text));
}
