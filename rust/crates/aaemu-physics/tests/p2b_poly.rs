//! P2b `poly`: the polynomial routines (findroots 0x35149170 / 0x35050630, Sturm count
//! 0x3514A2E0, derivative 0x35148830, division 0x351489E0) and the MSVCR100 double math they
//! call, against the DLL through the `PLY*` probe commands of `oracle/p2b_ext_poly.c`.
//!
//! Goldens `tests/golden/p2b_poly_<name>.trace` hold `PLY…` lines and `= <answer>` lines; the
//! check mirrors `probe::check` (oracle → regenerate + compare, `AAEMU_PHYS_BLESS`,
//! `AAEMU_PHYS_P2B_LIVE`, `AAEMU_PHYS_P2B_DUMP`; no oracle → port vs. golden).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::poly::{
    self, count_roots, crt, derivative, divide, find_roots_deg, Poly4,
};
use aaemu_physics::collision::swept::probe::{
    self as probe, oracle_raw, seed_of, word_diff, Case, Rng, DUMP_ENV, LIVE_ENV,
};
use aaemu_physics::geometry::primitives::{Primitive, RayPrim, SpherePrim};
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::{golden_dir, oracle_path, BLESS_ENV};
use std::collections::BTreeMap;
use std::fmt::Write as _;

// ------------------------------------------------------------------------------- harness

fn h64(x: f64) -> String {
    format!("{:016x}", x.to_bits())
}

fn words(it: &mut std::str::SplitWhitespace) -> u64 {
    u64::from_str_radix(it.next().expect("truncated line"), 16).expect("bad hex")
}

fn wf(it: &mut std::str::SplitWhitespace) -> f64 {
    f64::from_bits(words(it))
}

fn read_poly(it: &mut std::str::SplitWhitespace) -> Poly4 {
    let denom = wf(it);
    let mut data = [0.0; 5];
    for c in &mut data {
        *c = wf(it);
    }
    Poly4 { denom, data }
}

fn poly_words(p: &Poly4) -> String {
    let mut s = h64(p.denom);
    for c in p.data {
        s.push(' ');
        s.push_str(&h64(c));
    }
    s
}

/// Evaluate a probe line with the port, formatting the answer like the probe.
fn run_port(line: &str) -> String {
    let mut it = line.split_whitespace();
    let op = it.next().unwrap();
    match op {
        "PLYF" => {
            let n = words(&mut it) as i32;
            let lo = wf(&mut it);
            let hi = wf(&mut it);
            let maxiter = words(&mut it) as i32;
            let deg = words(&mut it) as i32;
            let known = words(&mut it) != 0;
            let p = read_poly(&mut it);
            let mut r = [0.0; 4];
            for x in &mut r {
                *x = wf(&mut it);
            }
            assert!(n == 3 || n == 4);
            let ret = find_roots_deg(&p, lo, hi, &mut r, maxiter, deg, known);
            let mut s = format!("ret {:08x}", ret as u32);
            for x in r {
                let _ = write!(s, " {}", h64(x));
            }
            for _ in 0..4 {
                s.push_str(" 5a5a5a5a5a5a5a5a");
            }
            s
        }
        "PLYC" => {
            let lo = wf(&mut it);
            let hi = wf(&mut it);
            let p = read_poly(&mut it);
            format!("ret {:08x}", count_roots(&p, lo, hi) as u32)
        }
        "PLYD" => {
            let n = words(&mut it) as i32;
            let p = read_poly(&mut it);
            let fill = f64::from_bits(0xA5A5_A5A5_A5A5_A5A5);
            let mut q = Poly4 {
                denom: fill,
                data: [fill; 5],
            };
            derivative(&p, &mut q, n);
            format!("D {}", poly_words(&q))
        }
        "PLYV" => {
            let dn = words(&mut it) as i32;
            let dd = words(&mut it) as i32;
            let a = read_poly(&mut it);
            let b = read_poly(&mut it);
            let fill = f64::from_bits(0xA5A5_A5A5_A5A5_A5A5);
            let mut q = Poly4 {
                denom: fill,
                data: [fill; 5],
            };
            let mut r = q;
            divide(&a, &b, &mut q, &mut r, dn, dd);
            format!(
                "V {} {} {} {}",
                poly_words(&q),
                h64(fill),
                h64(fill),
                poly_words(&r)
            )
        }
        "PLYM" => {
            let name = it.next().unwrap();
            let x = wf(&mut it);
            let v = match name {
                "exp" => crt::exp(x),
                "log" => crt::log(x),
                "sin" => crt::sin(x),
                "cos" => crt::cos(x),
                "atan2" => crt::atan2(x, wf(&mut it)),
                _ => panic!("unknown function {name}"),
            };
            format!("M {}", h64(v))
        }
        _ => panic!("unknown probe line {line}"),
    }
}

