//! The parts of the Jitter2 (2.8.8) physics engine the C# ship physics uses: rigid bodies with a
//! single box shape (`TransformedShape(BoxShape)`), `World.Step` (damping, forces, gravity,
//! terrain contacts, integration) and the heightmap terrain collider
//! (`Physics/HeightMaps/*`: `Heightmap`, `HeightmapTester`, `HeightmapDetection`).
//!
//! Jitter's axes are kept: X = game X, **Y = up (game Z)**, Z = game Y.
//!
//! What is mirrored exactly (checked against Jitter2 2.8.8 itself, see the tests): the body state
//! and the step order — the velocity change of the previous step (`DeltaVelocity`: forces and
//! gravity) is added first, then the positions are integrated (quaternion exponential map
//! `dq * q`), then the velocities are damped by the default `Damping` multipliers (0.998 / 0.995)
//! and the next velocity change is computed from the accumulated forces (cleared), so a force
//! acts one step later — the `PreStep` / `PostStep` order (callers run the force
//! generators before [`PhysWorld::step`]), `SetMassInertia(mass)` of a translated box, the world
//! bounding box of the shape and the heightmap sampling (`RawHeights[x / 2, z / 2]` per metre,
//! bounded to `[0, Width - 1] x [0, Height - 1]` metres — the samples count is used as metres,
//! so only the first half of a world has terrain contacts: C# bug, mirrored).
//!
//! What is not bit-exact: Jitter's narrow phase (`NarrowPhase.MprEpa` box vs triangle) and its
//! contact solver (persistent manifolds, speculative contacts). Here every overlapped terrain
//! triangle is tested with SAT against the box and gives one contact (deepest box corner along
//! the triangle normal, the normal `HeightmapDetection` registers), solved with sequential
//! impulses (6 iterations, friction 0.2, no restitution, Baumgarte bias 0.2 with 0.01 m slop —
//! Jitter's defaults). The ship code overwrites the hull velocities every tick, so the terrain
//! contacts only matter for the vertical settling of beached hulls.

use crate::systems::ai::numerics::{Quat, Vec3Ext};
use crate::systems::skills::controllers::Vec3;

/// `JMatrix` (row-major, column vectors: `JVector.Transform(v, m) = m * v`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Mat3 {
    pub m: [[f32; 3]; 3],
}

impl Mat3 {
    pub const IDENTITY: Mat3 = Mat3 {
        m: [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]],
    };
    pub const ZERO: Mat3 = Mat3 { m: [[0.0; 3]; 3] };

    /// `JMatrix.CreateFromQuaternion(q)`.
    pub fn from_quat(q: Quat) -> Mat3 {
        let (x, y, z, w) = (q.x, q.y, q.z, q.w);
        let (xx, yy, zz) = (x * x, y * y, z * z);
        let (xy, xz, yz) = (x * y, x * z, y * z);
        let (xw, yw, zw) = (x * w, y * w, z * w);
        Mat3 {
            m: [
                [1.0 - 2.0 * (yy + zz), 2.0 * (xy - zw), 2.0 * (xz + yw)],
                [2.0 * (xy + zw), 1.0 - 2.0 * (xx + zz), 2.0 * (yz - xw)],
                [2.0 * (xz - yw), 2.0 * (yz + xw), 1.0 - 2.0 * (xx + yy)],
            ],
        }
    }

    pub fn diag(x: f32, y: f32, z: f32) -> Mat3 {
        Mat3 {
            m: [[x, 0.0, 0.0], [0.0, y, 0.0], [0.0, 0.0, z]],
        }
    }

    pub fn mul_vec(&self, v: Vec3) -> Vec3 {
        let m = &self.m;
        Vec3::new(
            m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z,
            m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
            m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z,
        )
    }

    pub fn mul(&self, o: &Mat3) -> Mat3 {
        let mut r = Mat3::ZERO;
        for i in 0..3 {
            for j in 0..3 {
                r.m[i][j] = (0..3).map(|k| self.m[i][k] * o.m[k][j]).sum();
            }
        }
        r
    }

    pub fn transpose(&self) -> Mat3 {
        let mut r = Mat3::ZERO;
        for i in 0..3 {
            for j in 0..3 {
                r.m[i][j] = self.m[j][i];
            }
        }
        r
    }

    pub fn scale(&self, s: f32) -> Mat3 {
        let mut r = *self;
        for row in &mut r.m {
            for v in row {
                *v *= s;
            }
        }
        r
    }

    pub fn add(&self, o: &Mat3) -> Mat3 {
        let mut r = *self;
        for i in 0..3 {
            for j in 0..3 {
                r.m[i][j] += o.m[i][j];
            }
        }
        r
    }

    /// `JMatrix.Absolute`.
    pub fn abs(&self) -> Mat3 {
        let mut r = *self;
        for row in &mut r.m {
            for v in row {
                *v = v.abs();
            }
        }
        r
    }

    /// `JMatrix.Inverse` (zero matrix when singular).
    pub fn inverse(&self) -> Mat3 {
        let m = &self.m;
        let det = m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1])
            - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
            + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
        if det.abs() < f32::MIN_POSITIVE {
            return Mat3::ZERO;
        }
        let inv = 1.0 / det;
        Mat3 {
            m: [
                [
                    (m[1][1] * m[2][2] - m[1][2] * m[2][1]) * inv,
                    (m[0][2] * m[2][1] - m[0][1] * m[2][2]) * inv,
                    (m[0][1] * m[1][2] - m[0][2] * m[1][1]) * inv,
                ],
                [
                    (m[1][2] * m[2][0] - m[1][0] * m[2][2]) * inv,
                    (m[0][0] * m[2][2] - m[0][2] * m[2][0]) * inv,
                    (m[0][2] * m[1][0] - m[0][0] * m[1][2]) * inv,
                ],
                [
                    (m[1][0] * m[2][1] - m[1][1] * m[2][0]) * inv,
                    (m[0][1] * m[2][0] - m[0][0] * m[2][1]) * inv,
                    (m[0][0] * m[1][1] - m[0][1] * m[1][0]) * inv,
                ],
            ],
        }
    }
}

