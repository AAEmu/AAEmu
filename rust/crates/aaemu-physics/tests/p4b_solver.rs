//! P4b: `InvokeContactSolver` 0x3509E850 (with `InitSolver`, `RegisterContact`, `DisableCG`)
//! against the DLL on generated islands (`physoracle --p4b`, `src/solver/probe.rs`).

use aaemu_physics::math::{Mat33, Quat, Vec3};
use aaemu_physics::solver::contact::flags as cf;
use aaemu_physics::solver::probe::{check, check_live, Case, Rng};
use aaemu_physics::solver::{BodyId, Contact, ContactId, ContactOwner, MultiBodyEntry, RigidBody};

// ------------------------------------------------------------------------------------ builders

fn rot(q: Quat, v: Vec3) -> Vec3 {
    q.rotate(v)
}

/// A box body (half sizes `h`, mass `m`) at `pos` with orientation `q`, linear velocity `v`
/// and angular momentum `l`.
fn box_body(pos: Vec3, q: Quat, h: Vec3, m: f32, v: Vec3, l: Vec3) -> RigidBody {
    let mut b = RigidBody {
        pos,
        q,
        m,
        minv: if m > 0.0 { 1.0 / m } else { 0.0 },
        volume: 8.0 * h.x * h.y * h.z,
        ..RigidBody::default()
    };
    if m > 0.0 {
        let k = m / 3.0;
        b.ibody = Vec3::new(
            k * (h.y * h.y + h.z * h.z),
            k * (h.x * h.x + h.z * h.z),
            k * (h.x * h.x + h.y * h.y),
        );
        b.ibody_inv = Vec3::new(1.0 / b.ibody.x, 1.0 / b.ibody.y, 1.0 / b.ibody.z);
    }
    b.p = Vec3::new(v.x * m, v.y * m, v.z * m);
    b.l = l;
    b.update_state();
    if m <= 0.0 {
        b.v = Vec3::ZERO;
        b.w = Vec3::ZERO;
    }
    b
}

/// The static dummy as `GetRigidBody` leaves it (constructor state).
fn dummy() -> RigidBody {
    RigidBody::default()
}

struct Island {
    bodies: Vec<RigidBody>,
    contacts: Vec<Contact>,
    multi: Vec<MultiBodyEntry>,
}

impl Island {
    fn new() -> Self {
        Self {
            bodies: Vec::new(),
            contacts: Vec::new(),
            multi: Vec::new(),
        }
    }
    fn add(&mut self, b: RigidBody) -> BodyId {
        self.bodies.push(b);
        BodyId(self.bodies.len() as u32)
    }
    fn body(&self, id: BodyId) -> RigidBody {
        if id == BodyId::STATIC {
            dummy()
        } else {
            self.bodies[id.0 as usize - 1]
        }
    }
    /// A contact as RegisterContactPoint leaves it: both points at `pt`, normal `n` (from
    /// `b1` towards `b0`), `vn`, `vreq = n·min(2.5, (depth − 0.01)·10)`.
    fn contact(&mut self, b0: BodyId, b1: BodyId, pt: Vec3, n: Vec3, depth: f32, mu: f32) -> usize {
        let (x0, x1) = (self.body(b0), self.body(b1));
        let r0 = pt - x0.pos;
        let r1 = pt - x1.pos;
        let v0 = x0.v + x0.w.cross(r0);
        let v1 = x1.v + x1.w.cross(r1);
        let mut c = Contact::zeroed();
        c.pt = [pt, pt];
        c.n = n;
        c.pbody = [b0, b1];
        c.friction = mu;
        c.vn = n.dot(v0 - v1);
        let s = if depth > 0.0 {
            let a = (depth - 0.01) * 10.0;
            ((2.5 + a) - (2.5 - a).abs()) * 0.5
        } else {
            0.0
        };
        c.vreq = n * s;
        c.penetration = depth;
        c.nloc = x0.q.conj().rotate(n);
        c.id_bits = 0;
        c.ipart = [0, 0];
        self.contacts.push(c);
        self.contacts.len() - 1
    }
    fn case(self, rng: &mut Rng, vars: Vec<(usize, u32)>) -> Case {
        let e0 = {
            let mut s = 0.0f32;
            let mut e = 0.0f32;
            for b in &self.bodies {
                s += b.m * 2.5e-5;
                e += b.m * b.v.len2() + b.w.dot(b.l);
            }
            ((s - e).abs() + (s + e)) * 0.5
        };
        Case {
            dt: rng.pick(&[0.01f32, 0.02, 1.0 / 30.0, 0.005]),
            e0,
            prev: 0,
            cg: true,
            vars,
            bodies: self.bodies,
            dummy: dummy(),
            contacts: self.contacts,
            multi: self.multi,
            nrep: 1,
        }
    }
}