fn golden_text(name: &str, lines: &[String], answers: &[String]) -> String {
    let mut s = format!("p2b-golden 1 {name}\n");
    for (c, a) in lines.iter().zip(answers) {
        let _ = writeln!(s, "{c}\n= {a}");
    }
    s
}

fn parse_golden(text: &str) -> Vec<(String, String)> {
    let mut it = text.lines();
    assert!(it.next().is_some_and(|h| h.starts_with("p2b-golden 1")));
    let mut out = Vec::new();
    while let Some(c) = it.next() {
        if c.is_empty() {
            continue;
        }
        let a = it
            .next()
            .and_then(|a| a.strip_prefix("= "))
            .expect("bad golden");
        out.push((c.to_string(), a.to_string()));
    }
    out
}

/// Golden + oracle + live check of a line generator (see the module docs).
fn check(name: &str, n: usize, gen: &dyn Fn(&mut Rng) -> String) {
    let golden = golden_dir().join(format!("{name}.trace"));
    let recs: Vec<(String, String)> = if let Some(exe) = oracle_path() {
        let mut rng = Rng::new(seed_of(name));
        let lines: Vec<String> = (0..n).map(|_| gen(&mut rng)).collect();
        let answers = oracle_raw(&exe, &lines).unwrap_or_else(|e| panic!("{name}: {e}"));
        let fresh = golden_text(name, &lines, &answers);
        let stored = std::fs::read_to_string(&golden)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
        if stored.as_deref() != Some(fresh.as_str()) {
            assert!(
                bless || stored.is_none(),
                "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
                golden.display()
            );
            std::fs::write(&golden, &fresh).unwrap();
        }
        let mut recs: Vec<(String, String)> = lines.into_iter().zip(answers).collect();
        let live: usize = std::env::var(LIVE_ENV)
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(0);
        if live > 0 {
            let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
            let lines: Vec<String> = (0..live).map(|_| gen(&mut rng)).collect();
            for chunk in lines.chunks(50_000) {
                let answers = oracle_raw(&exe, chunk).unwrap_or_else(|e| panic!("{name}: {e}"));
                recs.extend(chunk.iter().cloned().zip(answers));
            }
        }
        recs
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        parse_golden(&text)
    };
    let mut bad = 0usize;
    let mut report = String::new();
    let mut dump = String::new();
    let mut stats: BTreeMap<String, usize> = BTreeMap::new();
    for (i, (line, want)) in recs.iter().enumerate() {
        let key = want
            .split_whitespace()
            .take(2)
            .collect::<Vec<_>>()
            .join(" ");
        let key = if want.starts_with("ret") {
            key
        } else {
            want[..1].to_string()
        };
        *stats.entry(key).or_default() += 1;
        let got = run_port(line);
        if &got != want {
            bad += 1;
            let e = format!(
                "#{i}\n  case:   {line}\n  oracle: {want}\n  port:   {got}\n  diff:   {}\n",
                word_diff(want, &got)
            );
            if bad <= 3 {
                report.push_str(&e);
            }
            dump.push_str(&e);
        }
    }
    eprintln!("{name}: {} cases, answers {stats:?}", recs.len());
    if bad > 0 {
        if let Some(p) = std::env::var_os(DUMP_ENV) {
            let _ = std::fs::write(p, &dump);
        }
    }
    assert!(bad == 0, "{name}: {bad} of {} differ\n{report}", recs.len());
}

// ----------------------------------------------------------------------------- generators

