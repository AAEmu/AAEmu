//! P1a: `CTriMesh` slot 12 `CalcVolumetricPressure` (0x351CBE20), reached through the box's
//! slot 12 (0x3513B0F0, temporary 12-triangle mesh), bit-exact against the oracle.

mod p1a_support;

use p1a_support::*;

fn replay(c: &Case) -> Vec<String> {
    let a = AnyGeom::of(c);
    let mut p = c.v(21);
    let mut l = c.v(24);
    a.g().calc_volumetric_pressure(
        &c.gwd(0, false),
        c.v(13),
        c.f(16),
        c.f(17),
        c.v(18),
        &mut p,
        &mut l,
    );
    let mut o = Vec::new();
    push_v(&mut o, p);
    push_v(&mut o, l);
    o
}

fn run(what: &str, cs: &[Case]) {
    let mut fails = Vec::new();
    for c in cs.iter().filter(|c| c.probe == "press") {
        check(c, &replay(c), &mut fails);
    }
    finish(what, cs.len(), &fails);
}

#[test]
fn box_mesh_pressure_matches_the_oracle() {
    run("CTriMesh CalcVolumetricPressure", &cases("p1a_press"));
}

/// Large fuzz runs straight from the oracle (only with `AAEMU_PHYS_ORACLE`; no goldens).
#[test]
fn box_mesh_pressure_large_fuzz_with_oracle() {
    let mut text = String::from(
        "gprobe probe=press type=box n=3000 seed=5101\n\
         gprobe probe=press type=box n=1000 seed=5102 mode=1\n",
    );
    for mode in 0..11 {
        text.push_str(&format!(
            "pressprobe mode={mode} n=1500 seed={}\n",
            5200 + mode
        ));
    }
    let Some(cs) = adhoc_cases(&text) else {
        return;
    };
    run("CTriMesh CalcVolumetricPressure fuzz", &cs);
}
