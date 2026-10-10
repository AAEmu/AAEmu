//! The rigid body record (`RigidBody`, 212 bytes in the binary): embedded in `CRigidEntity` at
//! `E+744`, in articulated joints at `joint+432`, and the per-thread static dummy bodies at
//! `0x3520C390 + 212·tid` (returned by the base `GetRigidBody`, slot 64 0x35053030, for static
//! entities). `dynamics.md` §1.1, `entities.md` §3.2.
//!
//! Bodies live in the world's body arena ([`BodyArena`]) and are referenced by [`BodyId`]; the
//! static dummy is [`BodyId::STATIC`].

use crate::math::{crt, Mat33, Quat, Vec3};

/// Index of a body in the world's [`BodyArena`].
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct BodyId(pub u32);

impl BodyId {
    /// The per-thread static dummy body (`0x3520C390`, thread 0). Its mass and inverse mass
    /// are 0 and its velocities are zeroed by every `GetRigidBody` call of a static entity.
    pub const STATIC: BodyId = BodyId(0);
}

/// `RigidBody` (212 bytes). Field offsets are the body offsets (`B+x`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RigidBody {
    /// `+0` centre of mass (world).
    pub pos: Vec3,
    /// `+12` principal-axes orientation.
    pub q: Quat,
    /// `+28` linear momentum.
    pub p: Vec3,
    /// `+40` angular momentum.
    pub l: Vec3,
    /// `+52` angular velocity.
    pub w: Vec3,
    /// `+64` linear velocity.
    pub v: Vec3,
    /// `+76` mass.
    pub m: f32,
    /// `+80` inverse mass (0 = static / infinite).
    pub minv: f32,
    /// `+84` volume.
    pub volume: f32,
    /// `+88` principal inertia.
    pub ibody: Vec3,
    /// `+100` inverse principal inertia.
    pub ibody_inv: Vec3,
    /// `+112` body → entity frame rotation (`q_ent = q ⊗ qfb`).
    pub qfb: Quat,
    /// `+128` entity origin → CoM offset in the entity frame.
    pub offsfb: Vec3,
    /// `+140` world inverse inertia `R·diag(Ibody_inv)·Rᵀ`.
    pub iinv: Mat33,
    /// `+176` collision force accumulator (`(P − P_saved)/dt` after the solver).
    pub fcoll: Vec3,
    /// `+188` collision torque accumulator.
    pub tcoll: Vec3,
    /// `+200` per-thread solver index (`short[4]`, index = physics thread).
    pub solver_idx: [i16; 4],
    /// `+208` extra energy term (`Eaux`).
    pub e_aux: f32,
}

impl Default for RigidBody {
    /// The body constructor `sub_35098EA0`: everything zero, `q` and `qfb` identity.
    fn default() -> Self {
        Self {
            pos: Vec3::ZERO,
            q: Quat::IDENTITY,
            p: Vec3::ZERO,
            l: Vec3::ZERO,
            w: Vec3::ZERO,
            v: Vec3::ZERO,
            m: 0.0,
            minv: 0.0,
            volume: 0.0,
            ibody: Vec3::ZERO,
            ibody_inv: Vec3::ZERO,
            qfb: Quat::IDENTITY,
            offsfb: Vec3::ZERO,
            iinv: Mat33::ZERO,
            fcoll: Vec3::ZERO,
            tcoll: Vec3::ZERO,
            solver_idx: [0; 4],
            e_aux: 0.0,
        }
    }
}

impl RigidBody {
    /// `sub_3509CE40` UpdateState: `Iinv = R·diag(Ibody_inv)·Rᵀ`; if `Minv > 0`:
    /// `v = P·Minv`, `w = Iinv·L`.
    pub fn update_state(&mut self) {
        let r = Mat33::from_quat(self.q);
        self.iinv = Mat33::sandwich_diag(&r, self.ibody_inv);
        if self.minv > 0.0 {
            self.v = self.p * self.minv;
            self.w = self.iinv.mul_vec(self.l);
        }
    }

    /// `sub_3509E180` Step(dt) (pinned by `tests/math_oracle.rs::body_step_matches_the_dll`).
    pub fn step(&mut self, dt: f32) {
        self.update_state();
        let v = self.v;
        let pos = self.pos;
        self.pos = Vec3::new(v.x * dt + pos.x, v.y * dt + pos.y, v.z * dt + pos.z);
        let w = self.w;
        let q = self.q;
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
        self.q = q.normalized_xwyz();
        if self.minv > 0.0 {
            let r = Mat33::from_quat(self.q);
            self.iinv = Mat33::sandwich_diag(&r, self.ibody_inv);
            self.w = self.iinv.mul_vec(self.l);
        }
    }

    /// The static dummy as `CPhysicalEntity::GetRigidBody` (0x35053030) leaves it: `P`, `L`,
    /// `w`, `v` zeroed (the rest of the dummy is the zero-initialised `.data` block, which
    /// nothing writes except the solver's per-thread index).
    pub fn reset_static_dummy(&mut self) {
        self.p = Vec3::ZERO;
        self.l = Vec3::ZERO;
        self.w = Vec3::ZERO;
        self.v = Vec3::ZERO;
    }
}

/// The world's bodies. Index 0 is the static dummy of physics thread 0.
#[derive(Clone, Debug)]
pub struct BodyArena {
    pub bodies: Vec<RigidBody>,
    /// Ordering key per body used where the binary sorts by body address (the solver's
    /// quicksorts). Entities get increasing keys in creation order; the static dummy's key
    /// mirrors where the `.data` dummy sits relative to heap memory (see the solver docs).
    /// The dummies are built by the static initialiser `sub_351EBAB0` with the body ctor.
    pub sort_key: Vec<u64>,
    free: Vec<u32>,
}

impl Default for BodyArena {
    fn default() -> Self {
        Self {
            bodies: vec![RigidBody::default()],
            sort_key: vec![0],
            free: Vec::new(),
        }
    }
}

impl BodyArena {
    /// Allocate a body (constructor state) with the given ordering key.
    pub fn alloc(&mut self, sort_key: u64) -> BodyId {
        if let Some(i) = self.free.pop() {
            self.bodies[i as usize] = RigidBody::default();
            self.sort_key[i as usize] = sort_key;
            BodyId(i)
        } else {
            self.bodies.push(RigidBody::default());
            self.sort_key.push(sort_key);
            BodyId(self.bodies.len() as u32 - 1)
        }
    }

    /// Return a body to the arena.
    pub fn release(&mut self, id: BodyId) {
        if id != BodyId::STATIC {
            self.free.push(id.0);
        }
    }

    #[inline]
    #[must_use]
    pub fn get(&self, id: BodyId) -> &RigidBody {
        &self.bodies[id.0 as usize]
    }

    #[inline]
    pub fn get_mut(&mut self, id: BodyId) -> &mut RigidBody {
        &mut self.bodies[id.0 as usize]
    }
}