/// A random double over many magnitudes.
fn mag(rng: &mut Rng, lo_exp: i32, hi_exp: i32) -> f64 {
    let e = lo_exp + rng.below((hi_exp - lo_exp) as u32 + 1) as i32;
    let m = 1.0 + f64::from(rng.f(0.0, 1.0)) + (rng.next_u64() >> 40) as f64 * 1e-17;
    let s = if rng.chance(0.5) { -1.0 } else { 1.0 };
    s * m * 2f64.powi(e)
}

/// Coefficients of `k · Π (x − r_i) · Π (x² + b_j x + c_j)` of degree 4 (ascending).
fn from_factors(k: f64, real: &[f64], quads: &[(f64, f64)]) -> [f64; 5] {
    let mut c = vec![k];
    let mul = |c: &Vec<f64>, f: &[f64]| -> Vec<f64> {
        let mut o = vec![0.0; c.len() + f.len() - 1];
        for (i, a) in c.iter().enumerate() {
            for (j, b) in f.iter().enumerate() {
                o[i + j] += a * b;
            }
        }
        o
    };
    for &r in real {
        c = mul(&c, &[-r, 1.0]);
    }
    for &(b, cc) in quads {
        c = mul(&c, &[cc, b, 1.0]);
    }
    let mut out = [0.0; 5];
    out[..c.len()].copy_from_slice(&c);
    out
}

/// A root in or around `[lo, hi]` (sometimes exactly at an end or repeated).
fn root_near(rng: &mut Rng, lo: f64, hi: f64, prev: &[f64]) -> f64 {
    let w = hi - lo;
    match rng.below(10) {
        0 => lo,
        1 => hi,
        2 if !prev.is_empty() => prev[rng.below(prev.len() as u32) as usize],
        3 => lo - w * f64::from(rng.f(0.0, 2.0)),
        4 => hi + w * f64::from(rng.f(0.0, 2.0)),
        5 => f64::from(rng.quant(-2.0, 3.0, 0.125)),
        _ => lo + w * f64::from(rng.f(-0.1, 1.1)),
    }
}

fn interval(rng: &mut Rng) -> (f64, f64) {
    match rng.below(10) {
        0..=6 => (0.0, 1.0),
        7 => {
            let a = f64::from(rng.f(-5.0, 5.0));
            (a, a + f64::from(rng.f(0.0, 5.0)))
        }
        8 => (-1e20, 1e20),
        _ => {
            let a = f64::from(rng.f(-2.0, 2.0));
            let b = f64::from(rng.f(-2.0, 2.0));
            (a, b)
        }
    }
}