fn small_l(rng: &mut Rng, m: f32, s: f32) -> Vec3 {
    let v = rng.vec(-s, s);
    Vec3::new(v.x * m, v.y * m, v.z * m)
}

fn tilt(rng: &mut Rng, a: f32) -> Quat {
    let ax = rng.unit();
    let ang = rng.f(-a, a);
    let (s, c) = ((ang * 0.5).sin(), (ang * 0.5).cos());
    Quat::new(ax.x * s, ax.y * s, ax.z * s, c)
}

fn yaw(rng: &mut Rng) -> Quat {
    let ang = rng.f(-3.1, 3.1);
    Quat::new(0.0, 0.0, (ang * 0.5).sin(), (ang * 0.5).cos())
}

const MUS: [f32; 8] = [0.0, 0.005, 0.2, 0.5, 0.8, 1.0, 1.2, 5.0];

/// World offsets of solver vars (`W+104+x`).
const V_LCP_PASSES: usize = 160;

fn no_lcp() -> Vec<(usize, u32)> {
    vec![(V_LCP_PASSES, 0)]
}

/// A box resting on the static ground with 1..=8 contacts.
fn resting_box(rng: &mut Rng, vars: Vec<(usize, u32)>) -> Case {
    let mut isl = Island::new();
    let h = Vec3::new(rng.f(0.2, 2.0), rng.f(0.2, 2.0), rng.f(0.2, 1.5));
    let m = rng.pick(&[1.0f32, 10.0, 80.0, 500.0, 3000.0]) * rng.f(0.5, 1.5);
    let q = if rng.chance(0.5) {
        yaw(rng)
    } else {
        tilt(rng, 0.05) * yaw(rng)
    };
    let depth = rng.pick(&[0.0f32, 0.005, 0.012, 0.03, 0.2]) * rng.f(0.5, 1.5);
    let pos = Vec3::new(
        rng.f(-50.0, 50.0),
        rng.f(-50.0, 50.0),
        h.z - depth + rng.f(-0.5, 0.5),
    );
    let v = Vec3::new(rng.f(-0.3, 0.3), rng.f(-0.3, 0.3), rng.f(-1.0, 0.1));
    let l = small_l(rng, m, 0.2);
    let b = isl.add(box_body(pos, q, h, m, v, l));
    let n_ct = rng.range(1, 8);
    let mu = rng.pick(&MUS);
    let up = if rng.chance(0.7) {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let t = rng.vec(-0.05, 0.05);
        let u = Vec3::new(t.x, t.y, 1.0);
        u * (1.0 / u.len())
    };
    for k in 0..n_ct {
        let corner = Vec3::new(
            if k & 1 != 0 { h.x } else { -h.x },
            if k & 2 != 0 { h.y } else { -h.y },
            -h.z,
        );
        let mut pt = pos + rot(q, corner);
        if k >= 4 {
            pt += Vec3::new(rng.f(-0.1, 0.1), rng.f(-0.1, 0.1), 0.0);
        }
        let d = depth + rng.f(-0.002, 0.002);
        let mu_k = if rng.chance(0.8) { mu } else { rng.pick(&MUS) };
        isl.contact(b, BodyId::STATIC, pt, up, d, mu_k);
    }
    let mut c = isl.case(rng, vars);
    c.cg = rng.chance(0.7);
    c
}