/// `JQuaternion.CreateRotationY(angle)`.
pub fn quat_rotation_y(angle: f32) -> Quat {
    let (s, c) = (angle * 0.5).sin_cos();
    Quat::new(0.0, s, 0.0, c)
}

/// `PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(q))`: despite the tuple
/// order the first item is the rotation about Jitter Y (the hull yaw), the second the rotation
/// about Jitter X, the third `asin(2xy + 2zw)`. The C# goes through
/// `JQuaternion.CreateFromMatrix`, which may flip the quaternion's sign; every term is quadratic,
/// so the orientation quaternion is used directly.
pub fn yaw_pitch_roll(q: Quat) -> (f32, f32, f32) {
    let roll = (2.0 * q.y * q.w - 2.0 * q.x * q.z).atan2(1.0 - 2.0 * q.y * q.y - 2.0 * q.z * q.z);
    let pitch = (2.0 * q.x * q.w - 2.0 * q.y * q.z).atan2(1.0 - 2.0 * q.x * q.x - 2.0 * q.z * q.z);
    let yaw = (2.0 * q.x * q.y + 2.0 * q.z * q.w).asin();
    (roll, pitch, yaw)
}

/// `TransformedShape(new BoxShape(size), translation)` (identity transformation).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct BoxShape {
    /// Half of the `BoxShape` size.
    pub half: Vec3,
    /// The `TransformedShape` translation (body space).
    pub offset: Vec3,
}

impl BoxShape {
    pub fn new(size: Vec3, offset: Vec3) -> Self {
        Self {
            half: size * 0.5,
            offset,
        }
    }

    /// `Shape.WorldBoundingBox` for the body at `pos` / `orientation`.
    pub fn world_aabb(&self, pos: Vec3, orientation: Quat) -> Aabb {
        let r = Mat3::from_quat(orientation);
        let center = pos + r.mul_vec(self.offset);
        let ext = r.abs().mul_vec(self.half);
        Aabb {
            min: center - ext,
            max: center + ext,
        }
    }

    /// The 8 corners in world space.
    pub fn corners(&self, pos: Vec3, orientation: Quat) -> [Vec3; 8] {
        let r = Mat3::from_quat(orientation);
        let center = pos + r.mul_vec(self.offset);
        let mut out = [Vec3::new(0.0, 0.0, 0.0); 8];
        for (i, c) in out.iter_mut().enumerate() {
            let sx = if i & 1 == 0 { -1.0 } else { 1.0 };
            let sy = if i & 2 == 0 { -1.0 } else { 1.0 };
            let sz = if i & 4 == 0 { -1.0 } else { 1.0 };
            *c = center
                + r.mul_vec(Vec3::new(
                    self.half.x * sx,
                    self.half.y * sy,
                    self.half.z * sz,
                ));
        }
        out
    }

    /// `ShapeHelper` mass properties (density 1) about the body origin:
    /// `(mass, inertia)` with the parallel-axis term of the translation.
    pub fn mass_inertia(&self) -> (f32, Mat3) {
        let s = self.half * 2.0;
        let mass = s.x * s.y * s.z;
        let k = mass / 12.0;
        let box_inertia = Mat3::diag(
            k * (s.y * s.y + s.z * s.z),
            k * (s.x * s.x + s.z * s.z),
            k * (s.x * s.x + s.y * s.y),
        );
        let t = self.offset;
        let tt = t.length_squared();
        let outer = Mat3 {
            m: [
                [t.x * t.x, t.x * t.y, t.x * t.z],
                [t.y * t.x, t.y * t.y, t.y * t.z],
                [t.z * t.x, t.z * t.y, t.z * t.z],
            ],
        };
        let pat = Mat3::diag(tt, tt, tt).add(&outer.scale(-1.0)).scale(mass);
        (mass, box_inertia.add(&pat))
    }
}

/// `JBoundingBox`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Aabb {
    pub min: Vec3,
    pub max: Vec3,
}

/// `Jitter2.Dynamics.RigidBody` (the members the ship code uses).
#[derive(Debug, Clone, PartialEq)]
pub struct RigidBody {
    pub position: Vec3,
    pub orientation: Quat,
    pub velocity: Vec3,
    pub angular_velocity: Vec3,
    pub force: Vec3,
    pub torque: Vec3,
    /// `DeltaVelocity` / `DeltaAngularVelocity`: the forces and gravity of the previous step,
    /// added at the start of the next one (Jitter applies forces with one step of delay).
    pub delta_velocity: Vec3,
    pub delta_angular_velocity: Vec3,
    pub inverse_mass: f32,
    /// Body-space inverse inertia.
    pub inverse_inertia: Mat3,
    pub affected_by_gravity: bool,
    /// `IsActive` (`SetActivationState`); ship hulls never deactivate (`DeactivationTime = MaxValue`).
    pub active: bool,
    pub shape: BoxShape,
    /// `Damping` multipliers (Jitter defaults).
    pub linear_damping_mul: f32,
    pub angular_damping_mul: f32,
}