/// A random polynomial of nominal degree 4 exercising every branch of findroots.
fn gen_poly(rng: &mut Rng, lo: f64, hi: f64) -> Poly4 {
    let k = if rng.chance(0.3) {
        mag(rng, -20, 20)
    } else {
        f64::from(rng.f(0.5, 2.0)) * if rng.chance(0.5) { -1.0 } else { 1.0 }
    };
    let mut d = match rng.below(12) {
        0 | 1 => {
            // four real roots
            let mut r = Vec::new();
            for _ in 0..4 {
                let x = root_near(rng, lo, hi, &r);
                r.push(x);
            }
            from_factors(k, &r, &[])
        }
        2 | 3 => {
            let mut r = Vec::new();
            for _ in 0..2 {
                let x = root_near(rng, lo, hi, &r);
                r.push(x);
            }
            let b = f64::from(rng.f(-2.0, 2.0));
            let c = b * b * 0.25 + f64::from(rng.f(0.0, 2.0)).powi(3);
            from_factors(k, &r, &[(b, c)])
        }
        4 => {
            let b1 = f64::from(rng.f(-2.0, 2.0));
            let b2 = f64::from(rng.f(-2.0, 2.0));
            from_factors(
                k,
                &[],
                &[
                    (b1, b1 * b1 * 0.25 + f64::from(rng.f(0.0, 1.0))),
                    (b2, b2 * b2 * 0.25 + 1e-6),
                ],
            )
        }
        5 => {
            // lower degree (leading coefficients zero or tiny)
            let deg = rng.below(4) as usize;
            let mut r = Vec::new();
            for _ in 0..deg {
                let x = root_near(rng, lo, hi, &r);
                r.push(x);
            }
            let mut c = from_factors(k, &r, &[]);
            if deg < 4 && rng.chance(0.5) {
                c[deg + 1] = k * mag(rng, -60, -34);
            }
            c
        }
        6 => {
            let mut c = [0.0; 5];
            for x in &mut c {
                *x = mag(rng, -30, 30);
            }
            c
        }
        7 => {
            let mut c = [0.0; 5];
            for x in &mut c {
                *x = f64::from(rng.quant(-4.0, 4.0, 0.5));
            }
            c
        }
        8 => {
            // double / triple / quadruple roots
            let x = root_near(rng, lo, hi, &[]);
            let y = root_near(rng, lo, hi, &[]);
            match rng.below(3) {
                0 => from_factors(k, &[x, x, y, y], &[]),
                1 => from_factors(k, &[x, x, x, y], &[]),
                _ => from_factors(k, &[x, x, x, x], &[]),
            }
        }
        9 => {
            let mut c = [0.0; 5];
            for x in &mut c {
                *x = f64::from(rng.f(-1.0, 1.0));
            }
            c
        }
        10 => {
            // near-degenerate leading coefficient relative to the others
            let mut c = [0.0; 5];
            for x in &mut c {
                *x = f64::from(rng.f(-1.0, 1.0));
            }
            c[4] = c[4] * 1e-10 * f64::from(rng.f(0.5, 2.0));
            if rng.chance(0.3) {
                c[3] *= 1e-10;
            }
            c
        }
        _ => {
            let mut r = Vec::new();
            for _ in 0..4 {
                let x = root_near(rng, lo, hi, &r);
                r.push(x);
            }
            let mut c = from_factors(k, &r, &[]);
            let e = rng.below(5) as usize;
            c[e] += c[e] * 1e-9 * f64::from(rng.f(-1.0, 1.0));
            c
        }
    };
    if rng.chance(0.004) {
        let i = rng.below(5) as usize;
        d[i] = [f64::NAN, f64::INFINITY, f64::NEG_INFINITY, 0.0, -0.0][rng.below(5) as usize];
    }
    let denom = match rng.below(6) {
        0 => mag(rng, -40, 40),
        1 => -1.0,
        _ => 1.0,
    };
    Poly4 { denom, data: d }
}

fn gen_find4(rng: &mut Rng) -> String {
    let (lo, hi) = interval(rng);
    let p = gen_poly(rng, lo, hi);
    let known = rng.chance(0.08);
    let deg = if known { rng.below(5) } else { 4 };
    format!(
        "PLYF 4 {} {} 14 {:x} {:x} {} 1111111111111111 2222222222222222 3333333333333333 4444444444444444",
        h64(lo),
        h64(hi),
        deg,
        u32::from(known),
        poly_words(&p)
    )
}

fn gen_find3(rng: &mut Rng) -> String {
    let (lo, hi) = if rng.chance(0.5) {
        (-1e20, 1e20)
    } else {
        interval(rng)
    };
    let mut p = gen_poly(rng, lo, hi);
    if rng.chance(0.7) {
        // a genuine cubic (the resolvent has a unit leading coefficient)
        let mut r = Vec::new();
        for _ in 0..3 {
            let x = root_near(rng, lo.max(-10.0), hi.min(10.0), &r);
            r.push(x);
        }
        let c = if rng.chance(0.5) {
            from_factors(1.0, &r, &[])
        } else {
            let b = f64::from(rng.f(-3.0, 3.0));
            from_factors(
                1.0,
                &r[..1],
                &[(b, b * b * 0.25 + f64::from(rng.f(0.0, 3.0)))],
            )
        };
        p.data = [c[0], c[1], c[2], c[3], 0.0];
        if rng.chance(0.3) {
            for x in &mut p.data[..3] {
                *x *= mag(rng, -10, 30).abs();
            }
        }
    }
    p.data[4] = f64::from_bits(0x7777_7777_7777_7777);
    let known = rng.chance(0.1);
    let deg = if known { rng.below(4) } else { 3 };
    format!(
        "PLYF 3 {} {} 14 {:x} {:x} {} 1111111111111111 2222222222222222 3333333333333333 4444444444444444",
        h64(lo),
        h64(hi),
        deg,
        u32::from(known),
        poly_words(&p)
    )
}

