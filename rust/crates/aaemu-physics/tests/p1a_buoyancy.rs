//! P1a: slot 13 CalculateBuoyancy (box through the temporary mesh, sphere, cylinder, capsule)
//! and slot 12 CalcVolumetricPressure, bit-exact against the oracle.

mod p1a_support;

use aaemu_physics::math::Vec3;
use p1a_support::*;

#[test]
fn buoyancy_matches_the_oracle() {
    let cs: Vec<Case> = cases("p1a_buoyancy")
        .into_iter()
        .filter(|c| c.probe == "buoy")
        .collect();
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        let (v, cen) = a.g().calculate_buoyancy(&c.plane(0), &c.gwd(6, false));
        let mut o = vec![hx(v)];
        push_v(&mut o, cen);
        check(c, &o, &mut fails);
    }
    finish("CalculateBuoyancy", cs.len(), &fails);
}

#[test]
fn volumetric_pressure_matches_the_oracle() {
    let cs: Vec<Case> = cases("p1a_buoyancy")
        .into_iter()
        .filter(|c| c.probe == "press")
        .collect();
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        let gwd = c.gwd(0, false);
        let (epi, k, rmin, com) = (c.v(13), c.f(16), c.f(17), c.v(18));
        let mut p = c.v(21);
        let mut l = c.v(24);
        a.g()
            .calc_volumetric_pressure(&gwd, epi, k, rmin, com, &mut p, &mut l);
        let mut o = Vec::new();
        push_v(&mut o, p);
        push_v(&mut o, l);
        check(c, &o, &mut fails);
    }
    finish("CalcVolumetricPressure", cs.len(), &fails);
}

#[test]
fn buoyancy_sanity() {
    use aaemu_physics::geometry::primitives::{Plane, SpherePrim};
    use aaemu_physics::geometry::shapes::SphereGeom;
    use aaemu_physics::geometry::{GeomWorldData, Geometry};
    let s = SphereGeom::new(&SpherePrim {
        center: Vec3::ZERO,
        r: 1.0,
    });
    let deep = Plane {
        n: Vec3::Z,
        origin: Vec3::new(0.0, 0.0, 5.0),
    };
    let (v, _) = s.calculate_buoyancy(&deep, &GeomWorldData::default());
    assert!((v - 4.188_79).abs() < 1e-4);
}

/// Large fuzz runs straight from the oracle (only with `AAEMU_PHYS_ORACLE`; no goldens).
#[test]
fn buoyancy_large_fuzz_with_oracle() {
    let text = "gprobe probe=buoy type=box n=3000 seed=4101\n\
                gprobe probe=buoy type=sphere n=3000 seed=4102\n\
                gprobe probe=buoy type=cylinder n=6000 seed=4103\n\
                gprobe probe=buoy type=capsule n=6000 seed=4104\n\
                gprobe probe=buoy type=cylinder n=2000 seed=4105 mode=1\n\
                gprobe probe=press type=sphere n=2000 seed=4106\n\
                gprobe probe=press type=cylinder n=2000 seed=4107\n";
    let Some(cs) = adhoc_cases(text) else {
        return;
    };
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        let mut o = Vec::new();
        if c.probe == "buoy" {
            let (v, cen) = a.g().calculate_buoyancy(&c.plane(0), &c.gwd(6, false));
            o.push(hx(v));
            push_v(&mut o, cen);
        } else {
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
            push_v(&mut o, p);
            push_v(&mut o, l);
        }
        check(c, &o, &mut fails);
    }
    finish("buoyancy/pressure fuzz", cs.len(), &fails);
}