impl RigidBody {
    /// `World.CreateRigidBody()` + `AddShape(shape)` + `SetMassInertia(mass)` +
    /// `MotionType = Dynamic` + `SetActivationState(true)`.
    pub fn new(shape: BoxShape, mass: f32, position: Vec3, orientation: Quat) -> Self {
        let (shape_mass, inertia) = shape.mass_inertia();
        let scaled = if shape_mass > 0.0 {
            inertia.scale(mass / shape_mass)
        } else {
            inertia
        };
        Self {
            position,
            orientation,
            velocity: Vec3::new(0.0, 0.0, 0.0),
            angular_velocity: Vec3::new(0.0, 0.0, 0.0),
            force: Vec3::new(0.0, 0.0, 0.0),
            torque: Vec3::new(0.0, 0.0, 0.0),
            delta_velocity: Vec3::new(0.0, 0.0, 0.0),
            delta_angular_velocity: Vec3::new(0.0, 0.0, 0.0),
            inverse_mass: if mass > 0.0 { 1.0 / mass } else { 0.0 },
            inverse_inertia: scaled.inverse(),
            affected_by_gravity: true,
            active: true,
            shape,
            linear_damping_mul: 0.998,
            angular_damping_mul: 0.995,
        }
    }

    /// `Mass`.
    pub fn mass(&self) -> f32 {
        if self.inverse_mass > 0.0 {
            1.0 / self.inverse_mass
        } else {
            0.0
        }
    }

    /// `AddForce(force)` (at the centre of mass).
    pub fn add_force(&mut self, f: Vec3) {
        self.force += f;
    }

    /// `Shapes[0].WorldBoundingBox`.
    pub fn world_aabb(&self) -> Aabb {
        self.shape.world_aabb(self.position, self.orientation)
    }

    /// `InverseInertiaWorld = R * InverseInertia * R^T`.
    pub fn inverse_inertia_world(&self) -> Mat3 {
        let r = Mat3::from_quat(self.orientation);
        r.mul(&self.inverse_inertia).mul(&r.transpose())
    }
}

/// The heightmap samples the terrain collider reads: `Heightmap.RawHeights` (one sample per 2 m,
/// `[x, z]` = game `[x, y]`; unloaded cells read 0 like the C# array).
pub trait TerrainSamples {
    /// `RawHeights.GetLength(0)` / `(1)`.
    fn size(&self) -> (i32, i32);
    /// `RawHeights[sx, sz]`.
    fn sample(&self, sx: i32, sz: i32) -> f32;
}

/// `Heightmap.GetHeight(x, z)` = `RawHeights[x / 2, z / 2]` (metres → samples).
fn terrain_height(t: &dyn TerrainSamples, x: i32, z: i32) -> f32 {
    t.sample(x / 2, z / 2)
}

/// One registered contact (`World.RegisterContact(..., pointA, pointB, normal)`), body side.
#[derive(Debug, Clone, Copy)]
struct Contact {
    /// Contact point on the body (deepest corner).
    point: Vec3,
    /// Triangle normal (pointing out of the terrain).
    normal: Vec3,
    penetration: f32,
    normal_impulse: f32,
    tangent_impulse: [f32; 2],
}

/// Jitter's contact defaults.
const BIAS_FACTOR: f32 = 0.2;
const ALLOWED_PENETRATION: f32 = 0.01;
const FRICTION: f32 = 0.2;
const SOLVER_ITERATIONS: usize = 6;

/// `Jitter2.World` (gravity + the heightmap collider).
#[derive(Debug, Clone, Copy)]
pub struct PhysWorld {
    pub gravity: Vec3,
}

impl Default for PhysWorld {
    fn default() -> Self {
        Self {
            gravity: Vec3::new(0.0, -9.81, 0.0),
        }
    }
}

impl PhysWorld {
    /// `World.Step(dt, false)` minus the `PreStep` / `PostStep` events (the caller runs the force
    /// generators first). `dt <= 0` does nothing.
    pub fn step(
        &self,
        bodies: &mut [&mut RigidBody],
        dt: f32,
        terrain: Option<&dyn TerrainSamples>,
    ) {
        if dt <= 0.0 {
            return;
        }
        // IntegrateForces: the velocity change computed at the end of the previous step.
        for b in bodies.iter_mut() {
            if !b.active || b.inverse_mass <= 0.0 {
                continue;
            }
            b.velocity += b.delta_velocity;
            b.angular_velocity += b.delta_angular_velocity;
        }
        // Terrain contacts (collision detection runs on the positions before integration).
        if let Some(t) = terrain {
            for b in bodies.iter_mut() {
                if !b.active || b.inverse_mass <= 0.0 {
                    continue;
                }
                let mut contacts = terrain_contacts(t, b);
                if !contacts.is_empty() {
                    solve_contacts(b, &mut contacts, dt);
                }
            }
        }
        // Integration, then damping and the next step's velocity change (forces + gravity).
        for b in bodies.iter_mut() {
            if !b.active || b.inverse_mass <= 0.0 {
                continue;
            }
            integrate(b, dt);
            b.angular_velocity *= b.angular_damping_mul;
            b.velocity *= b.linear_damping_mul;
            let mut dv = b.force * (b.inverse_mass * dt);
            if b.affected_by_gravity {
                dv += self.gravity * dt;
            }
            b.delta_velocity = dv;
            b.delta_angular_velocity = b.inverse_inertia_world().mul_vec(b.torque) * dt;
            b.force = Vec3::new(0.0, 0.0, 0.0);
            b.torque = Vec3::new(0.0, 0.0, 0.0);
        }
    }
}