fn gen_count(rng: &mut Rng) -> String {
    let (lo, hi) = interval(rng);
    let p = gen_poly(rng, lo, hi);
    format!("PLYC {} {} {}", h64(lo), h64(hi), poly_words(&p))
}

fn gen_deriv(rng: &mut Rng) -> String {
    let p = gen_poly(rng, 0.0, 1.0);
    format!("PLYD {:x} {}", rng.below(5), poly_words(&p))
}

fn gen_div(rng: &mut Rng) -> String {
    let a = gen_poly(rng, 0.0, 1.0);
    let b = gen_poly(rng, 0.0, 1.0);
    let dn = rng.below(5);
    let dd = rng.below(dn + 1);
    format!("PLYV {dn:x} {dd:x} {} {}", poly_words(&a), poly_words(&b))
}

fn gen_crt(rng: &mut Rng) -> String {
    let special = |rng: &mut Rng| -> f64 {
        [
            0.0,
            -0.0,
            f64::INFINITY,
            f64::NEG_INFINITY,
            f64::NAN,
            f64::from_bits(0x7FF0_0000_0000_0123),
            f64::MIN_POSITIVE,
            f64::from_bits(5),
            1.0,
            -1.0,
        ][rng.below(10) as usize]
    };
    let any = |rng: &mut Rng| -> f64 {
        if rng.chance(0.03) {
            special(rng)
        } else if rng.chance(0.1) {
            f64::from_bits(rng.next_u64())
        } else {
            mag(rng, -40, 12)
        }
    };
    match rng.below(5) {
        0 => {
            let x = match rng.below(4) {
                0 => f64::from(rng.f(-16.0, 240.0)),
                1 => f64::from(rng.f(-1.0, 1.0)) * 1e-3,
                2 => f64::from(rng.f(-800.0, 800.0)),
                _ => any(rng),
            };
            format!("PLYM exp {}", h64(x))
        }
        1 => {
            let x = match rng.below(4) {
                0 => f64::from(rng.f(0.9, 1.1)),
                1 => mag(rng, -70, 1000).abs(),
                2 => f64::from(rng.f(0.0, 4.0)),
                _ => any(rng),
            };
            format!("PLYM log {}", h64(x))
        }
        2 | 3 => {
            let x = match rng.below(4) {
                0 => f64::from(rng.f(0.0, std::f32::consts::FRAC_PI_3)),
                1 => f64::from(rng.f(-20.0, 20.0)),
                2 => mag(rng, -40, 18),
                _ => any(rng),
            };
            // |x| >= 500000 takes MSVCR100's __remainder_piby2 path, which is not ported (the
            // root finder only evaluates sin/cos on [0, π/3]).
            let x = if x.is_finite() && x.abs() >= 500_000.0 {
                x % 499_999.0
            } else {
                x
            };
            let name = if rng.chance(0.5) { "sin" } else { "cos" };
            format!("PLYM {name} {}", h64(x))
        }
        _ => {
            let (y, x) = match rng.below(5) {
                0 => (mag(rng, -30, 30).abs(), mag(rng, -30, 30)),
                1 => (f64::from(rng.f(0.0, 2.0)), f64::from(rng.f(-2.0, 2.0))),
                2 => (mag(rng, -1074, 1023), mag(rng, -1074, 1023)),
                3 => {
                    let x = mag(rng, -10, 10);
                    (x.abs() * f64::from(rng.f(0.0, 2.0)), x)
                }
                _ => (any(rng), any(rng)),
            };
            format!("PLYM atan2 {} {}", h64(y), h64(x))
        }
    }
}

// --------------------------------------------------------------------------------- tests

#[test]
fn crt_matches_msvcr100() {
    check("p2b_poly_crt", 300, &gen_crt);
}

#[test]
fn findroots4_matches_dll() {
    check("p2b_poly_find4", 300, &gen_find4);
}

#[test]
fn findroots3_matches_dll() {
    check("p2b_poly_find3", 300, &gen_find3);
}

