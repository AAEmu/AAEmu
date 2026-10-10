//! Port of `Models/Game/World/Transform` (`Transform`, `PositionAndRotation`) shared by every
//! world object.
//!
//! The C# `Transform` holds a `Local` position/rotation plus an optional parent transform; its
//! `World` value is the parent's world value composed with `Local` (`GetWorldPosition`). Here
//! [`Transform`] stores the *local* values flat (`x`, `y`, `z`, `rot_*`) — for an object without
//! a parent (players, NPCs, most doodads) local == world, which is all phases 1–2 needed. A child
//! (a doodad on a house or a ship, a passenger) records its parent in [`Transform::parent_obj_id`];
//! its world value is computed with [`PositionAndRotation::compose`] by whoever owns both objects
//! (`WorldInstance::world_pos_rot`). Sticky parents, debug trackers and `FinalizeTransform`
//! batching are left to the subsystems that need them.

/// `Helpers.ConvertLongX` / `ConvertLongY` (float -> fixed-point long).
pub fn convert_long(v: f32) -> i64 {
    ((v * 4096.0) as i64) << 32
}

const TWO_PI: f32 = std::f32::consts::PI * 2.0;
/// `PositionAndRotation.ToSByteDivider`.
const TO_SBYTE_DIVIDER: f32 = 1.0 / 127.0;

/// `System.Numerics.Quaternion` (x, y, z, w) with the .NET operator semantics.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Quaternion {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

impl Quaternion {
    pub const IDENTITY: Self = Self {
        x: 0.0,
        y: 0.0,
        z: 0.0,
        w: 1.0,
    };

    /// `Vector3.Transform(value, rotation)`.
    pub fn rotate(self, v: [f32; 3]) -> [f32; 3] {
        let (x2, y2, z2) = (self.x + self.x, self.y + self.y, self.z + self.z);
        let (wx2, wy2, wz2) = (self.w * x2, self.w * y2, self.w * z2);
        let (xx2, xy2, xz2) = (self.x * x2, self.x * y2, self.x * z2);
        let (yy2, yz2, zz2) = (self.y * y2, self.y * z2, self.z * z2);
        [
            v[0] * (1.0 - yy2 - zz2) + v[1] * (xy2 - wz2) + v[2] * (xz2 + wy2),
            v[0] * (xy2 + wz2) + v[1] * (1.0 - xx2 - zz2) + v[2] * (yz2 - wx2),
            v[0] * (xz2 - wy2) + v[1] * (yz2 + wx2) + v[2] * (1.0 - xx2 - yy2),
        ]
    }
}

impl std::ops::Mul for Quaternion {
    type Output = Quaternion;

    /// `Quaternion.Multiply(self, other)` (Hamilton product, .NET formula).
    fn mul(self, q2: Quaternion) -> Quaternion {
        let q1 = self;
        let cx = q1.y * q2.z - q1.z * q2.y;
        let cy = q1.z * q2.x - q1.x * q2.z;
        let cz = q1.x * q2.y - q1.y * q2.x;
        let dot = q1.x * q2.x + q1.y * q2.y + q1.z * q2.z;
        Quaternion {
            x: q1.x * q2.w + q2.x * q1.w + cx,
            y: q1.y * q2.w + q2.y * q1.w + cy,
            z: q1.z * q2.w + q2.z * q1.w + cz,
            w: q1.w * q2.w - dot,
        }
    }
}

/// `PositionAndRotation`: position + (roll, pitch, yaw) rotation in radians.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct PositionAndRotation {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// Roll (`Rotation.X`).
    pub rot_x: f32,
    /// Pitch (`Rotation.Y`).
    pub rot_y: f32,
    /// Yaw (`Rotation.Z`).
    pub rot_z: f32,
}

impl PositionAndRotation {
    pub fn new(x: f32, y: f32, z: f32, rot_x: f32, rot_y: f32, rot_z: f32) -> Self {
        Self {
            x,
            y,
            z,
            rot_x,
            rot_y,
            rot_z,
        }
    }

