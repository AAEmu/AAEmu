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
            !std::ptr::eq(
                f as *const (),
                aaemu_physics::collision::swept::unproject_none as *const (),
            )
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
        gb.offset = Vec3::new(
            ga.offset.x,
            ga.offset.y,
            ga.offset.z + ga.v.z * ti * rng.f(0.1, 0.9),
        );
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

// ------------------------------------------------------------------------------------ REG

use aaemu_physics::collision::contact::{GeomContact, PrimInters};
use aaemu_physics::collision::swept::sweep::{
    register_intersection, GeomTestMotion, StaleUnprojSlots,
};

fn motion_words(m: &GeomTestMotion) -> Vec<u32> {
    let mut w = Vec::new();
    for v in [m.v, m.w, m.com, m.rot_center] {
        w.extend_from_slice(&v.to_bits());
    }
    w.push(m.min_vtx_dist.to_bits());
    w
}

fn motion_from(w: &[u32]) -> GeomTestMotion {
    let v = |i: usize| Vec3::from_bits([w[i], w[i + 1], w[i + 2]]);
    GeomTestMotion {
        v: v(0),
        w: v(3),
        com: v(6),
        rot_center: v(9),
        min_vtx_dist: f32::from_bits(w[12]),
    }
}

/// prim_inters as its 22 words (88 bytes; the border pointer words are 0).
fn inters_words(pi: &PrimInters) -> Vec<u32> {
    let mut w = Vec::new();
    w.extend_from_slice(&pi.pt[0].to_bits());
    w.extend_from_slice(&pi.pt[1].to_bits());
    w.extend_from_slice(&pi.n.to_bits());
    let f = pi.i_feature;
    w.push(u32::from_le_bytes([f[0][0], f[0][1], f[1][0], f[1][1]]));
    w.push(pi.tolerance.to_bits());
    w.push(u32::from(pi.id[0] as u16) | (u32::from(pi.id[1] as u16) << 16));
    w.push(pi.i_node[0] as u32);
    w.push(pi.i_node[1] as u32);
    w.extend_from_slice(&[0, 0, 0, 0]);
    w.extend_from_slice(&pi.n_alt.to_bits());
    w.push(pi.unk_84 as u32);
    w
}

fn inters_from(w: &[u32]) -> PrimInters {
    let v = |i: usize| Vec3::from_bits([w[i], w[i + 1], w[i + 2]]);
    let fb = w[9].to_le_bytes();
    PrimInters {
        pt: [v(0), v(3)],
        n: v(6),
        i_feature: [[fb[0], fb[1]], [fb[2], fb[3]]],
        tolerance: f32::from_bits(w[10]),
        id: [w[11] as u16 as i16, (w[11] >> 16) as u16 as i16],
        i_node: [w[12] as i32, w[13] as i32],
        n_alt: v(18),
        unk_84: w[21] as i32,
        ..PrimInters::default()
    }
}

fn is_ported(t: &UnprojTables, mode: usize, a: &Primitive, b: &Primitive) -> bool {
    let f = t.get(mode, a.geom_type(), b.geom_type());
    !std::ptr::eq(
        f as *const (),
        aaemu_physics::collision::swept::unproject_none as *const (),
    )
}

fn rand_world_prim(rng: &mut Rng, t: i32) -> Primitive {
    if t == 3 {
        let o = rng.vec(-1.0, 1.0);
        let d = rng.vec(-2.0, 2.0);
        return Primitive::Ray(aaemu_physics::geometry::primitives::RayPrim { origin: o, dir: d });
    }
    let mut p = rand_prim(rng, t, 1.0);
    if let Primitive::Box(b) = &mut p {
        b.oriented = 1;
        if rng.chance(0.4) {
            b.basis = Mat33::IDENTITY;
        }
    }
    p
}

/// Type pairs whose mode-0 cells exist in this build.
fn reg_pairs() -> Vec<(i32, i32)> {
    let t = UnprojTables::build();
    let types = [0i32, 3, 4, 5, 6];
    let mut v = Vec::new();
    for &a in &types {
        for &b in &types {
            let mut rng = Rng::new(7);
            let pa = rand_world_prim(&mut rng, a);
            let pb = rand_world_prim(&mut rng, b);
            if is_ported(&t, 0, &pa, &pb) {
                v.push((a, b));
            }
        }
    }
    v
}