/// A stack of `n` boxes on the ground, 4 contacts per interface.
fn stack(rng: &mut Rng, n: usize, vars: Vec<(usize, u32)>, heavy_light: bool) -> Case {
    let mut isl = Island::new();
    let mut z = 0.0f32;
    let mut prev = BodyId::STATIC;
    let base = Vec3::new(rng.f(-20.0, 20.0), rng.f(-20.0, 20.0), 0.0);
    let mu = rng.pick(&MUS);
    for k in 0..n {
        let h = Vec3::new(rng.f(0.3, 1.0), rng.f(0.3, 1.0), rng.f(0.2, 0.6));
        let mut m = rng.pick(&[1.0f32, 10.0, 100.0, 1000.0]) * rng.f(0.5, 1.5);
        if heavy_light {
            m = if k % 2 == 1 {
                rng.f(0.1, 1.0)
            } else {
                rng.f(200.0, 5000.0)
            };
        }
        let depth = rng.pick(&[0.0f32, 0.008, 0.02, 0.05]) * rng.f(0.5, 1.5);
        let q = if rng.chance(0.6) {
            yaw(rng)
        } else {
            tilt(rng, 0.03) * yaw(rng)
        };
        let pos = base + Vec3::new(rng.f(-0.05, 0.05), rng.f(-0.05, 0.05), z + h.z - depth);
        let v = Vec3::new(rng.f(-0.2, 0.2), rng.f(-0.2, 0.2), rng.f(-0.8, 0.1));
        let l = small_l(rng, m, 0.1);
        let b = isl.add(box_body(pos, q, h, m, v, l));
        let up = Vec3::new(0.0, 0.0, 1.0);
        for c in 0..4 {
            let corner = Vec3::new(
                if c & 1 != 0 { h.x } else { -h.x },
                if c & 2 != 0 { h.y } else { -h.y },
                -h.z,
            );
            let pt = pos + rot(q, corner) * 0.9;
            let mu_k = if rng.chance(0.85) { mu } else { rng.pick(&MUS) };
            isl.contact(b, prev, pt, up, depth + rng.f(-0.002, 0.002), mu_k);
        }
        if heavy_light && k % 2 == 1 && rng.chance(0.7) {
            // squeeze: the heavy body above also pushes down on the light one from the side
            let pt = pos + Vec3::new(h.x, 0.0, 0.0);
            isl.contact(b, prev, pt, Vec3::new(-1.0, 0.0, 0.0), depth, mu);
        }
        z = pos.z + h.z;
        prev = b;
    }
    let mut c = isl.case(rng, vars);
    c.cg = rng.chance(0.5);
    c
}

/// Random flags for a contact (stage-1 relevant combinations).
fn random_flags(rng: &mut Rng, c: &mut Contact) {
    match rng.below(12) {
        0 => {
            c.flags |= cf::ANGULAR;
            c.vreq = rng.vec(-0.5, 0.5);
        }
        1 => {
            c.flags |= cf::BILATERAL_3D;
            c.pn = rng.f(0.0, 50.0);
            c.vreq = rng.vec(-0.2, 0.2);
        }
        2 => {
            c.flags |= cf::BILATERAL_1D;
            c.pn = rng.f(0.0, 50.0);
        }
        3 => {
            c.flags |= cf::BILATERAL_2D;
            c.pn = rng.f(0.0, 50.0);
        }
        4 => {
            c.flags |= cf::SOFT;
            c.pn = rng.f(0.0, 30.0);
        }
        5 => {
            c.flags |= cf::PROJ_AXIS;
            c.nloc = rng.unit();
        }
        6 => {
            c.flags |= cf::PROJ_PLANE;
            c.nloc = rng.unit();
        }
        7 => {
            c.flags |= cf::KEEP_LIMIT | cf::BILATERAL_3D;
            c.pn = rng.f(0.0, 20.0);
        }
        8 => {
            c.flags |= cf::ANGULAR | cf::BILATERAL_3D;
            c.pn = rng.f(0.0, 20.0);
        }
        9 => {
            c.flags |= cf::KEEP_LIMIT;
            c.pn = rng.f(0.0, 20.0);
        }
        10 => {
            c.flags |= cf::SOFT | cf::PROJ_PLANE;
            c.nloc = rng.unit();
            c.pn = rng.f(0.0, 30.0);
        }
        _ => {}
    }
}