    /// `ToQuaternion()` (yaw Z, pitch Y, roll X).
    pub fn to_quaternion(&self) -> Quaternion {
        let cy = (self.rot_z * 0.5).cos();
        let sy = (self.rot_z * 0.5).sin();
        let cp = (self.rot_y * 0.5).cos();
        let sp = (self.rot_y * 0.5).sin();
        let cr = (self.rot_x * 0.5).cos();
        let sr = (self.rot_x * 0.5).sin();
        Quaternion {
            w: cr * cp * cy + sr * sp * sy,
            x: sr * cp * cy - cr * sp * sy,
            y: cr * sp * cy + sr * cp * sy,
            z: cr * cp * sy - sr * sp * cy,
        }
    }

    /// `FromQuaternion(q)` -> (roll, pitch, yaw). Note (C#): a gimbal-locked pitch is clamped to
    /// `CopySign(TwoPi, sinP)`, not pi/2; mirrored.
    pub fn rotation_from_quaternion(q: Quaternion) -> (f32, f32, f32) {
        let sin_r_cos_p = 2.0 * (q.w * q.x + q.y * q.z);
        let cos_r_cos_p = 1.0 - 2.0 * (q.x * q.x + q.y * q.y);
        let roll = sin_r_cos_p.atan2(cos_r_cos_p);
        let sin_p = 2.0 * (q.w * q.y - q.z * q.x);
        let pitch = if sin_p.abs() >= 1.0 {
            TWO_PI.copysign(sin_p)
        } else {
            sin_p.asin()
        };
        let sin_y_cos_p = 2.0 * (q.w * q.z + q.x * q.y);
        let cos_y_cos_p = 1.0 - 2.0 * (q.y * q.y + q.z * q.z);
        let yaw = sin_y_cos_p.atan2(cos_y_cos_p);
        (roll, pitch, yaw)
    }

    /// `ToRollPitchYawShorts()`: the quaternion's x/y/z scaled to `short.MaxValue` (truncating
    /// casts, as the C#).
    pub fn roll_pitch_yaw_shorts(&self) -> (i16, i16, i16) {
        let q = self.to_quaternion();
        let f = |v: f32| (v * f32::from(i16::MAX)) as i16;
        (f(q.x), f(q.y), f(q.z))
    }

    /// `ToRollPitchYawSBytes()` (`MathF.Round` = ties to even).
    pub fn roll_pitch_yaw_sbytes(&self) -> (i8, i8, i8) {
        let f = |v: f32| -> i8 { (v / TWO_PI / TO_SBYTE_DIVIDER).round_ties_even() as i8 };
        (f(self.rot_x), f(self.rot_y), f(self.rot_z))
    }

    /// `FromRollPitchYawSBytes`.
    pub fn rotation_from_sbytes(roll: i8, pitch: i8, yaw: i8) -> (f32, f32, f32) {
        let f = |v: i8| f32::from(v) * TO_SBYTE_DIVIDER * TWO_PI;
        (f(roll), f(pitch), f(yaw))
    }

    /// `Transform.GetWorldPosition()` for a child: the parent's world value with the child's
    /// local position (scaled by the parent's `Scale`) rotated into it, and the rotations
    /// multiplied (`parent * local`).
    pub fn compose(parent_world: &PositionAndRotation, parent_scale: f32, local: &Self) -> Self {
        let pq = parent_world.to_quaternion();
        let offset = pq.rotate([
            local.x * parent_scale,
            local.y * parent_scale,
            local.z * parent_scale,
        ]);
        let wq = pq * local.to_quaternion();
        let (rx, ry, rz) = Self::rotation_from_quaternion(wq);
        Self {
            x: parent_world.x + offset[0],
            y: parent_world.y + offset[1],
            z: parent_world.z + offset[2],
            rot_x: rx,
            rot_y: ry,
            rot_z: rz,
        }
    }

    /// `MathUtil.CalculateDistance(a, b, includeZAxis)`.
    pub fn distance_to(&self, other: &Self, include_z: bool) -> f32 {
        let dx = other.x - self.x;
        let dy = other.y - self.y;
        if include_z {
            let dz = other.z - self.z;
            (dx * dx + dy * dy + dz * dz).sqrt()
        } else {
            (dx * dx + dy * dy).sqrt()
        }
    }
}

