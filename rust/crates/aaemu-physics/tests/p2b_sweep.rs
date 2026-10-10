//! P2b: PreparePrimitive (geometry slot 48) and the sweep path of the primitive Intersect
//! (0x35151F20) against the real geometry objects in the DLL (`physoracle --p2b`, PREP / SWEEP
//! commands of `oracle/p2b_ext_sweep.c`).
//!
//! The bounding box of B (slot 6) and the geometries' `minVtxDist` (+16) are computed by the
//! primitive geometries' `SetData` (another phase); the port takes them from the oracle's answer.

use aaemu_physics::collision::contact::IntersectionParams;
use aaemu_physics::collision::swept::new_area;
use aaemu_physics::collision::swept::probe::{
    check_raw, fmt_words, hex_words, prim_from_words, prim_word_count, prim_words, Rng,
};
use aaemu_physics::collision::swept::sweep::{prepare_primitive, sweep_primitives, SweepGeom};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use aaemu_physics::geometry::GeomWorldData;
use aaemu_physics::math::{Mat33, Vec3};

fn gwd_words(g: &GeomWorldData) -> Vec<u32> {
    let mut w = Vec::with_capacity(22);
    w.extend_from_slice(&g.offset.to_bits());
    w.extend_from_slice(&g.r.to_bits());
    w.push(g.scale.to_bits());
    w.extend_from_slice(&g.v.to_bits());
    w.extend_from_slice(&g.w.to_bits());
    w.extend_from_slice(&g.center_of_mass.to_bits());
    w
}

fn gwd_from(w: &[u32]) -> GeomWorldData {
    let v = |i: usize| Vec3::from_bits([w[i], w[i + 1], w[i + 2]]);
    GeomWorldData {
        offset: v(0),
        r: Mat33::from_bits(w[3..12].try_into().unwrap()),
        scale: f32::from_bits(w[12]),
        v: v(13),
        w: v(16),
        center_of_mass: v(19),
    }
}

fn ip_words(ip: &IntersectionParams) -> Vec<u32> {
    let mut w = vec![0u32; 28];
    w[0] = ip.i_unprojection_mode as u32;
    w[1..4].copy_from_slice(&ip.center_of_rotation.to_bits());
    w[4..7].copy_from_slice(&ip.axis_of_rotation.to_bits());
    w[7] = ip.time_interval.to_bits();
    w[8] = ip.vrel_min.to_bits();
    w[9] = ip.max_surface_gap_angle.to_bits();
    w[10] = ip.min_axis_dist.to_bits();
    w[14..17].copy_from_slice(&ip.unproj_dir.to_bits());
    w[17] = ip.max_unproj.to_bits();
    w[18..21].copy_from_slice(&ip.pt_outside_pivot[0].to_bits());
    w[21..24].copy_from_slice(&ip.pt_outside_pivot[1].to_bits());
    w[24] = u32::from(ip.sweep_test)
        | (u32::from(ip.keep_prev_contacts) << 8)
        | (u32::from(ip.no_area_contacts) << 24);
    w[27] = u32::from(ip.no_intersection);
    w
}

fn ip_from(w: &[u32]) -> IntersectionParams {
    let f = |i: usize| f32::from_bits(w[i]);
    let v = |i: usize| Vec3::from_bits([w[i], w[i + 1], w[i + 2]]);
    IntersectionParams {
        i_unprojection_mode: w[0] as i32,
        center_of_rotation: v(1),
        axis_of_rotation: v(4),
        time_interval: f(7),
        vrel_min: f(8),
        max_surface_gap_angle: f(9),
        min_axis_dist: f(10),
        unproj_dir: v(14),
        max_unproj: f(17),
        pt_outside_pivot: [v(18), v(21)],
        sweep_test: w[24] & 0xFF != 0,
        keep_prev_contacts: (w[24] >> 8) & 0xFF != 0,
        no_area_contacts: (w[24] >> 24) & 0xFF != 0,
        no_intersection: w[27] != 0,
    }
}

fn rand_gwd(rng: &mut Rng) -> GeomWorldData {
    GeomWorldData {
        offset: rng.vec(-50.0, 50.0),
        r: if rng.chance(0.3) {
            Mat33::IDENTITY
        } else {
            rng.rotation()
        },
        scale: if rng.chance(0.6) {
            1.0
        } else {
            rng.f(0.3, 3.0)
        },
        v: rng.vec(-20.0, 20.0),
        w: rng.vec(-3.0, 3.0),
        center_of_mass: rng.vec(-1.0, 1.0),
    }
}