fn rand_motion(rng: &mut Rng) -> GeomTestMotion {
    let still = rng.chance(0.2);
    GeomTestMotion {
        v: if still {
            Vec3::ZERO
        } else {
            rng.vec(-5.0, 5.0)
        },
        w: if still || rng.chance(0.3) {
            Vec3::ZERO
        } else {
            rng.vec(-3.0, 3.0)
        },
        com: rng.vec(-1.0, 1.0),
        rot_center: rng.vec(-2.0, 2.0),
        min_vtx_dist: rng.f(0.0001, 0.01),
    }
}

fn gen_reg_with(rng: &mut Rng, pairs: &[(i32, i32)]) -> String {
    let (ta, tb) = rng.pick(pairs);
    let pa = rand_world_prim(rng, ta);
    let pb = rand_world_prim(rng, tb);
    let n = if rng.chance(0.5) {
        rng.unit()
    } else {
        rng.vec(-2.0, 2.0)
    };
    let pi = PrimInters {
        pt: [rng.vec(-1.0, 1.0), rng.vec(-1.0, 1.0)],
        n,
        i_feature: [[0x40, 0x41], [0x20, 0x81]],
        tolerance: 0.0,
        id: [rng.below(200) as i16 - 100, -1],
        i_node: [rng.below(10) as i32 - 1, rng.below(10) as i32 - 1],
        ..PrimInters::default()
    };
    let rot = rng.chance(0.4) && is_ported(&UnprojTables::build(), 1, &pa, &pb);
    let ip = IntersectionParams {
        i_unprojection_mode: i32::from(rot),
        time_interval: rng.pick(&[100.0f32, 1.0, 0.1, 0.03]),
        vrel_min: rng.pick(&[1e-6f32, 1e10, 0.5, 2.0]),
        ..IntersectionParams::default()
    };
    format!(
        "REG {ta:x}{} {tb:x}{} SA{} SB{} PI{} IP{} AREA {} STALE {:08x}",
        fmt_words(&prim_words(&pa)),
        fmt_words(&prim_words(&pb)),
        fmt_words(&motion_words(&rand_motion(rng))),
        fmt_words(&motion_words(&rand_motion(rng))),
        fmt_words(&inters_words(&pi)),
        fmt_words(&ip_words(&ip)),
        u32::from(rng.chance(0.5)),
        rng.pick(&[0.0f32, 1e10, 0.01, 2.5]).to_bits()
    )
}

fn port_reg(line: &str, _oracle: &str) -> String {
    let tables = UnprojTables::build();
    let mut it = line.split_whitespace().skip(1);
    let ta = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
    let pa = prim_from_words(ta, &hex_words(&mut it, prim_word_count(ta).unwrap()));
    let tb = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
    let pb = prim_from_words(tb, &hex_words(&mut it, prim_word_count(tb).unwrap()));
    assert_eq!(it.next(), Some("SA"));
    let ma = motion_from(&hex_words(&mut it, 13));
    assert_eq!(it.next(), Some("SB"));
    let mb = motion_from(&hex_words(&mut it, 13));
    assert_eq!(it.next(), Some("PI"));
    let mut pi = inters_from(&hex_words(&mut it, 22));
    assert_eq!(it.next(), Some("IP"));
    let ip = ip_from(&hex_words(&mut it, 28));
    assert_eq!(it.next(), Some("AREA"));
    let use_area = it.next() == Some("1");
    assert_eq!(it.next(), Some("STALE"));
    let stale = f32::from_bits(u32::from_str_radix(it.next().unwrap(), 16).unwrap());
    let s32 = f32::from_bits(0x1111_1111);
    let s3 = Vec3::splat(s32);
    let mut c = GeomContact {
        t: f64::from_bits(0x1111_1111_1111_1111),
        pt: s3,
        n: s3,
        dir: s3,
        i_unproj_mode: 0x1111_1111,
        vel: s32,
        id: [0x1111_1111; 2],
        i_prim: [0x1111_1111; 2],
        i_feature: [0x1111_1111; 2],
        i_node: [0x1111_1111; 2],
        area: use_area.then(|| new_area(256)),
        ..GeomContact::default()
    };
    let ok = register_intersection(
        &tables,
        &pa,
        &ma,
        &pb,
        &mb,
        &mut pi,
        &ip,
        &mut c,
        StaleUnprojSlots {
            tmax: stale,
            min_pt_dist: stale,
        },
    );
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
    w.push(0x1111_1111);
    // one contact registered into a 4-slot buffer never raises the stop flag
    let mut s = format!(
        "reg {:08x} STOP 00000000 PI{} C{} NB 00000001 SELF 1",
        u32::from(ok),
        fmt_words(&pi.n.to_bits()),
        fmt_words(&w)
    );
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
            }
        }
        None => s.push_str(" NOAREA"),
    }
    s
}