/// Random island: 1..=nb bodies, contacts between random pairs (or the ground), random flags.
fn random_island(rng: &mut Rng, max_b: usize, max_c: usize, vars: Vec<(usize, u32)>) -> Case {
    let mut isl = Island::new();
    let nb = rng.range(1, max_b as i32) as usize;
    let mut ids = Vec::new();
    for _ in 0..nb {
        let h = Vec3::new(rng.f(0.2, 1.5), rng.f(0.2, 1.5), rng.f(0.2, 1.5));
        let m = if rng.chance(0.08) {
            0.0
        } else {
            rng.pick(&[0.5f32, 5.0, 50.0, 500.0, 5000.0]) * rng.f(0.5, 1.5)
        };
        let speed = if rng.chance(0.1) { 20.0 } else { 1.0 };
        let v = rng.vec(-speed, speed);
        let l = small_l(rng, m, 0.5);
        ids.push(isl.add(box_body(rng.vec(-3.0, 3.0), rng.quat(), h, m, v, l)));
    }
    let nc = rng.range(1, max_c as i32) as usize;
    let mu = rng.pick(&MUS);
    for _ in 0..nc {
        let b0 = ids[rng.below(nb as u32) as usize];
        let b1 = if nb > 1 && rng.chance(0.5) {
            let o = ids[rng.below(nb as u32) as usize];
            if o == b0 {
                BodyId::STATIC
            } else {
                o
            }
        } else {
            BodyId::STATIC
        };
        if isl.body(b0).minv == 0.0 && isl.body(b1).minv == 0.0 {
            // two infinite-mass bodies: K = 0 gives inf/NaN impulses (see the solver docs on
            // NaN), not part of the bit-exact domain
            continue;
        }
        let p0 = isl.body(b0).pos;
        let pt = p0 + rng.vec(-1.0, 1.0);
        let n = if rng.chance(0.4) {
            Vec3::new(0.0, 0.0, 1.0)
        } else {
            rng.unit()
        };
        let d = rng.pick(&[0.0f32, 0.005, 0.02, 0.1]);
        let mu_k = if rng.chance(0.8) { mu } else { rng.pick(&MUS) };
        let i = isl.contact(b0, b1, pt, n, d, mu_k);
        if rng.chance(0.35) {
            random_flags(rng, &mut isl.contacts[i]);
        }
    }
    let mut c = isl.case(rng, vars);
    c.cg = rng.chance(0.5);
    if rng.chance(0.1) {
        c.e0 *= rng.pick(&[1e-3f32, 0.1, 0.0]);
    }
    c
}

/// Solver vars variations (world offsets).
fn random_vars(rng: &mut Rng, lcp: bool) -> Vec<(usize, u32)> {
    let mut v = Vec::new();
    if !lcp {
        v.push((V_LCP_PASSES, 0));
    }
    if rng.chance(0.2) {
        v.push((112, rng.pick(&[0u32, 10, 50, 200]))); // iteration floor
    }
    if rng.chance(0.15) {
        v.push((116, rng.pick(&[1u32, 2, 8]))); // iterations per contact
    }
    if rng.chance(0.15) {
        v.push((124, rng.pick(&[0.0005f32, 0.05, 0.5]).to_bits())); // tolN
    }
    if rng.chance(0.15) {
        v.push((176, rng.pick(&[0.0f32, 0.002, 0.2]).to_bits())); // tolT
    }
    v
}

#[test]
fn p4b_resting_box_stage1() {
    check("p4b_resting_box_stage1", 120, &|rng| {
        resting_box(rng, no_lcp())
    });
}

#[test]
fn p4b_stack_stage1() {
    check("p4b_stack_stage1", 80, &|rng| {
        let n = rng.range(2, 10) as usize;
        stack(rng, n, no_lcp(), false)
    });
}

#[test]
fn p4b_random_stage1() {
    check("p4b_random_stage1", 150, &|rng| {
        let vars = random_vars(rng, false);
        random_island(rng, 6, 24, vars)
    });
}

