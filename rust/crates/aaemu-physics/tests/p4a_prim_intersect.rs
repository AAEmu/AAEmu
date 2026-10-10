//! P4a: the shared primitive `Intersect` (geometry slot 10, x64 0x35151F20) against the real
//! geometry objects of the DLL (`physoracle --p2b`, `PI` command of
//! `oracle/p4a_probe_intersect.c`), with the per-thread contact scratch compared word for
//! word (contact records, border points, areas, counters, hashes of the pools).
//!
//! * goldens `tests/golden/p4a_pi_<A>.trace` (one per first operand, mixed second operands
//!   incl. the ray), checked without the DLL;
//! * `AAEMU_PHYS_ORACLE` regenerates them (and `AAEMU_PHYS_BLESS=1` rewrites);
//! * `AAEMU_PHYS_P4A_STATS=<n>` (with the oracle) runs `n` fresh cases per ordered pair and
//!   prints the match statistics (fails on any mismatch).

use aaemu_physics::collision::contact::IntersectionParams;
use aaemu_physics::collision::prim_intersect::{intersect, PrimIntersectEnv, ThreadScratch};
use aaemu_physics::collision::swept::probe::{
    check_raw, hex_words, oracle_raw, prim_from_words, prim_word_count, prim_words, seed_of,
    word_diff, Rng,
};
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::{Mat33, Vec3};
use std::fmt::Write;

// ------------------------------------------------------------------------------- encoding

fn fmt_words(w: &[u32]) -> String {
    let mut s = String::new();
    for x in w {
        let _ = write!(s, " {x:08x}");
    }
    s
}

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

fn make_geom(p: &Primitive) -> Box<dyn Geometry> {
    match p {
        Primitive::Box(b) => Box::new(BoxGeom::new(b)),
        Primitive::Sphere(s) => Box::new(SphereGeom::new(s)),
        Primitive::Cylinder(c) => Box::new(CylinderGeom::new(c)),
        Primitive::Capsule(c) => Box::new(CapsuleGeom::new(c)),
        Primitive::Ray(r) => Box::new(RayGeom::new(r)),
        _ => unreachable!(),
    }
}

// ------------------------------------------------------------------------------ generator