#[test]
fn sturm_count_matches_dll() {
    check("p2b_poly_count", 300, &gen_count);
}

#[test]
fn derivative_matches_dll() {
    check("p2b_poly_deriv", 100, &gen_deriv);
}

#[test]
fn division_matches_dll() {
    check("p2b_poly_div", 200, &gen_div);
}

#[test]
fn hand_checked_roots() {
    // (x − 0.25)(x − 0.5)(x − 0.75)(x − 2) on [0, 1]
    let p = Poly4::new(from_factors(1.0, &[0.25, 0.5, 0.75, 2.0], &[]));
    let mut r = [0.0; 4];
    assert_eq!(count_roots(&p, 0.0, 1.0), 3);
    let n = poly::find_roots(&p, 0.0, 1.0, &mut r, 20);
    assert_eq!(n, 3);
    for (got, want) in r.iter().zip([0.25, 0.5, 0.75]) {
        assert!((got - want).abs() < 1e-9, "{r:?}");
    }
    // x² − 0.25: the quadratic branch
    let q = Poly4::new([-0.25, 0.0, 1.0, 0.0, 0.0]);
    assert_eq!(poly::find_roots(&q, 0.0, 1.0, &mut r, 20), 1);
    assert_eq!(r[0], 0.5);
    // linear: the binary returns c0/c1 (sign bug kept)
    let l = Poly4::new([0.5, 1.0, 0.0, 0.0, 0.0]);
    assert_eq!(poly::find_roots(&l, 0.0, 1.0, &mut r, 20), 1);
    assert_eq!(r[0], 0.5);
}

// ------------------------------------------------------------------ rotational ray–sphere

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn scale(a: Vec3, k: f32) -> Vec3 {
    Vec3::new(a.x * k, a.y * k, a.z * k)
}
fn cross(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x,
    )
}
fn dot(a: Vec3, b: Vec3) -> f32 {
    a.x * b.x + a.y * b.y + a.z * b.z
}

/// `pt` rotated by `phi` about the axis `a` through `c` (Rodrigues, float — only used to place
/// test geometry).
fn rotate(pt: Vec3, c: Vec3, a: Vec3, phi: f32) -> Vec3 {
    let v = Vec3::new(pt.x - c.x, pt.y - c.y, pt.z - c.z);
    let (s, co) = phi.sin_cos();
    let along = scale(a, dot(a, v) * (1.0 - co));
    add(c, add(add(scale(v, co), scale(cross(a, v), s)), along))
}

/// Ray + sphere + rotational params (axis, centre, tmax = sin(max angle), min_pt_dist) placed so
/// that the rotating segment meets the sphere at its ends, at its side, never, or already.
fn rot_ray_sph_setup(rng: &mut Rng) -> (RayPrim, SpherePrim, UnprojParams) {
    let axis = rng.unit_or_axis(0.3);
    let center = if rng.chance(0.2) {
        Vec3::new(0.0, 0.0, 0.0)
    } else {
        rng.vec(-10.0, 10.0)
    };
    let len = if rng.chance(0.05) {
        0.0
    } else {
        rng.f(0.1, 6.0)
    };
    let origin = if rng.chance(0.15) {
        // origin at / near the rotation centre (exercises the min_pt_dist skip)
        add(center, rng.vec(-0.02, 0.02))
    } else {
        add(center, rng.vec(-4.0, 4.0))
    };
    let dir = if rng.chance(0.1) {
        // along the axis or perpendicular to it
        if rng.chance(0.5) {
            scale(axis, len)
        } else {
            let u = cross(axis, rng.unit());
            let l = dot(u, u).sqrt();
            if l > 1e-3 {
                scale(u, len / l)
            } else {
                scale(rng.unit(), len)
            }
        }
    } else {
        scale(rng.unit_or_axis(0.2), len)
    };
    let r = if rng.chance(0.05) {
        rng.f(0.001, 0.05)
    } else {
        rng.f(0.05, 2.5)
    };
    // the point of the segment that should touch: an end or an interior point
    let u = match rng.below(4) {
        0 => 0.0,
        1 => 1.0,
        _ => rng.f(0.0, 1.0),
    };
    let x = add(origin, scale(dir, u));
    let phi = rng.f(-0.3, 1.9);
    let xr = rotate(x, center, axis, phi);
    let sc = match rng.below(6) {
        0 => rng.vec(-10.0, 10.0),
        1 => add(x, rng.vec(-r, r)), // overlapping already
        _ => {
            let off = rng.unit();
            add(xr, scale(off, r * rng.f(0.3, 1.3)))
        }
    };
    let p = UnprojParams {
        dir: axis,
        center,
        tmax: rng.f(0.05, 1.0),
        min_pt_dist: if rng.chance(0.2) {
            rng.f(0.0, 1.0)
        } else {
            rng.f(0.001, 0.05)
        },
        max_cos: rng.pick(&[0.1, 0.9998]),
        ..UnprojParams::default()
    };
    (RayPrim { origin, dir }, SpherePrim { center: sc, r }, p)
}