/// `Transform` (+ its `PositionAndRotation Local`). Rotation is (roll, pitch, yaw) in radians.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Transform {
    pub instance_id: u32,
    pub world_id: u32,
    pub zone_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub rot_x: f32,
    pub rot_y: f32,
    pub rot_z: f32,
    /// `Parent?.GameObject.ObjId` (0 = no parent: the values above are world coordinates).
    /// Maintained by `WorldInstance::attach_child` / `detach_child`.
    pub parent_obj_id: u32,
}

impl Transform {
    /// `Local` as a [`PositionAndRotation`].
    pub fn local(&self) -> PositionAndRotation {
        PositionAndRotation::new(self.x, self.y, self.z, self.rot_x, self.rot_y, self.rot_z)
    }

    /// `PositionAndRotation.ToRollPitchYawSBytes()` of `Local`.
    pub fn roll_pitch_yaw_sbytes(&self) -> (i8, i8, i8) {
        self.local().roll_pitch_yaw_sbytes()
    }

    /// `PositionAndRotation.ToRollPitchYawShorts()` of `Local`.
    pub fn roll_pitch_yaw_shorts(&self) -> (i16, i16, i16) {
        self.local().roll_pitch_yaw_shorts()
    }

    /// `Local.SetPosition(x, y, z, roll, pitch, yaw)`.
    pub fn set_position(&mut self, x: f32, y: f32, z: f32, rx: f32, ry: f32, rz: f32) {
        self.x = x;
        self.y = y;
        self.z = z;
        self.rot_x = rx;
        self.rot_y = ry;
        self.rot_z = rz;
    }

    /// `Local.SetPosition(x, y, z)` (rotation kept).
    pub fn set_xyz(&mut self, x: f32, y: f32, z: f32) {
        self.x = x;
        self.y = y;
        self.z = z;
    }

    /// `Local.SetHeight(z)`.
    pub fn set_height(&mut self, z: f32) {
        self.z = z;
    }

    pub fn has_parent(&self) -> bool {
        self.parent_obj_id != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn convert_long_matches_csharp() {
        assert_eq!(convert_long(1.0), 4096i64 << 32);
        assert_eq!(convert_long(-0.5), -2048i64 << 32);
    }

    #[test]
    fn rotation_sbytes_and_shorts() {
        let t = Transform {
            rot_z: std::f32::consts::PI,
            ..Transform::default()
        };
        assert_eq!(t.roll_pitch_yaw_sbytes(), (0, 0, 64));
        // yaw pi -> q = (0, 0, sin(pi/2), cos(pi/2)) = (0, 0, 1, ~0)
        assert_eq!(t.roll_pitch_yaw_shorts(), (0, 0, i16::MAX));
        assert_eq!(Transform::default().roll_pitch_yaw_shorts(), (0, 0, 0));
    }

    #[test]
    fn compose_rotates_child_offset() {
        let parent =
            PositionAndRotation::new(100.0, 200.0, 10.0, 0.0, 0.0, std::f32::consts::FRAC_PI_2);
        let child = PositionAndRotation::new(1.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        let w = PositionAndRotation::compose(&parent, 2.0, &child);
        assert!((w.x - 100.0).abs() < 1e-4, "{w:?}");
        assert!((w.y - 202.0).abs() < 1e-4, "{w:?}");
        assert!((w.z - 10.0).abs() < 1e-4);
        assert!((w.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-4);
        // Unrotated parent: plain translation.
        let p0 = PositionAndRotation::new(5.0, 5.0, 5.0, 0.0, 0.0, 0.0);
        let w0 = PositionAndRotation::compose(
            &p0,
            1.0,
            &PositionAndRotation::new(1.0, 2.0, 3.0, 0.0, 0.0, 0.0),
        );
        assert_eq!((w0.x, w0.y, w0.z), (6.0, 7.0, 8.0));
    }

    #[test]
    fn quaternion_roundtrip() {
        let p = PositionAndRotation::new(0.0, 0.0, 0.0, 0.3, -0.2, 1.1);
        let (r, pi, y) = PositionAndRotation::rotation_from_quaternion(p.to_quaternion());
        assert!((r - 0.3).abs() < 1e-5 && (pi + 0.2).abs() < 1e-5 && (y - 1.1).abs() < 1e-5);
    }
}