/// A primitive of type `t` in its geometry frame, roughly of size `s` around the origin.
fn rand_prim(rng: &mut Rng, t: i32, s: f32) -> Primitive {
    let c = if rng.chance(0.5) {
        Vec3::ZERO
    } else {
        rng.vec(-0.3 * s, 0.3 * s)
    };
    match t {
        0 => {
            let oriented = rng.chance(0.4);
            let thin = rng.chance(0.25);
            let mut size = Vec3::new(rng.f(0.1, s), rng.f(0.1, s), rng.f(0.1, s));
            if thin {
                size.z = rng.pick(&[0.005f32, 0.01, 0.02, 0.05]);
            }
            Primitive::Box(BoxPrim {
                basis: if oriented {
                    rng.rotation()
                } else {
                    Mat33::IDENTITY
                },
                oriented: i32::from(oriented),
                center: c,
                size,
            })
        }
        4 => Primitive::Sphere(SpherePrim {
            center: c,
            r: rng.f(0.05, s),
        }),
        5 | 6 => {
            let cy = CylinderPrim {
                center: c,
                axis: rng.unit_or_axis(0.6),
                r: rng.f(0.05, 0.6 * s),
                hh: rng.f(0.05, s),
            };
            if t == 5 {
                Primitive::Cylinder(cy)
            } else {
                Primitive::Capsule(cy)
            }
        }
        _ => unreachable!(),
    }
}

// ------------------------------------------------------------------------------------ PREP

fn gen_prep(rng: &mut Rng) -> String {
    let t = rng.pick(&[0, 4, 5, 6]);
    let p = rand_prim(rng, t, 2.0);
    let g = rand_gwd(rng);
    format!(
        "PREP {t:x}{} G{}",
        fmt_words(&prim_words(&p)),
        fmt_words(&gwd_words(&g))
    )
}

fn port_prep(line: &str, oracle: &str) -> String {
    let mut it = line.split_whitespace().skip(1);
    let t = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
    let pw = hex_words(&mut it, prim_word_count(t).unwrap());
    assert_eq!(it.next(), Some("G"));
    let g = gwd_from(&hex_words(&mut it, 22));
    let out = prepare_primitive(&prim_from_words(t, &pw), &g).unwrap();
    let tail = &oracle[oracle.find(" BB").unwrap_or(oracle.len())..];
    format!(
        "prep {:08x}{}{tail}",
        out.geom_type().id(),
        fmt_words(&prim_words(&out))
    )
}

#[test]
fn prepare_primitive_matches_dll() {
    check_raw("p2b_sweep_prep", 300, &gen_prep, &port_prep);
}

// ------------------------------------------------------------------------------------ SWEEP

/// Pairs whose unprojection cells are available in this build (the sweep only uses
/// `UNPROJECT[0][tA][tB]`). Environment `AAEMU_PHYS_P2B_SWEEP_PAIRS=all` forces all 16.
fn pairs() -> Vec<(i32, i32)> {
    let all: Vec<(i32, i32)> = [0, 4, 5, 6]
        .iter()
        .flat_map(|&a| [0, 4, 5, 6].iter().map(move |&b| (a, b)))
        .collect();
    let t = UnprojTables::build();
    let probe_a = |ty: i32| -> Primitive {
        let mut rng = Rng::new(1);
        rand_prim(&mut rng, ty, 1.0)
    };
    all.into_iter()
        .filter(|&(a, b)| {
            let f = t.get(0, probe_a(a).geom_type(), probe_a(b).geom_type());
            f as usize != aaemu_physics::collision::swept::unproject_none as usize
        })
        .collect()
}

fn gen_sweep_with(rng: &mut Rng, pairs: &[(i32, i32)]) -> String {
    let (ta, tb) = rng.pick(pairs);
    let sa = rng.f(0.2, 2.0);
    let sb = rng.f(0.2, 4.0);
    let pa = rand_prim(rng, ta, sa);
    let pb = rand_prim(rng, tb, sb);
    let mut ga = rand_gwd(rng);
    let mut gb = rand_gwd(rng);
    // B somewhere along A's path (or beside it), A moving fast or slow
    let speed = if rng.chance(0.3) {
        rng.f(50.0, 400.0)
    } else {
        rng.f(0.5, 30.0)
    };
    let dir = rng.unit_or_axis(0.3);
    let ti = rng.pick(&[1.0f32, 0.1, 1.0 / 30.0, 0.5]);
    let travel = speed * ti;
    let at = rng.f(-0.2, 1.2) * travel;
    let side = if rng.chance(0.6) {
        Vec3::ZERO
    } else {
        rng.vec(-1.5 * (sa + sb), 1.5 * (sa + sb))
    };
    gb.offset = Vec3::new(
        ga.offset.x + dir.x * at + side.x,
        ga.offset.y + dir.y * at + side.y,
        ga.offset.z + dir.z * at + side.z,
    );
    ga.v = Vec3::new(dir.x * speed, dir.y * speed, dir.z * speed);
    if rng.chance(0.2) {
        // thin wall facing the motion
        gb.r = Mat33::IDENTITY;
        ga.v = Vec3::new(0.0, 0.0, if rng.chance(0.5) { speed } else { -speed });
        gb.offset = Vec3::new(ga.offset.x, ga.offset.y, ga.offset.z + ga.v.z * ti * rng.f(0.1, 0.9));
    }
    let ip = IntersectionParams {
        time_interval: ti,
        max_surface_gap_angle: rng.pick(&[0.017_453_3f32, 0.1, 0.19, 0.0611, 0.0]),
        sweep_test: true,
        no_area_contacts: rng.chance(0.2),
        ..IntersectionParams::default()
    };
    format!(
        "SWEEP {ta:x}{} {tb:x}{} GA{} GB{} IP{}",
        fmt_words(&prim_words(&pa)),
        fmt_words(&prim_words(&pb)),
        fmt_words(&gwd_words(&ga)),
        fmt_words(&gwd_words(&gb)),
        fmt_words(&ip_words(&ip))
    )
}