#[test]
fn p4b_stack_lcp() {
    check("p4b_stack_lcp", 80, &|rng| {
        let n = rng.range(2, 10) as usize;
        let vars = random_vars(rng, true);
        stack(rng, n, vars, false)
    });
}

#[test]
fn p4b_heavy_light() {
    check("p4b_heavy_light", 80, &|rng| {
        let n = rng.range(3, 8) as usize;
        let vars = random_vars(rng, true);
        stack(rng, n, vars, true)
    });
}

#[test]
fn p4b_random_lcp() {
    check("p4b_random_lcp", 150, &|rng| {
        let vars = random_vars(rng, true);
        random_island(rng, 6, 24, vars)
    });
}

/// Random island plus shared-Pn owners, hit masks and multi-body (rope) constraints.
fn special_island(rng: &mut Rng, vars: Vec<(usize, u32)>) -> Case {
    let mut c = random_island(rng, 6, 20, vars);
    let nc = c.contacts.len();
    let nb = c.bodies.len() as u32;
    for i in 0..nc {
        if rng.chance(0.15) && nc > 1 {
            let o = rng.below(nc as u32) as usize;
            c.contacts[i].flags |= cf::SHARED_PN;
            c.contacts[i].owner = ContactOwner::Count(ContactId(o as u32 + 1));
        }
        if rng.chance(0.1) {
            c.contacts[i].flags |= rng.range(1, 3) as u32;
        }
        let (m0, m1) = (
            c.bodies
                .get((c.contacts[i].pbody[0].0 as usize).wrapping_sub(1))
                .map_or(0.0, |b| b.minv),
            c.bodies
                .get((c.contacts[i].pbody[1].0 as usize).wrapping_sub(1))
                .map_or(0.0, |b| b.minv),
        );
        if rng.chance(0.15) && (m0 != 0.0 || m1 != 0.0) {
            // rope-style multi-body constraint
            let k = c.multi.len() as u32;
            let n_e = rng.range(0, 3);
            for _ in 0..n_e {
                let n1 = rng.unit();
                c.multi.push(MultiBodyEntry {
                    r: [rng.f(-1.0, 1.0), rng.f(-1.0, 1.0), rng.f(-1.0, 1.0)],
                    n0: [n1.x, n1.y, n1.z],
                    n1: if rng.chance(0.5) {
                        [n1.x, n1.y, n1.z]
                    } else {
                        [-n1.x, -n1.y, -n1.z]
                    },
                    unk36: 0.0,
                    body: BodyId(rng.below(nb) + 1),
                    idx: 0,
                });
            }
            let cc = &mut c.contacts[i];
            cc.flags = cf::MULTI_BODY;
            cc.owner = ContactOwner::Temp(k);
            cc.group_n = n_e;
            cc.n = rng.unit();
            cc.vreq = rng.unit();
            cc.nloc = Vec3::new(
                rng.pick(&[1.0f32, -1.0, 0.5]),
                rng.f(0.001, 2.0),
                rng.pick(&[0.0f32, 1e-9, 5.0, 100.0]),
            );
            cc.friction = rng.f(-1.0, 1.0);
            cc.pn = 0.0;
        }
    }
    c
}

#[test]
fn p4b_special() {
    check("p4b_special", 150, &|rng| {
        let lcp = rng.chance(0.7);
        let vars = random_vars(rng, lcp);
        special_island(rng, vars)
    });
}

/// The same island solved 2–3 times in a row on one solver context (stale block contents), and
/// the `prev > 0x10000` re-entry path.
#[test]
fn p4b_repeat() {
    check("p4b_repeat", 60, &repeat_case);
}

fn repeat_case(rng: &mut Rng) -> Case {
    {
        let lcp = rng.chance(0.7);
        let vars = random_vars(rng, lcp);
        let mut c = if rng.chance(0.5) {
            special_island(rng, vars)
        } else {
            let n = rng.range(2, 6) as usize;
            let hl = rng.chance(0.5);
            stack(rng, n, vars, hl)
        };
        c.nrep = rng.range(2, 3) as u32;
        if rng.chance(0.25) {
            c.prev = (1 << 16) | rng.range(1, 3);
        }
        c
    }
}