/// Position and orientation integration (`dq * q`, Taylor expansion for tiny angles).
pub fn integrate(b: &mut RigidBody, dt: f32) {
    b.position += b.velocity * dt;
    let avel = b.angular_velocity;
    let angle = avel.length();
    let axis = if angle < 0.001 {
        avel * (0.5 * dt - dt * dt * dt * 0.020_833_334 * angle * angle)
    } else {
        avel * ((0.5 * angle * dt).sin() / angle)
    };
    let dq = Quat::new(axis.x, axis.y, axis.z, (angle * dt * 0.5).cos());
    b.orientation = dq.multiply(b.orientation).normalized();
}

/// `HeightmapDetection.Filter`: every quad under the body's bounding box (two triangles each),
/// within `[0, Width - 1] x [0, Height - 1]` (metres).
fn terrain_contacts(t: &dyn TerrainSamples, b: &RigidBody) -> Vec<Contact> {
    let (w, h) = t.size();
    let bb = b.world_aabb();
    // HeightmapTester.WorldBoundingBox: [0, Width-1] x [0, Height-1] (+ the height range).
    if bb.max.x < 0.0 || bb.min.x > (w - 1) as f32 || bb.max.z < 0.0 || bb.min.z > (h - 1) as f32 {
        return Vec::new();
    }
    let min_x = (bb.min.x as i32).max(0);
    let min_z = (bb.min.z as i32).max(0);
    let max_x = (w - 1).min(bb.max.x as i32 + 1);
    let max_z = (h - 1).min(bb.max.z as i32 + 1);
    let corners = b.shape.corners(b.position, b.orientation);
    let r = Mat3::from_quat(b.orientation);
    let center = b.position + r.mul_vec(b.shape.offset);
    let axes = [
        Vec3::new(r.m[0][0], r.m[1][0], r.m[2][0]),
        Vec3::new(r.m[0][1], r.m[1][1], r.m[2][1]),
        Vec3::new(r.m[0][2], r.m[1][2], r.m[2][2]),
    ];
    let mut out = Vec::new();
    for x in min_x..max_x {
        for z in min_z..max_z {
            let h00 = terrain_height(t, x, z);
            let h10 = terrain_height(t, x + 1, z);
            let h11 = terrain_height(t, x + 1, z + 1);
            let h01 = terrain_height(t, x, z + 1);
            let (xf, zf) = (x as f32, z as f32);
            let tris = [
                [
                    Vec3::new(xf, h00, zf),
                    Vec3::new(xf + 1.0, h10, zf),
                    Vec3::new(xf + 1.0, h11, zf + 1.0),
                ],
                [
                    Vec3::new(xf, h00, zf),
                    Vec3::new(xf + 1.0, h11, zf + 1.0),
                    Vec3::new(xf, h01, zf + 1.0),
                ],
            ];
            for tri in tris {
                // normal = Normalize((C - A) % (B - A))
                let n = (tri[2] - tri[0]).cross(tri[1] - tri[0]).normalized();
                if !box_triangle_overlap(center, &axes, b.shape.half, &tri, n) {
                    continue;
                }
                // deepest corner along -n
                let plane = n.dot(tri[0]);
                let (mut deepest, mut depth) = (corners[0], f32::MIN);
                for c in corners {
                    let d = plane - n.dot(c);
                    if d > depth {
                        depth = d;
                        deepest = c;
                    }
                }
                if depth <= 0.0 {
                    continue;
                }
                out.push(Contact {
                    point: deepest,
                    normal: n,
                    penetration: depth,
                    normal_impulse: 0.0,
                    tangent_impulse: [0.0; 2],
                });
            }
        }
    }
    out
}

/// Separating-axis test of an oriented box against a triangle (13 axes).
fn box_triangle_overlap(
    center: Vec3,
    axes: &[Vec3; 3],
    half: Vec3,
    tri: &[Vec3; 3],
    n: Vec3,
) -> bool {
    let v = [tri[0] - center, tri[1] - center, tri[2] - center];
    let e = [v[1] - v[0], v[2] - v[1], v[0] - v[2]];
    let h = [half.x, half.y, half.z];
    let test = |axis: Vec3| -> bool {
        if axis.length_squared() < 1e-12 {
            return true;
        }
        let p = [axis.dot(v[0]), axis.dot(v[1]), axis.dot(v[2])];
        let r = h[0] * axes[0].dot(axis).abs()
            + h[1] * axes[1].dot(axis).abs()
            + h[2] * axes[2].dot(axis).abs();
        let mn = p[0].min(p[1]).min(p[2]);
        let mx = p[0].max(p[1]).max(p[2]);
        !(mn > r || mx < -r)
    };
    for a in axes {
        if !test(*a) {
            return false;
        }
    }
    if !test(n) {
        return false;
    }
    for a in axes {
        for ed in &e {
            if !test(a.cross(*ed)) {
                return false;
            }
        }
    }
    true
}