fn port_sweep(line: &str, oracle: &str) -> String {
    let tables = UnprojTables::build();
    let mut it = line.split_whitespace().skip(1);
    let ta = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
    let pa = prim_from_words(ta, &hex_words(&mut it, prim_word_count(ta).unwrap()));
    let tb = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
    let pb = prim_from_words(tb, &hex_words(&mut it, prim_word_count(tb).unwrap()));
    assert_eq!(it.next(), Some("GA"));
    let mut ga = gwd_from(&hex_words(&mut it, 22));
    assert_eq!(it.next(), Some("GB"));
    let gb = gwd_from(&hex_words(&mut it, 22));
    assert_eq!(it.next(), Some("IP"));
    let ip = ip_from(&hex_words(&mut it, 28));
    // engine data from the answer: B's bbox and the minVtxDists
    let mut o = oracle.split_whitespace();
    // the OVERLAP pre-check (BV / pair phases) as the engine evaluated it
    while o.next() != Some("OV") {}
    let overlap = o.next() == Some("1");
    while o.next() != Some("BB") {}
    let bbw = hex_words(&mut o, 16);
    let bbox = match prim_from_words(0, &bbw) {
        Primitive::Box(b) => b,
        _ => unreachable!(),
    };
    assert_eq!(o.next(), Some("MVD"));
    let mvd = hex_words(&mut o, 2);
    let mut area = (!ip.no_area_contacts).then(|| new_area(256));
    let hit = if !overlap {
        None
    } else {
        sweep_primitives(
        &tables,
        SweepGeom {
            prim: &pa,
            min_vtx_dist: f32::from_bits(mvd[0]),
        },
        &mut ga,
        SweepGeom {
            prim: &pb,
            min_vtx_dist: f32::from_bits(mvd[1]),
        },
        &gb,
        &bbox,
        &ip,
        area.as_mut(),
        )
    };
    let mut s = format!(
        "sweep {:08x} OV {} GA{} BB{} MVD{}",
        u32::from(hit.is_some()),
        u32::from(overlap),
        fmt_words(&gwd_words(&ga)),
        fmt_words(&bbw),
        fmt_words(&mvd)
    );
    if let Some(h) = hit {
        let c = &h.contact;
        let t = c.t.to_bits();
        let mut w = vec![t as u32, (t >> 32) as u32];
        w.extend_from_slice(&c.pt.to_bits());
        w.extend_from_slice(&c.n.to_bits());
        w.extend_from_slice(&c.dir.to_bits());
        w.push(c.i_unproj_mode as u32);
        w.push(c.vel.to_bits());
        w.extend(c.id.iter().map(|&x| x as u32));
        w.extend(c.i_prim.iter().map(|&x| x as u32));
        w.extend(c.i_feature.iter().map(|&x| x as u32));
        w.extend(c.i_node.iter().map(|&x| x as u32));
        w.push(0);
        s.push_str(&format!(" C{}", fmt_words(&w)));
        s.push_str(&format!(" B {:08x}", c.border.len() as u32));
        for p in &c.border {
            s.push_str(&fmt_words(&p.to_bits()));
        }
        s.push_str(&format!(
            " CEN{} F {:02x}",
            fmt_words(&c.center.to_bits()),
            u8::from(c.border_consecutive)
        ));
        match &c.area {
            Some(a) => {
                s.push_str(&format!(
                    " AREA {:08x} {:08x} {:08x}{}",
                    a.npt as u32,
                    a.nmaxpt as u32,
                    a.min_edge.to_bits(),
                    fmt_words(&a.n.to_bits())
                ));
                for i in 0..a.npt.max(0) as usize {
                    s.push_str(&fmt_words(&a.pt[i].to_bits()));
                    s.push_str(&fmt_words(&[
                        a.prim[0][i] as u32,
                        a.prim[1][i] as u32,
                        a.feature[0][i] as u32,
                        a.feature[1][i] as u32,
                    ]));
                }
            }
            None => s.push_str(" NOAREA"),
        }
    }
    s
}

#[test]
fn sweep_matches_dll() {
    let ps = if std::env::var("AAEMU_PHYS_P2B_SWEEP_PAIRS").as_deref() == Ok("all") {
        [0, 4, 5, 6]
            .iter()
            .flat_map(|&a| [0, 4, 5, 6].iter().map(move |&b| (a, b)))
            .collect()
    } else {
        pairs()
    };
    let name = if ps.len() == 16 {
        "p2b_sweep_all".to_string()
    } else {
        format!("p2b_sweep_{}pairs", ps.len())
    };
    check_raw(&name, 300, &|rng| gen_sweep_with(rng, &ps), &port_sweep);
}