/// Large islands (oracle only): many resting boxes, `nC` above 1500 so that `4·nC` exceeds
/// the 6000-iteration floor, and stacks of up to 30.
#[test]
fn p4b_large_live() {
    check_live("p4b_large_live", 12, 4, &|rng| {
        let mut isl = Island::new();
        let nbox = rng.range(300, 520);
        for k in 0..nbox {
            let h = Vec3::new(0.4, 0.4, 0.4);
            let m = rng.f(5.0, 50.0);
            let pos = Vec3::new((k % 25) as f32 * 1.0, (k / 25) as f32 * 1.0, 0.39);
            let v = Vec3::new(rng.f(-0.5, 0.5), rng.f(-0.5, 0.5), rng.f(-1.0, 0.0));
            let b = isl.add(box_body(pos, Quat::IDENTITY, h, m, v, small_l(rng, m, 0.3)));
            for c in 0..4 {
                let corner = Vec3::new(
                    if c & 1 != 0 { 0.4 } else { -0.4 },
                    if c & 2 != 0 { 0.4 } else { -0.4 },
                    -0.4,
                );
                let mu = rng.pick(&MUS);
                isl.contact(
                    b,
                    BodyId::STATIC,
                    pos + corner,
                    Vec3::new(0.0, 0.0, 1.0),
                    0.02,
                    mu,
                );
            }
        }
        let vars = if rng.chance(0.5) {
            no_lcp()
        } else {
            Vec::new()
        };
        let mut c = isl.case(rng, vars);
        c.cg = false;
        c
    });
    check_live("p4b_tall_stack_live", 20, 10, &|rng| {
        let n = rng.range(12, 30) as usize;
        let vars = random_vars(rng, true);
        let hl = rng.chance(0.3);
        stack(rng, n, vars, hl)
    });
}

#[test]
#[ignore = "diagnostics: run the live special/repeat sets through the port with progress"]
fn p4b_live_port_progress() {
    use aaemu_physics::solver::probe::seed_of;
    let n: usize = std::env::var("AAEMU_PHYS_P4B_LIVE")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(0);
    for name in ["p4b_special", "p4b_repeat"] {
        let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
        for i in 0..n {
            let c = if name == "p4b_special" {
                let lcp = rng.chance(0.7);
                let vars = random_vars(&mut rng, lcp);
                special_island(&mut rng, vars)
            } else {
                repeat_case(&mut rng)
            };
            eprintln!("{name} {i}");
            if std::env::var_os("AAEMU_PHYS_P4B_WRITE").is_some() {
                std::fs::write(
                    std::env::var_os("AAEMU_PHYS_P4B_WRITE").unwrap(),
                    c.to_line(),
                )
                .ok();
            }
            let _ = c.run_port();
        }
    }
}

#[test]
#[ignore = "diagnostics: path histogram of the generators"]
fn p4b_path_stats() {
    use aaemu_physics::solver::probe::path_stats;
    println!(
        "{}",
        path_stats("resting", 300, &|rng| resting_box(rng, Vec::new()))
    );
    println!(
        "{}",
        path_stats("stack", 300, &|rng| {
            let n = rng.range(2, 10) as usize;
            stack(rng, n, Vec::new(), false)
        })
    );
    println!(
        "{}",
        path_stats("random", 300, &|rng| {
            let vars = random_vars(rng, false);
            random_island(rng, 6, 24, vars)
        })
    );
    println!(
        "{}",
        path_stats("stack_lcp", 300, &|rng| {
            let n = rng.range(2, 10) as usize;
            let vars = random_vars(rng, true);
            stack(rng, n, vars, false)
        })
    );
    println!(
        "{}",
        path_stats("heavy_light", 300, &|rng| {
            let n = rng.range(3, 8) as usize;
            let vars = random_vars(rng, true);
            stack(rng, n, vars, true)
        })
    );
    println!(
        "{}",
        path_stats("random_lcp", 300, &|rng| {
            let vars = random_vars(rng, true);
            random_island(rng, 6, 24, vars)
        })
    );
}

#[allow(dead_code)]
fn unused(_: Mat33) {}
