//! P1a through the entity API of the real engine (oracle goldens):
//!
//! * `p1a_entity`: `pe_status_contains_point` (status 13) of static entities at the origin is
//!   exactly the port's `PointInsideStatus` (slot 8); `pe_status_dynamics` (status 8) of rigid
//!   entities at the origin gives the mass and the world inverse inertia built from the port's
//!   `CalcPhysicalProperties` (slot 7) with the `RegisterGeometry` floor.
//! * `p1a_float`: each primitive settles in water; the port's `CalculateBuoyancy` (slot 13) at
//!   the final pose balances the weight (`ρ·g_buoy·V = m·g` with the engine's g = 9.81 for
//!   buoyancy and 9.8 for gravity) within the few percent the engine's sleeping leaves. The
//!   golden traces are also reference data for the rigid-body package (P4a).

mod p1a_support;

use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Plane, SpherePrim};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, SphereGeom};
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::{max_idiom, Mat33, Quat, Vec3};
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::trace::Trace;
use std::collections::HashMap;

fn hexf(s: &str) -> f32 {
    f32::from_bits(u32::from_str_radix(s, 16).unwrap())
}

fn kv(t: &[String], key: &str) -> Option<String> {
    t.iter()
        .find_map(|s| s.strip_prefix(&format!("{key}=")).map(str::to_string))
}

fn floats(s: &str) -> Vec<f32> {
    s.split(',').map(|x| x.parse::<f32>().unwrap()).collect()
}

/// The geometries of a scenario, rebuilt from its `geom` lines (decimal values parsed as the
/// oracle does: `strtod` then rounding to float).
fn geoms(scn: &str) -> HashMap<String, Box<dyn Geometry>> {
    let mut m: HashMap<String, Box<dyn Geometry>> = HashMap::new();
    for line in scn.lines() {
        let t: Vec<String> = line.split_whitespace().map(str::to_string).collect();
        if t.first().map(String::as_str) != Some("geom") {
            continue;
        }
        let name = kv(&t, "name").unwrap();
        let ty = kv(&t, "type").unwrap();
        let center = kv(&t, "center")
            .map(|s| {
                let v = floats(&s);
                Vec3::new(v[0], v[1], v[2])
            })
            .unwrap_or(Vec3::ZERO);
        let g: Box<dyn Geometry> = match ty.as_str() {
            "box" => {
                let s = floats(&kv(&t, "size").unwrap());
                let basis = kv(&t, "basis")
                    .map(|b| {
                        let v = floats(&b);
                        Mat33::from_array([v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]])
                    })
                    .unwrap_or(Mat33::IDENTITY);
                Box::new(BoxGeom::new(&BoxPrim {
                    basis,
                    oriented: 1,
                    center,
                    size: Vec3::new(s[0], s[1], s[2]),
                }))
            }
            "sphere" => Box::new(SphereGeom::new(&SpherePrim {
                center,
                r: kv(&t, "r").unwrap().parse().unwrap(),
            })),
            "cylinder" | "capsule" => {
                let axis = kv(&t, "axis")
                    .map(|s| {
                        let v = floats(&s);
                        Vec3::new(v[0], v[1], v[2])
                    })
                    .unwrap_or(Vec3::Z);
                let p = CylinderPrim {
                    center,
                    axis,
                    r: kv(&t, "r").unwrap().parse().unwrap(),
                    hh: kv(&t, "hh").unwrap().parse().unwrap(),
                };
                if ty == "cylinder" {
                    Box::new(CylinderGeom::new(&p))
                } else {
                    Box::new(CapsuleGeom::new(&p))
                }
            }
            other => panic!("{other}"),
        };
        m.insert(name, g);
    }
    m
}

fn scenario_text(name: &str) -> String {
    std::fs::read_to_string(aaemu_physics::oracle::scenario_dir().join(format!("{name}.scn")))
        .unwrap()
}

#[test]
fn contains_point_is_point_inside_status() {
    let scn = scenario_text("p1a_entity");
    let gs = geoms(&scn);
    // entity name -> geometry name
    let mut part = HashMap::new();
    for line in scn.lines() {
        let t: Vec<String> = line.split_whitespace().map(str::to_string).collect();
        if t.first().map(String::as_str) == Some("part") {
            part.insert(kv(&t, "ent").unwrap(), kv(&t, "geom").unwrap());
        }
    }
    let queries: Vec<(String, Vec3)> = scn
        .lines()
        .filter(|l| l.starts_with("status") && l.contains("type=13"))
        .map(|l| {
            let t: Vec<String> = l.split_whitespace().map(str::to_string).collect();
            let v = floats(&kv(&t, "v4").unwrap());
            (kv(&t, "target").unwrap(), Vec3::new(v[0], v[1], v[2]))
        })
        .collect();
    let tr: Trace = scenario_trace("p1a_entity").unwrap();
    let rets: Vec<&Vec<String>> = tr
        .rets
        .iter()
        .filter(|r| r[0] == "status" && r[2] == "13")
        .collect();
    assert_eq!(rets.len(), queries.len());
    let mut inside = 0;
    for ((ent, pt), r) in queries.iter().zip(rets) {
        assert_eq!(&r[1], ent);
        let g = &gs[&part[ent]];
        let want: i32 = r[3].parse().unwrap();
        assert_eq!(g.point_inside_status(*pt), want, "{ent} {pt:?}");
        inside += want;
    }
    assert!(
        inside > 50 && inside < queries.len() as i32 - 50,
        "{inside}"
    );
}