#[test]
fn register_intersection_matches_dll() {
    let ps = reg_pairs();
    let name = format!("p2b_sweep_reg_{}pairs", ps.len());
    check_raw(&name, 300, &|rng| gen_reg_with(rng, &ps), &port_reg);
}

// ------------------------------------------------------------------------- thin walls (CCD)

/// Fast bodies (50–2000 m/s over one 1/30 s step or a 1 s sweep) against thin boxes
/// (half-thickness 0.0025–0.02) in the middle of the path: the tunnelling cases the CCD pass and
/// movement sweeps exist for, including the back-up of step 6 when the end pose is past the wall.
fn gen_thin_wall(rng: &mut Rng, movers: &[i32]) -> String {
    let ta = rng.pick(movers);
    let sa = rng.f(0.2, 1.0);
    let pa = rand_prim(rng, ta, sa);
    let ga = GeomWorldData {
        offset: rng.vec(-20.0, 20.0),
        r: if rng.chance(0.5) {
            Mat33::IDENTITY
        } else {
            rng.rotation()
        },
        scale: 1.0,
        v: Vec3::ZERO,
        w: Vec3::ZERO,
        center_of_mass: Vec3::ZERO,
    };
    let dir = rng.unit_or_axis(0.5);
    let speed = rng.f(50.0, 2000.0);
    let ti = rng.pick(&[1.0f32 / 30.0, 1.0 / 60.0, 1.0]);
    let travel = speed * ti;
    let at = rng.f(0.05, 0.95) * travel;
    let mut ga = ga;
    ga.v = dir * speed;
    let wall = Primitive::Box(BoxPrim {
        basis: Mat33::IDENTITY,
        oriented: 0,
        center: Vec3::ZERO,
        size: Vec3::new(
            rng.f(1.0, 5.0),
            rng.f(1.0, 5.0),
            rng.pick(&[0.0025f32, 0.005, 0.01, 0.02]),
        ),
    });
    // wall plane facing the motion (its local z along dir, or tilted)
    let gb = GeomWorldData {
        offset: Vec3::new(
            ga.offset.x + dir.x * at + rng.f(-0.5, 0.5),
            ga.offset.y + dir.y * at + rng.f(-0.5, 0.5),
            ga.offset.z + dir.z * at + rng.f(-0.5, 0.5),
        ),
        r: rng.rotation(),
        scale: 1.0,
        v: Vec3::ZERO,
        w: Vec3::ZERO,
        center_of_mass: Vec3::ZERO,
    };
    let ip = IntersectionParams {
        time_interval: ti,
        max_surface_gap_angle: 0.017_453_3,
        sweep_test: true,
        no_area_contacts: rng.chance(0.3),
        ..IntersectionParams::default()
    };
    format!(
        "SWEEP {ta:x}{} 0{} GA{} GB{} IP{}",
        fmt_words(&prim_words(&pa)),
        fmt_words(&prim_words(&wall)),
        fmt_words(&gwd_words(&ga)),
        fmt_words(&gwd_words(&gb)),
        fmt_words(&ip_words(&ip))
    )
}

#[test]
fn thin_wall_sweeps_match_dll() {
    let movers: Vec<i32> = pairs()
        .into_iter()
        .filter(|&(_, b)| b == 0)
        .map(|(a, _)| a)
        .collect();
    let name = format!("p2b_sweep_thin_wall_{}movers", movers.len());
    check_raw(&name, 200, &|rng| gen_thin_wall(rng, &movers), &port_sweep);
}