/// Sequential impulses against the static terrain.
fn solve_contacts(b: &mut RigidBody, contacts: &mut [Contact], dt: f32) {
    let inv_i = b.inverse_inertia_world();
    for _ in 0..SOLVER_ITERATIONS {
        for c in contacts.iter_mut() {
            let r = c.point - b.position;
            let vrel = b.velocity + b.angular_velocity.cross(r);
            let n = c.normal;
            let kn = b.inverse_mass + n.dot(inv_i.mul_vec(r.cross(n)).cross(r));
            if kn <= 0.0 {
                continue;
            }
            let bias = BIAS_FACTOR / dt * (c.penetration - ALLOWED_PENETRATION).max(0.0);
            let vn = vrel.dot(n);
            let mut lambda = (-vn + bias) / kn;
            let old = c.normal_impulse;
            c.normal_impulse = (old + lambda).max(0.0);
            lambda = c.normal_impulse - old;
            apply_impulse(b, &inv_i, r, n * lambda);

            // friction along two tangents
            let t1 = if n.x.abs() > 0.57 {
                Vec3::new(n.y, -n.x, 0.0).normalized()
            } else {
                Vec3::new(0.0, n.z, -n.y).normalized()
            };
            let t2 = n.cross(t1);
            for (k, t) in [t1, t2].into_iter().enumerate() {
                let vrel = b.velocity + b.angular_velocity.cross(r);
                let kt = b.inverse_mass + t.dot(inv_i.mul_vec(r.cross(t)).cross(r));
                if kt <= 0.0 {
                    continue;
                }
                let mut l = -vrel.dot(t) / kt;
                let max_f = FRICTION * c.normal_impulse;
                let old = c.tangent_impulse[k];
                c.tangent_impulse[k] = (old + l).clamp(-max_f, max_f);
                l = c.tangent_impulse[k] - old;
                apply_impulse(b, &inv_i, r, t * l);
            }
        }
    }
}

fn apply_impulse(b: &mut RigidBody, inv_i: &Mat3, r: Vec3, imp: Vec3) {
    b.velocity += imp * b.inverse_mass;
    b.angular_velocity += inv_i.mul_vec(r.cross(imp));
}

/// `HeightmapTester.RayCast` is not used by the ship code (no caller); not ported.
#[cfg(test)]
mod tests {
    use super::*;

    fn close(a: f32, b: f32, eps: f32) -> bool {
        (a - b).abs() <= eps
    }

    #[test]
    fn rotation_y_gives_yaw_in_the_first_item() {
        for a in [-2.5f32, -0.3, 0.0, 0.7, 1.5, 3.0] {
            let (yaw, p, r) = yaw_pitch_roll(quat_rotation_y(a));
            assert!(close(yaw, a, 1e-5), "{a} -> {yaw}");
            assert!(close(p, 0.0, 1e-6) && close(r, 0.0, 1e-6));
        }
    }

    #[test]
    fn matrix_matches_quaternion_rotation() {
        let q = Quat::from_yaw_pitch_roll(0.4, -0.2, 0.9);
        let v = Vec3::new(1.5, -2.0, 0.25);
        let a = Mat3::from_quat(q).mul_vec(v);
        let b = q.rotate(v);
        assert!(close(a.x, b.x, 1e-5) && close(a.y, b.y, 1e-5) && close(a.z, b.z, 1e-5));
        let m = Mat3::diag(2.0, 4.0, 8.0);
        let i = m.mul(&m.inverse());
        assert!(close(i.m[0][0], 1.0, 1e-6) && close(i.m[2][2], 1.0, 1e-6));
    }

    #[test]
    fn box_aabb_and_inertia() {
        let s = BoxShape::new(Vec3::new(2.0, 4.0, 6.0), Vec3::new(0.0, 1.0, 0.0));
        let bb = s.world_aabb(
            Vec3::new(10.0, 0.0, 0.0),
            quat_rotation_y(std::f32::consts::FRAC_PI_2),
        );
        assert!(close(bb.min.x, 7.0, 1e-5) && close(bb.max.x, 13.0, 1e-5));
        assert!(close(bb.min.y, -1.0, 1e-5) && close(bb.max.y, 3.0, 1e-5));
        assert!(close(bb.min.z, -1.0, 1e-5) && close(bb.max.z, 1.0, 1e-5));
        let (m, i) = s.mass_inertia();
        assert!(close(m, 48.0, 1e-4));
        // Ixx = m/12 (16 + 36) + m * 1 (offset along y)
        assert!(close(i.m[0][0], 48.0 / 12.0 * 52.0 + 48.0, 1e-3));
        assert!(close(i.m[1][1], 48.0 / 12.0 * 40.0, 1e-3));
    }