#[test]
fn rigid_mass_and_inverse_inertia() {
    let scn = scenario_text("p1a_entity");
    let gs = geoms(&scn);
    let mut parts = HashMap::new();
    for line in scn.lines() {
        let t: Vec<String> = line.split_whitespace().map(str::to_string).collect();
        if t.first().map(String::as_str) == Some("part") && kv(&t, "ent").unwrap().starts_with('r')
        {
            parts.insert(kv(&t, "ent").unwrap(), t);
        }
    }
    let tr = scenario_trace("p1a_entity").unwrap();
    let mut n = 0;
    for r in tr.rets.iter().filter(|r| r[0] == "status" && r[2] == "8") {
        let t = &parts[&r[1]];
        let g = &gs[&kv(t, "geom").unwrap()];
        let p = g.calc_physical_properties().unwrap();
        // RegisterGeometry inertia floor
        let mx = max_idiom(max_idiom(p.inertia.x, p.inertia.y), p.inertia.z) * 0.01;
        let ib = Vec3::new(
            max_idiom(p.inertia.x, mx),
            max_idiom(p.inertia.y, mx),
            max_idiom(p.inertia.z, mx),
        );
        let w: Vec<f32> = r[4..].iter().map(|s| hexf(s)).collect();
        let com = Vec3::new(w[0], w[1], w[2]);
        let mass = w[13];
        let (want_mass, density) = match kv(t, "mass") {
            Some(m) => {
                let m: f32 = m.parse().unwrap();
                (m, m / p.volume)
            }
            None => {
                let d: f32 = kv(t, "density").unwrap().parse().unwrap();
                (d * p.volume, d)
            }
        };
        let rel = |a: f32, b: f32| ((a - b) / b).abs();
        assert!(
            rel(mass, want_mass) < 1e-6,
            "{} mass {mass} vs {want_mass}",
            r[1]
        );
        assert!((com - p.origin).len() < 1e-6, "{} com {com:?}", r[1]);
        // world inverse inertia R·diag(1/(I·ρ))·Rᵀ, R from the principal-axes quaternion: the
        // rigid entity's exact rounding chain is package P4a's (105 of the 135 words already
        // match bit for bit with this naive formula), so compare with a tolerance
        let rm = Quat::to_mat33(p.q);
        let inv = Vec3::new(
            1.0 / (ib.x * density),
            1.0 / (ib.y * density),
            1.0 / (ib.z * density),
        );
        let want = Mat33::sandwich_diag(&rm, inv);
        let scale = inv.x.max(inv.y).max(inv.z);
        for i in 0..3 {
            for j in 0..3 {
                let got = w[3 + 3 * i + j];
                assert!(
                    (got - want[i][j]).abs() <= scale * 2e-5,
                    "{} Iinv[{i}][{j}] {got} vs {}",
                    r[1],
                    want[i][j]
                );
            }
        }
        n += 1;
    }
    assert_eq!(n, 15);
}

#[test]
fn floating_primitives_balance_their_weight() {
    let scn = scenario_text("p1a_float");
    let gs = geoms(&scn);
    let masses: HashMap<&str, f32> = [
        ("ball", 300.0),
        ("can", 500.0),
        ("pill", 250.0),
        ("crate", 400.0),
    ]
    .into();
    let geom_of: HashMap<&str, &str> = [
        ("ball", "ball"),
        ("can", "can"),
        ("pill", "pill"),
        ("crate", "crate"),
    ]
    .into();
    let tr = scenario_trace("p1a_float").unwrap();
    let water = Plane {
        n: Vec3::Z,
        origin: Vec3::new(32.0, 32.0, 2.0),
    };
    for (name, m) in &masses {
        let (_, st) = *tr.entity_states(name).last().unwrap();
        let gwd = GeomWorldData {
            offset: st.pos_f(),
            r: Quat::to_mat33(st.q_f()),
            ..GeomWorldData::default()
        };
        let g = &gs[geom_of[name]];
        let (v, c) = g.calculate_buoyancy(&water, &gwd);
        let want = m * 9.8 / (1000.0 * 9.81);
        // with resistance 1000 the approach is overdamped and the engine puts the bodies to
        // sleep while they still creep (≈ ±5 % off equilibrium; the PT golden box_float_300kg
        // shows the same): a consistency check of the port inside the real simulation, the
        // exact dynamics are P4a's
        assert!(
            ((v - want) / want).abs() < 0.08,
            "{name}: submerged {v} m³, weight needs {want} m³"
        );
        if *name == "ball" {
            // sphere quirk (0x351AEC00): the "centre" is `c + n·off` with `off` not divided by
            // the volume, so it is not a centroid and lies above the water here
            assert!(c.z > 2.0, "{name}: {c:?}");
        } else {
            assert!(c.z < 2.0 + 1e-4, "{name}: {c:?}");
        }
    }
}
