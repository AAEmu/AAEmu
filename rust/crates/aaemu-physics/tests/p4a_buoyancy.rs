//! P4a: ApplyBuoyancy / ApplyAirResistance / the area gathering, replayed on the port and
//! compared word for word with the oracle (`oracle/p3_areas.c`).

mod p3_support;

fn run(name: &str) {
    let (n, fails) = p3_support::check_scenario(name);
    assert!(fails.is_empty(), "{name}: {} of {n} lines differ:\n{}", fails.len(), fails.join("\n"));
}

#[test]
fn buoyancy_basic() {
    run("p4a_buoy_basic");
}

#[test]
fn buoyancy_variants() {
    run("p4a_buoy_variants");
}

/// Multi-part entities: blocked by the rigid AddGeometry body composition (second part's
/// CoM/inertia not merged in the port yet; P4a lead).
#[test]
#[ignore]
fn buoyancy_multipart() {
    run("p4a_buoy_multipart");
}

#[test]
fn air_resistance() {
    run("p4a_air");
}