    /// The output of Jitter2 2.8.8 for the same scenario (`ai-harness/jitter`: a translated
    /// box hull of 20 t, a vertical force before every step, the velocity overwritten every 5th
    /// step and a yaw rate every step like the ship code): per step the position, orientation,
    /// velocity, yaw rate, `WorldBoundingBox.Min.X` / `Max.Y` (f32 bits); then the body inverse
    /// inertia (M11 M22 M33 M12 M23 M13).
    const JITTER2_REFERENCE: &str = r#"
0 42C81EB8 42C60000 4347FAE1 00000000 3E7D5777 00000000 3F780AA5 3FBF9DB2 00000000 BEFF7CEE 00000000 42BF932B 42C80000
1 42C83D61 42C6019B 4347F5C5 00000000 3E7C5970 00000000 3F781AD4 3FBF3B97 3DA067D9 BEFEFA1F BD4BC6A8 42BFB636 42C8019B
2 42C85BFA 42C60268 4347F0AC 00000000 3E7A5D30 00000000 3F783B00 3FBED9AE 3D1FC3A4 BEFE7793 BDCBC6A8 42BFDD99 42C80268
3 42C87A83 42C60267 4347EB95 00000000 3E776257 00000000 3F786AC9 3FBE77F7 B975F5D1 BEFDF54A BE18D4FE 42C00962 42C80267
4 42C898FD 42C60401 4347E681 00000000 3E736852 00000000 3F78A99C 3FBE1672 3D9FED1D BEFD7344 BE4BC6A8 42C039A6 42C80401
5 42C8B7B5 42C604CD 4347E162 00000000 3E6E6E64 00000000 3F78F6B4 3FBF9DB2 3D1ECEA9 BEFF7CEE BE7EB852 42C06ECC 42C804CD
6 42C8D65E 42C604CB 4347DC46 00000000 3E6873AB 00000000 3F79511D 3FBF3B97 B9F539B2 BEFEFA1F BE98D4FE 42C0A8A6 42C804CB
7 42C8F4F7 42C60664 4347D72D 00000000 3E6873AB 00000000 3F79511D 3FBED9AE 3D9F731D BEFE7793 00000000 42C0C73F 42C80664
8 42C91380 42C6072E 4347D216 00000000 3E677457 00000000 3F795FF5 3FBE77F7 3D1DDB26 BEFDF54A BD4BC6A8 42C0EA57 42C8072E
9 42C931FA 42C6072A 4347CD02 00000000 3E657580 00000000 3F797D74 3FBE1672 BA375E6E BEFD7344 BDCBC6A8 42C111F8 42C8072A
10 42C950B2 42C608C2 4347C7E3 00000000 3E6276CE 00000000 3F79A939 3FBF9DB2 3D9EF9D8 BEFF7CEE BE18D4FE 42C13E78 42C808C2
11 42C96F5B 42C6098B 4347C2C7 00000000 3E5E77BB 00000000 3F79E2B0 3FBF3B97 3D1CE918 BEFEFA1F BE4BC6A8 42C16FA0 42C8098B
12 42C98DF4 42C60986 4347BDAE 00000000 3E59779C 00000000 3F7A2913 3FBED9AE BA73C2F2 BEFE7793 BE7EB852 42C1A587 42C80986
13 42C9AC7D 42C60B1D 4347B897 00000000 3E5375A1 00000000 3F7A7B6E 3FBE77F7 3D9E814D BEFDF54A BE98D4FE 42C1E04B 42C80B1D
14 42C9CAF7 42C60BE5 4347B383 00000000 3E5375A1 00000000 3F7A7B6E 3FBE1672 3D1BF87E BEFD7344 00000000 42C1FEC5 42C80BE5
15 42C9E9AF 42C60BDF 4347AE64 00000000 3E52751C 00000000 3F7A88EF 3FBF9DB2 BA97E553 BEFF7CEE BD4BC6A8 42C22239 42C80BDF
16 42CA0858 42C60D74 4347A948 00000000 3E5073E8 00000000 3F7AA3BE 3FBF3B97 3D9E097C BEFEFA1F BDCBC6A8 42C24A5F 42C80D74
17 42CA26F1 42C60E3B 4347A42F 00000000 3E4D71B4 00000000 3F7ACB7B 3FBED9AE 3D1B0957 BEFE7793 BE18D4FE 42C27743 42C80E3B
18 42CA457A 42C60E34 43479F18 00000000 3E496E05 00000000 3F7AFF8E 3FBE77F7 BAB5BAE5 BEFDF54A BE4BC6A8 42C2A8F8 42C80E34
19 42CA63F4 42C60FC8 43479A04 00000000 3E446847 00000000 3F7B3F37 3FBE1672 3D9D9262 BEFD7344 BE7EB852 42C2DF95 42C80FC8
20 42CA82AC 42C6108E 434794E5 00000000 3E3E5FB7 00000000 3F7B8979 3FBF9DB2 3D1A1B9D BEFF7CEE BE98D4FE 42C31B86 42C8108E
21 42CAA155 42C61086 43478FC9 00000000 3E3E5FB7 00000000 3F7B8979 3FBF3B97 BAD362EE BEFEFA1F 00000000 42C33A2F 42C81086
22 42CABFEE 42C61219 43478AB0 00000000 3E3D5E1E 00000000 3F7B95A0 3FBED9AE 3D9D1BFE BEFE7793 BD4BC6A8 42C35DAC 42C81219
23 42CADE77 42C612DD 43478599 00000000 3E3B5AC6 00000000 3F7BADBC 3FBE77F7 3D192F4E BEFDF54A BDCBC6A8 42C38607 42C812DD
24 42CAFCF1 42C612D3 43478085 00000000 3E385567 00000000 3F7BD16B 3FBE1672 BAF0DDAE BEFD7344 BE18D4FE 42C3B348 42C812D3
25 42CB1BA9 42C61465 43477B66 00000000 3E344D95 00000000 3F7C0017 3FBF9DB2 3D9CA650 BEFF7CEE BE4BC6A8 42C3E5D0 42C81465
26 42CB3A52 42C61528 4347764A 00000000 3E2F42C5 00000000 3F7C38FC 3FBF3B97 3D18446A BEFEFA1F BE7EB852 42C41D67 42C81528
27 42CB58EB 42C6151D 43477131 00000000 3E293451 00000000 3F7C7B1F 3FBED9AE BB071593 BEFE7793 BE98D4FE 42C45A27 42C8151D
28 42CB7774 42C616AE 43476C1A 00000000 3E293451 00000000 3F7C7B1F 3FBE77F7 3D9C3156 BEFDF54A 00000000 42C478B0 42C816AE
29 42CB95EE 42C61770 43476706 00000000 3E2831C2 00000000 3F7C85EB 3FBE1672 3D175AEE BEFD7344 BD4BC6A8 42C49C37 42C81770
30 42CBB4A6 42C61764 434761E7 00000000 3E262C81 00000000 3F7C9B50 3FBF9DB2 BB15A5DA BEFF7CEE BDCBC6A8 42C4C510 42C81764
31 42CBD34F 42C618F3 43475CCB 00000000 3E23244E 00000000 3F7CBAEE 3FBF3B97 3D9BBD0F BEFEFA1F BE18D4FE 42C4F2F6 42C818F3
32 42CBF1E8 42C619B4 434757B2 00000000 3E1F18CB 00000000 3F7CE42E 3FBED9AE 3D1672D7 BEFE7793 BE4BC6A8 42C525F8 42C819B4
33 42CC1071 42C619A7 4347529B 00000000 3E1A097C 00000000 3F7D1648 3FBE77F7 BB241FDD BEFDF54A BE7EB852 42C55E2F 42C819A7
34 42CC2EEB 42C61B35 43474D87 00000000 3E13F5D1 00000000 3F7D5043 3FBE1672 3D9B497B BEFD7344 BE98D4FE 42C59BB3 42C81B35
35 42CC4DA3 42C61BF5 43474868 00000000 3E13F5D1 00000000 3F7D5043 3FBF9DB2 3D158C25 BEFF7CEE 00000000 42C5BA6B 42C81BF5
36 42CC6C4C 42C61BE7 4347434C 00000000 3E12F267 00000000 3F7D59B3 3FBF3B97 BB32839B BEFEFA1F BD4BC6A8 42C5DE46 42C81BE7
37 42CC8AE5 42C61D74 43473E33 00000000 3E10EB77 00000000 3F7D6C60 3FBED9AE 3D9AD698 BEFE7793 BDCBC6A8 42C6074A 42C81D74
38 42CCA96E 42C61E33 4347391C 00000000 3E0DE0C9 00000000 3F7D87E9 3FBE77F7 3D14A6D5 BEFDF54A BE18D4FE 42C6357D 42C81E33
39 42CCC7E8 42C61E24 43473408 00000000 3E09D209 00000000 3F7DABB6 3FBE1672 BB40D145 BEFD7344 BE4BC6A8 42C668F4 42C81E24
40 42CCE6A0 42C61FB0 43472EE9 00000000 3E04BECE 00000000 3F7DD700 3FBF9DB2 3D9A6465 BEFF7CEE BE7EB852 42C6A20D 42C81FB0
41 42CD0549 42C6206E 434729CD 00000000 3DFD4D38 00000000 3F7E08CE 3FBF3B97 3D13C2E4 BEFEFA1F BE98D4FE 42C6E095 42C8206E
42 42CD23E2 42C6205D 434724B4 00000000 3DFD4D38 00000000 3F7E08CE 3FBED9AE BB4F0909 BEFE7793 00000000 42C6FF2E 42C8205D
43 42CD426B 42C621E8 43471F9D 00000000 3DFB44ED 00000000 3F7E10E1 3FBE77F7 3D99F2E1 BEFDF54A BD4BC6A8 42C7230C 42C821E8
44 42CD60E5 42C622A4 43471A89 00000000 3DF73425 00000000 3F7E20D4 3FBE1672 3D12E050 BEFD7344 BDCBC6A8 42C74C36 42C822A4
45 42CD7F9D 42C62292 4347156A 00000000 3DF11A80 00000000 3F7E3845 3FBF9DB2 BB5D2B09 BEFF7CEE BE18D4FE 42C77B01 42C82292
46 42CD9E46 42C6241C 4347104E 00000000 3DE8F772 00000000 3F7E569C 3FBF3B97 3D99820B BEFEFA1F BE4BC6A8 42C7AF2E 42C8241C
47 42CDBCDF 42C624D7 43470B35 00000000 3DDECA4B 00000000 3F7E7B13 3FBED9AE 3D11FF18 BEFE7793 BE7EB852 42C7E8D0 42C824D7
48 42CDDB68 42C624C4 4347061E 00000000 3DD29238 00000000 3F7EA4AC 3FBE77F7 BB6B3754 BEFDF54A BE98D4FE 42C827FE 42C824C4
49 42CDF9E2 42C6264D 4347010A 00000000 3DD29238 00000000 3F7EA4AC 3FBE1672 3D9911E2 BEFD7344 00000000 42C84678 42C8264D
I 36334620 36302F39 37AEF1FC B3100A71 B52FD350 34D983E9
"#;