fn gen_rot_ray_sph(rng: &mut Rng) -> Case {
    let (ray, sph, p) = rot_ray_sph_setup(rng);
    let c = Case::new(1, &Primitive::Ray(ray), &Primitive::Sphere(sph), &p);
    if rng.chance(0.2) {
        c.with_area(rng.pick(&[1, 4, 256]), 0.01)
    } else {
        c
    }
}

fn gen_rot_sph_ray(rng: &mut Rng) -> Case {
    let (ray, sph, p) = rot_ray_sph_setup(rng);
    let c = Case::new(1, &Primitive::Sphere(sph), &Primitive::Ray(ray), &p);
    if rng.chance(0.2) {
        c.with_area(rng.pick(&[1, 4, 256]), 0.01)
    } else {
        c
    }
}

#[test]
fn rot_ray_sph_matches_dll() {
    probe::check("p2b_poly_rot_ray_sph", 300, &gen_rot_ray_sph);
}

#[test]
fn rot_sph_ray_matches_dll() {
    probe::check("p2b_poly_rot_sph_ray", 300, &gen_rot_sph_ray);
}

#[test]
fn rot_ray_sph_hand_checked() {
    // The segment x = 2, y ∈ [−3, 3] rotates about +z through the origin; its line stays at
    // distance 2 from the axis. A sphere of radius 0.5 centred on the segment at (2, 0, 0) is
    // left by the side of the segment when 2 − 2·cos φ = 0.5: cos φ = 0.75, sin φ = √7/4 (the
    // end points, on a circle of radius √13, are far away).
    let mut p = UnprojParams {
        dir: Vec3::new(0.0, 0.0, 1.0),
        min_pt_dist: 0.01,
        ..UnprojParams::default()
    };
    let ray = RayPrim {
        origin: Vec3::new(2.0, -3.0, 0.0),
        dir: Vec3::new(0.0, 6.0, 0.0),
    };
    let sph = SpherePrim {
        center: Vec3::new(2.0, 0.0, 0.0),
        r: 0.5,
    };
    let mut out = aaemu_physics::collision::contact::UnprojResult::default();
    let ret = poly::unproject_rot_ray_sph(&mut p, &ray, -1, &sph, -1, &mut out);
    assert_eq!(ret, 1);
    assert!((out.t - 7f64.sqrt() / 4.0).abs() < 1e-6, "{out:?}");
    assert!((out.cos_t - 0.75).abs() < 1e-6, "{out:?}");
    assert_eq!(out.i_feature, [0x20, 0]);
    // pt: the point of the rotated line closest to the centre, (2 + 0.5·cos φ, 0.5·sin φ);
    // n points from it to the sphere centre
    assert!((out.pt.x - 2.375).abs() < 1e-5, "{out:?}");
    assert!((out.n.x + 0.75).abs() < 1e-5 && (f64::from(out.n.y) + 7f64.sqrt() / 4.0).abs() < 1e-5);
    // a sphere the segment does not touch now is not unprojected
    let far = SpherePrim {
        center: Vec3::new(0.0, 2.5, 0.0),
        r: 0.5,
    };
    assert_eq!(
        poly::unproject_rot_ray_sph(&mut p, &ray, -1, &far, -1, &mut out),
        0
    );
}