fn rand_prim(rng: &mut Rng, t: i32, s: f32) -> Primitive {
    let c = if rng.chance(0.5) {
        Vec3::ZERO
    } else {
        rng.vec(-0.3 * s, 0.3 * s)
    };
    match t {
        0 => {
            let oriented = rng.chance(0.4);
            let mut size = Vec3::new(rng.f(0.1, s), rng.f(0.1, s), rng.f(0.1, s));
            if rng.chance(0.2) {
                size.z = rng.pick(&[0.005f32, 0.01, 0.05]);
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

fn rand_gwd(rng: &mut Rng) -> GeomWorldData {
    GeomWorldData {
        offset: if rng.chance(0.1) {
            Vec3::new(-0.0, 0.0, -0.0)
        } else {
            rng.vec(-20.0, 20.0)
        },
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
        v: if rng.chance(0.3) {
            Vec3::ZERO
        } else {
            rng.vec(-10.0, 10.0)
        },
        w: if rng.chance(0.4) {
            Vec3::ZERO
        } else {
            rng.vec(-3.0, 3.0)
        },
        center_of_mass: rng.vec(-1.0, 1.0),
    }
}

/// One call of a case: A, B, their placements and the params.
fn gen_call(rng: &mut Rng, ta: i32, tb: i32, keep: bool) -> String {
    let sa = rng.f(0.2, 2.0);
    let sb = rng.f(0.2, 2.0);
    let pa = rand_prim(rng, ta, sa);
    let mut ga = rand_gwd(rng);
    let mut ip = IntersectionParams::default();
    let (pb, gb) = if tb == 3 {
        // RWI: the ray in world space, gwd2 = the identity static (0x35209C40)
        let gb = GeomWorldData::default();
        let target = Vec3::new(
            ga.offset.x + rng.f(-1.2, 1.2) * sa,
            ga.offset.y + rng.f(-1.2, 1.2) * sa,
            ga.offset.z + rng.f(-1.2, 1.2) * sa,
        );
        let dir = rng.unit_or_axis(0.3);
        let len = {
            let __x = rng.f(0.1, 20.0);
            rng.pick(&[0.0f32, 0.001, 1.0, 5.0, 50.0, __x])
        };
        let back = {
            let __x = rng.f(-0.2, 1.2);
            rng.pick(&[0.0f32, 0.3, 0.5, 1.0, __x])
        };
        let origin = Vec3::new(
            target.x - dir.x * len * back,
            target.y - dir.y * len * back,
            target.z - dir.z * len * back,
        );
        let ray = RayPrim {
            origin,
            dir: Vec3::new(dir.x * len, dir.y * len, dir.z * len),
        };
        if rng.chance(0.15) {
            ga.offset = Vec3::ZERO;
        }
        (Primitive::Ray(ray), gb)
    } else {
        let pb = rand_prim(rng, tb, sb);
        let mut gb = rand_gwd(rng);
        let dist = {
            let __x = rng.f(0.0, 1.5);
            rng.pick(&[0.0f32, 0.3, 0.7, 1.0, 1.3, __x])
        } * (sa + sb);
        let d = rng.unit_or_axis(0.3);
        gb.offset = Vec3::new(
            ga.offset.x + d.x * dist,
            ga.offset.y + d.y * dist,
            ga.offset.z + d.z * dist,
        );
        (pb, gb)
    };
    // params
    match rng.below(10) {
        0 => {
            ip.i_unprojection_mode = 1;
            ip.center_of_rotation = rng.vec(-20.0, 20.0);
            ip.axis_of_rotation = if rng.chance(0.5) {
                Vec3::ZERO
            } else {
                rng.unit()
            };
        }
        1 => ip.vrel_min = 1e10,
        2 => ip.no_area_contacts = true,
        3 if tb != 3 || rng.chance(0.3) => {
            ip.sweep_test = true;
            if ga.v == Vec3::ZERO {
                ga.v = rng.vec(-10.0, 10.0);
            }
            ip.time_interval = rng.pick(&[1.0f32, 0.1, 1.0 / 30.0, 0.5]);
        }
        4 => ip.no_intersection = true,
        5 => {
            ip.time_interval = rng.pick(&[0.01f32, 0.1, 1.0 / 30.0, 3.0]);
            ip.vrel_min = rng.pick(&[0.0f32, 1e-6, 0.5, 5.0]);
        }
        6 => ip.max_unproj = rng.pick(&[0.0f32, 0.05, 0.5, 2.0]),
        _ => {}
    }
    if rng.chance(0.3) {
        ip.max_surface_gap_angle = rng.pick(&[0.1f32, 0.19, 0.0611, 0.0]);
    }
    ip.keep_prev_contacts = keep;
    format!(
        " C {ta:x}{} {tb:x}{} GA{} GB{} IP{}",
        fmt_words(&prim_words(&pa)),
        fmt_words(&prim_words(&pb)),
        fmt_words(&gwd_words(&ga)),
        fmt_words(&gwd_words(&gb)),
        fmt_words(&ip_words(&ip))
    )
}

fn gen_case(rng: &mut Rng, ta: i32, tbs: &[i32]) -> String {
    let fill = rng.next_u64() as u32;
    let stale = {
        let __x = rng.next_u64() as u32;
        rng.pick(&[0u32, 0x3F80_0000, 0x7FC0_0000, __x])
    };
    let mut st = [0i32; 4];
    let k = if rng.chance(0.7) {
        1
    } else {
        2 + rng.below(4) as i32
    };
    let keep0 = rng.chance(0.3);
    if keep0 {
        st = [
            rng.pick(&[0, 3, 62, 63, 64, 65]),
            rng.pick(&[0, 5, 31, 32, 33, -1]),
            rng.pick(&[0, 40, 250, 255, 256, 257]),
            rng.pick(&[0, 100, 1990, 2010, 2030, 2039, 2040, 2045]),
        ];
    }
    let mut s = format!(
        "PI FILL {fill:08x} STALE {stale:08x} ST {:08x} {:08x} {:08x} {:08x} K {k:x}",
        st[0], st[1], st[2], st[3]
    );
    for i in 0..k {
        let tb = rng.pick(tbs);
        let keep = if i == 0 { keep0 } else { rng.chance(0.85) };
        s.push_str(&gen_call(rng, ta, tb, keep));
    }
    s
}

// ---------------------------------------------------------------------------------- port

fn run_port(line: &str) -> String {
    let env = PrimIntersectEnv::new();
    let mut it = line.split_whitespace();
    assert_eq!(it.next(), Some("PI"));
    assert_eq!(it.next(), Some("FILL"));
    let fill = hex_words(&mut it, 1)[0];
    assert_eq!(it.next(), Some("STALE"));
    let stale = hex_words(&mut it, 1)[0];
    assert_eq!(it.next(), Some("ST"));
    let st = hex_words(&mut it, 4);
    assert_eq!(it.next(), Some("K"));
    let k = hex_words(&mut it, 1)[0];
    let mut s = ThreadScratch::new(0);
    s.fill_with(|i| fill ^ i.wrapping_mul(0x9E37_79B9));
    s.stale_word = stale;
    s.set_n_contacts(st[0] as i32);
    s.set_n_areas(st[1] as i32);
    s.set_n_area_pts(st[2] as i32);
    s.set_n_border(st[3] as i32);
    let mut out = String::from("pi");
    for _ in 0..k {
        assert_eq!(it.next(), Some("C"));
        let ta = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
        let pa = prim_from_words(ta, &hex_words(&mut it, prim_word_count(ta).unwrap()));
        let tb = i32::from_str_radix(it.next().unwrap(), 16).unwrap();
        let pb = prim_from_words(tb, &hex_words(&mut it, prim_word_count(tb).unwrap()));
        assert_eq!(it.next(), Some("GA"));
        let mut ga = gwd_from(&hex_words(&mut it, 22));
        assert_eq!(it.next(), Some("GB"));
        let gb = gwd_from(&hex_words(&mut it, 22));
        assert_eq!(it.next(), Some("IP"));
        let mut ip = ip_from(&hex_words(&mut it, 28));
        if ip.keep_prev_contacts && (s.n_contacts() < 0 || s.n_border() < 0) {
            out.push_str(" | SKIP");
            continue;
        }
        let (a, b) = (make_geom(&pa), make_geom(&pb));
        let r = intersect(&env, &mut s, a.as_ref(), b.as_ref(), &mut ga, &gb, &mut ip);
        let slot = r.first.map_or(u32::MAX, |x| x as u32);
        let _ = write!(
            out,
            " | r {:08x} P {slot:08x} GA{} N {:08x} {:08x} {:08x} {:08x}",
            r.ret as u32,
            fmt_words(&ga.offset.to_bits()),
            s.n_contacts() as u32,
            s.n_areas() as u32,
            s.n_area_pts() as u32,
            s.n_border() as u32
        );
        if let Some(f) = r.first {
            out.push_str(&s.dump_contact(f));
        }
    }
    let _ = write!(
        out,
        " H {:08x} {:08x}",
        s.hash(0xF2C4, 0x10EC8),
        s.hash(0x16EC8, 0x186D0)
    );
    out
}

// --------------------------------------------------------------------------------- tests

const SOLIDS: [i32; 4] = [0, 4, 5, 6];
const ALL_B: [i32; 5] = [0, 4, 5, 6, 3];

fn tname(t: i32) -> &'static str {
    match t {
        0 => "box",
        4 => "sph",
        5 => "cyl",
        6 => "cap",
        3 => "ray",
        _ => "?",
    }
}

fn golden(ta: i32) {
    let name = format!("p4a_pi_{}", tname(ta));
    check_raw(&name, 1250, &|rng| gen_case(rng, ta, &ALL_B), &|c, _| {
        run_port(c)
    });
}

#[test]
fn intersect_box_matches_dll() {
    golden(0);
}
#[test]
fn intersect_sphere_matches_dll() {
    golden(4);
}
#[test]
fn intersect_cylinder_matches_dll() {
    golden(5);
}
#[test]
fn intersect_capsule_matches_dll() {
    golden(6);
}

/// Live statistics per ordered pair (`AAEMU_PHYS_P4A_STATS=<n>` with the oracle).
#[test]
fn intersect_live_stats() {
    let Some(n) = std::env::var("AAEMU_PHYS_P4A_STATS")
        .ok()
        .and_then(|v| v.parse::<usize>().ok())
    else {
        return;
    };
    let Some(exe) = aaemu_physics::oracle::oracle_path() else {
        return;
    };
    let mut report = String::new();
    let mut total_bad = 0;
    let mut dump = String::new();
    for ta in SOLIDS {
        for tb in ALL_B {
            let name = format!("p4a_stats_{}_{}", tname(ta), tname(tb));
            let mut rng = Rng::new(seed_of(&name) ^ 0x1234_5678);
            let lines: Vec<String> = (0..n).map(|_| gen_case(&mut rng, ta, &[tb])).collect();
            let mut answers = Vec::new();
            for chunk in lines.chunks(500) {
                answers.extend(oracle_raw(&exe, chunk).unwrap_or_else(|e| panic!("{name}: {e}")));
            }
            let mut bad = 0;
            let (mut calls, mut hits, mut areas, mut clob) = (0, 0, 0, 0);
            for (c, want) in lines.iter().zip(&answers) {
                calls += want.matches("| r ").count();
                hits += want.matches("| r 0000000").count() - want.matches("| r 00000000").count();
                areas += want.matches(" A ").count();
                clob += want
                    .split(" N ")
                    .skip(1)
                    .filter(|t| {
                        t.split_whitespace()
                            .nth(3)
                            .and_then(|w| u32::from_str_radix(w, 16).ok())
                            .is_some_and(|nb| nb > 0x2000)
                    })
                    .count();
                let got = run_port(c);
                if &got != want {
                    bad += 1;
                    if bad <= 2 {
                        let _ = writeln!(
                            dump,
                            "{name}\n  case:   {c}\n  oracle: {want}\n  port:   {got}\n  diff:   {}",
                            word_diff(want, &got)
                        );
                    }
                }
            }
            total_bad += bad;
            let _ = writeln!(
                report,
                "{:>3}-{:<3} {n:>6} cases {calls:>6} calls {hits:>6} contacts {areas:>5} with area {clob:>4} clobbered border counters  {bad} mismatches",
                tname(ta),
                tname(tb)
            );
        }
    }
    eprintln!("{report}");
    if let Ok(p) = std::env::var("AAEMU_PHYS_P2B_DUMP") {
        let _ = std::fs::write(p, &dump);
    }
    assert_eq!(total_bad, 0, "{report}\n{dump}");
}

/// The `Geometry::intersect` glue of the primitives: thread scratch, copies of the new
/// contacts.
#[test]
fn geometry_trait_glue() {
    let a = BoxGeom::new(&BoxPrim {
        basis: Mat33::IDENTITY,
        oriented: 0,
        center: Vec3::ZERO,
        size: Vec3::new(1.0, 1.0, 1.0),
    });
    let b = SphereGeom::new(&SpherePrim {
        center: Vec3::ZERO,
        r: 0.5,
    });
    let ga = GeomWorldData::default();
    let gb = GeomWorldData {
        offset: Vec3::new(0.0, 0.0, 1.3),
        ..GeomWorldData::default()
    };
    let mut ip = IntersectionParams::default();
    let mut out = Vec::new();
    let n = a.intersect(&b, &ga, &gb, &mut ip, &mut out);
    assert_eq!(n, 1);
    assert_eq!(out.len(), 1);
    let c = &out[0];
    assert!((c.t - 0.2).abs() < 1e-5, "depth {}", c.t);
    assert_eq!(c.border.len() as i32, 1);
    // separated: no contact
    let gb = GeomWorldData {
        offset: Vec3::new(0.0, 0.0, 3.0),
        ..GeomWorldData::default()
    };
    assert_eq!(a.intersect(&b, &ga, &gb, &mut ip, &mut out), 0);
    assert!(out.is_empty());
}
