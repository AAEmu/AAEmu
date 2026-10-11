//! P0 math checked bit-for-bit against routines executed inside the real 10.x DLL
//! (`physoracle --math`, goldens `tests/golden/math_*.trace`; regenerated and verified when
//! `AAEMU_PHYS_ORACLE` is set).

use aaemu_physics::math::{crt, Mat33, Quat, Vec3};
use aaemu_physics::oracle::math_trace;
use aaemu_physics::trace::{f, MathTrace};

fn load(name: &str, count: usize, seed: u64) -> MathTrace {
    let t = math_trace(name, count, seed).unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(t.rows.len(), count, "{name}: row count");
    t
}

fn words_f32(w: &[u64]) -> Vec<f32> {
    w.iter().map(|x| f(*x)).collect()
}

#[test]
fn quat_to_matrix_matches_sub_350247b0() {
    for (i, (inp, out)) in load("quat2mat", 500, 1).rows.iter().enumerate() {
        let q = Quat::from_array(words_f32(inp).try_into().unwrap());
        let m = Mat33::from_quat(q).to_bits();
        let want: Vec<u32> = out.iter().map(|w| *w as u32).collect();
        assert_eq!(m.to_vec(), want, "row {i}: q {:?}", q.to_bits());
    }
}

#[test]
fn quat_rotate_matches_sub_35022240() {
    for (i, (inp, out)) in load("quatrot", 500, 2).rows.iter().enumerate() {
        let a = words_f32(inp);
        let q = Quat::new(a[0], a[1], a[2], a[3]);
        let v = Vec3::new(a[4], a[5], a[6]);
        let r = q.rotate(v).to_bits();
        let want: Vec<u32> = out.iter().map(|w| *w as u32).collect();
        assert_eq!(r.to_vec(), want, "row {i}");
    }
}

#[test]
fn matrix_to_quat_matches_sub_35023eb0() {
    for (i, (inp, out)) in load("mat2quat", 500, 3).rows.iter().enumerate() {
        let m = Mat33::from_array(words_f32(inp).try_into().unwrap());
        let q = Quat::from_mat33(&m).to_bits();
        let want: Vec<u32> = out.iter().map(|w| *w as u32).collect();
        assert_eq!(q.to_vec(), want, "row {i}");
    }
}

/// The 212-byte rigid body as far as the probes fill it (words 0..44).
#[derive(Clone, Copy)]
struct Body {
    w: [f32; 44],
}

impl Body {
    fn v3(&self, i: usize) -> Vec3 {
        Vec3::new(self.w[i], self.w[i + 1], self.w[i + 2])
    }
    fn set3(&mut self, i: usize, v: Vec3) {
        self.w[i..i + 3].copy_from_slice(&v.to_array());
    }
    fn q(&self) -> Quat {
        Quat::new(self.w[3], self.w[4], self.w[5], self.w[6])
    }
    fn set_q(&mut self, q: Quat) {
        self.w[3..7].copy_from_slice(&q.to_array());
    }
    fn iinv(&self) -> Mat33 {
        Mat33::from_array(self.w[35..44].try_into().unwrap())
    }

    /// `0x3509CE40` UpdateState.
    fn update_state(&mut self) {
        let r = Mat33::from_quat(self.q());
        let iinv = Mat33::sandwich_diag(&r, self.v3(25));
        self.w[35..44].copy_from_slice(&iinv.to_array());
        let minv = self.w[20];
        if minv > 0.0 {
            self.set3(16, self.v3(7) * minv);
            self.set3(13, iinv.mul_vec(self.v3(10)));
        }
    }

    /// `0x3509E180` Step(dt), written with the P0 math types.
    fn step(&mut self, dt: f32) {
        self.update_state();
        let v = self.v3(16);
        let pos = self.v3(0);
        self.set3(
            0,
            Vec3::new(v.x * dt + pos.x, v.y * dt + pos.y, v.z * dt + pos.z),
        );
        let w = self.v3(13);
        let q = self.q();
        let w2 = w.len2();
        let q = if w2 * (dt * dt) >= 9e-6 {
            let len = w2.sqrt();
            let inv = 1.0 / len;
            let a = (len * dt) * 0.5;
            let (s, c) = (crt::sinf(a), crt::cosf(a));
            let dq = Quat::new(s * (w.x * inv), (inv * w.y) * s, (inv * w.z) * s, c);
            dq * q
        } else {
            let t = w.dot(q.v()) * dt;
            let hdt = dt * 0.5;
            let qw = q.w - t * 0.5;
            Quat::new(
                ((w.y * q.z - w.z * q.y) + w.x * qw) * hdt + q.x,
                ((q.x * w.z - q.z * w.x) + w.y * qw) * hdt + q.y,
                ((q.y * w.x - q.x * w.y) + w.z * qw) * hdt + q.z,
                qw,
            )
        };
        let q = q.normalized_xwyz();
        self.set_q(q);
        if self.w[20] > 0.0 {
            let r = Mat33::from_quat(q);
            let iinv = Mat33::sandwich_diag(&r, self.v3(25));
            self.w[35..44].copy_from_slice(&iinv.to_array());
            self.set3(13, self.iinv().mul_vec(self.v3(10)));
        }
    }
}