    fn bits(s: &str) -> f32 {
        f32::from_bits(u32::from_str_radix(s, 16).expect("hex"))
    }

    /// Differences in units in the last place.
    fn ulps(a: f32, b: f32) -> u32 {
        if a == b {
            return 0;
        }
        let (ia, ib) = (a.to_bits() as i32, b.to_bits() as i32);
        if (ia < 0) != (ib < 0) {
            return (a - b).abs().to_bits();
        }
        (ia - ib).unsigned_abs()
    }

    #[test]
    fn step_matches_jitter2() {
        let shape = BoxShape::new(Vec3::new(4.0, 3.0, 14.0), Vec3::new(0.3, -0.5, 1.2));
        let mut b = RigidBody::new(
            shape,
            20000.0,
            Vec3::new(100.0, 99.0, 200.0),
            quat_rotation_y(0.5),
        );
        let w = PhysWorld::default();
        let mut lines = JITTER2_REFERENCE.lines().filter(|l| !l.trim().is_empty());
        let mut worst = 0u32;
        for i in 0..50 {
            let mul = if i % 3 == 0 { 1.2 } else { 0.9 };
            b.add_force(Vec3::new(0.0, 20000.0 * 9.81 * mul, 0.0));
            if i % 5 == 0 {
                b.velocity = Vec3::new(1.5, b.velocity.y, -0.5);
            }
            b.angular_velocity = Vec3::new(0.0, -0.05 * (i % 7) as f32, 0.0);
            w.step(&mut [&mut b], 0.04, None);
            let want: Vec<f32> = lines
                .next()
                .expect("line")
                .split(' ')
                .skip(1)
                .map(bits)
                .collect();
            let bb = b.world_aabb();
            let got = [
                b.position.x,
                b.position.y,
                b.position.z,
                b.orientation.x,
                b.orientation.y,
                b.orientation.z,
                b.orientation.w,
                b.velocity.x,
                b.velocity.y,
                b.velocity.z,
                b.angular_velocity.y,
                bb.min.x,
                bb.max.y,
            ];
            for (k, (g, e)) in got.iter().zip(&want).enumerate() {
                let u = ulps(*g, *e);
                assert!(
                    u <= 64 || (g - e).abs() < 1e-6,
                    "step {i} value {k}: {g} vs {e} ({u} ulps)"
                );
                worst = worst.max(if (g - e).abs() < 1e-6 { 0 } else { u });
            }
        }
        let want: Vec<f32> = lines
            .next()
            .expect("I")
            .split(' ')
            .skip(1)
            .map(bits)
            .collect();
        let ii = b.inverse_inertia;
        let got = [
            ii.m[0][0], ii.m[1][1], ii.m[2][2], ii.m[0][1], ii.m[1][2], ii.m[0][2],
        ];
        for (g, e) in got.iter().zip(&want) {
            assert!(
                (g - e).abs() <= e.abs() * 1e-4 + 1e-12,
                "inverse inertia {g} vs {e}"
            );
        }
        println!("worst difference vs Jitter2: {worst} ulps");
    }