fn body_rows(name: &str, count: usize, seed: u64, step: bool) {
    let mut exact_rot = 0;
    for (i, (inp, out)) in load(name, count, seed).rows.iter().enumerate() {
        let a = words_f32(inp);
        let mut b = Body { w: [0.0; 44] };
        b.w[..28].copy_from_slice(&a[..28]);
        let dt = a[28];
        if step {
            let w = {
                let mut t = b;
                t.update_state();
                t.v3(13)
            };
            if w.len2() * (dt * dt) >= 9e-6 {
                exact_rot += 1;
            }
            b.step(dt);
        } else {
            b.update_state();
        }
        let got: Vec<u32> = b.w.iter().map(|x| x.to_bits()).collect();
        let want: Vec<u32> = out.iter().map(|w| *w as u32).collect();
        assert_eq!(got, want, "{name} row {i} (dt {dt})");
    }
    if step {
        assert!(
            exact_rot > count / 4 && exact_rot < count,
            "both rotation branches are exercised ({exact_rot})"
        );
    }
}

#[test]
fn body_update_state_matches_the_dll() {
    body_rows("bodyupdate", 100, 5, false);
}

/// Pins the quaternion product order (`dq ⊗ q`), the f64 quat→matrix, the non-symmetric
/// `R·diag·Rᵀ`, the `((x²+w²)+y²)+z²` renormalisation and MSVCR100 `sinf`/`cosf`.
#[test]
fn body_step_matches_the_dll() {
    body_rows("bodystep", 200, 4, true);
}

fn f32_fn_rows(name: &str, count: usize, seed: u64, func: fn(f32) -> f32) -> usize {
    let t = load(name, count, seed);
    t.rows
        .iter()
        .filter(|(i, o)| func(f(i[0])).to_bits() != o[0] as u32)
        .count()
}

#[test]
fn sinf_cosf_are_bit_exact() {
    assert_eq!(f32_fn_rows("sinf", 4000, 6, crt::sinf), 0);
    assert_eq!(f32_fn_rows("cosf", 4000, 7, crt::cosf), 0);
}

#[test]
fn sqrtf_is_exact() {
    assert_eq!(f32_fn_rows("sqrtf", 1000, 8, crt::sqrtf), 0);
}

/// Functions that are not bit-exact yet: assert the measured mismatch rate does not grow
/// (limits in per mille of 1000 samples; measured on Windows/UCRT in parentheses).
#[test]
fn other_crt_functions_known_divergence() {
    let lim = |name: &str, bad: usize, limit: usize| {
        assert!(bad <= limit, "{name}: {bad}/1000 mismatches > {limit}");
        eprintln!("{name}: {bad}/1000 differ from MSVCR100");
    };
    lim("tanf", f32_fn_rows("tanf", 1000, 8, crt::tanf), 10); // (0)
    lim("atanf", f32_fn_rows("atanf", 1000, 8, crt::atanf), 5); // (0)
    lim("asinf", f32_fn_rows("asinf", 1000, 8, crt::asinf), 100); // (~60)
    lim("acosf", f32_fn_rows("acosf", 1000, 8, crt::acosf), 50); // (~20)
    lim("expf", f32_fn_rows("expf", 1000, 8, crt::expf), 5); // (0)
    lim("logf", f32_fn_rows("logf", 1000, 8, crt::logf), 5); // (0)
    for (name, func) in [
        ("atan2f", crt::atan2f as fn(f32, f32) -> f32),
        ("powf", crt::powf),
    ] {
        let t = load(name, 1000, 8);
        let bad = t
            .rows
            .iter()
            .filter(|(i, o)| {
                // atan2f(y, x): the probe passes (x, y) = (in0, in1) as (arg0, arg1)
                func(f(i[0]), f(i[1])).to_bits() != o[0] as u32
            })
            .count();
        lim(name, bad, 5);
    }
    for (name, func) in [
        ("sin", crt::sin as fn(f64) -> f64),
        ("cos", crt::cos),
        ("exp", crt::exp),
        ("log", crt::log),
        ("asin", crt::asin),
    ] {
        let t = load(name, 1000, 8);
        let bad = t
            .rows
            .iter()
            .filter(|(i, o)| func(f64::from_bits(i[0])).to_bits() != o[0])
            .count();
        lim(name, bad, 30);
    }
    let t = load("atan2", 1000, 8);
    let bad = t
        .rows
        .iter()
        .filter(|(i, o)| crt::atan2(f64::from_bits(i[0]), f64::from_bits(i[1])).to_bits() != o[0])
        .count();
    lim("atan2", bad, 30);
}