    #[test]
    fn forces_act_one_step_later_and_zero_dt_does_nothing() {
        let s = BoxShape::new(Vec3::new(1.0, 1.0, 1.0), Vec3::new(0.0, 0.0, 0.0));
        let mut b = RigidBody::new(s, 10.0, Vec3::new(0.0, 100.0, 0.0), Quat::IDENTITY);
        b.add_force(Vec3::new(0.0, 98.1, 0.0)); // cancels the gravity of the next step
        let w = PhysWorld::default();
        w.step(&mut [&mut b], 0.04, None);
        assert_eq!(b.velocity.y, 0.0);
        w.step(&mut [&mut b], 0.04, None);
        assert!(close(b.velocity.y, 0.0, 1e-5));
        w.step(&mut [&mut b], 0.04, None);
        assert!(close(b.velocity.y, -9.81 * 0.04 * 0.998, 1e-5));
        let before = b.clone();
        w.step(&mut [&mut b], 0.0, None);
        assert_eq!(b, before);
    }

    #[test]
    fn yaw_rate_integrates_heading() {
        let s = BoxShape::new(Vec3::new(1.0, 1.0, 1.0), Vec3::new(0.0, 0.0, 0.0));
        let mut b = RigidBody::new(s, 10.0, Vec3::new(0.0, 0.0, 0.0), Quat::IDENTITY);
        b.affected_by_gravity = false;
        let w = PhysWorld::default();
        for _ in 0..25 {
            b.angular_velocity = Vec3::new(0.0, 0.5, 0.0);
            w.step(&mut [&mut b], 0.04, None);
        }
        let (yaw, _, _) = yaw_pitch_roll(b.orientation);
        // 25 steps of 0.5 rad/s * 0.04 s (the damping applies after the integration)
        assert!(close(yaw, 25.0 * 0.5 * 0.04, 1e-4), "{yaw}");
    }

    struct Flat(f32, i32);
    impl TerrainSamples for Flat {
        fn size(&self) -> (i32, i32) {
            (self.1, self.1)
        }
        fn sample(&self, _sx: i32, _sz: i32) -> f32 {
            self.0
        }
    }

    #[test]
    fn box_rests_on_terrain_inside_the_collider_only() {
        let terrain = Flat(50.0, 512); // 512 samples -> collider spans 511 m (half of 1024 m)
        let w = PhysWorld::default();
        let s = BoxShape::new(Vec3::new(2.0, 2.0, 4.0), Vec3::new(0.0, 0.0, 0.0));
        let mut b = RigidBody::new(s, 1000.0, Vec3::new(100.0, 51.5, 100.0), Quat::IDENTITY);
        for _ in 0..100 {
            w.step(&mut [&mut b], 0.04, Some(&terrain));
        }
        assert!(
            b.position.y > 50.5 && b.position.y < 51.2,
            "{}",
            b.position.y
        );
        // Beyond Width-1 metres (the C# half-world bug) there is no terrain.
        let mut far = RigidBody::new(s, 1000.0, Vec3::new(700.0, 51.5, 100.0), Quat::IDENTITY);
        for _ in 0..25 {
            w.step(&mut [&mut far], 0.04, Some(&terrain));
        }
        assert!(far.position.y < 50.0);
    }
}
