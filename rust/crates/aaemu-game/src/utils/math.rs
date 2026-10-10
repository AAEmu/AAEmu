//! `Utils/MathUtil` (complete, on plain vectors: `[x, y, z]`, quaternions `(x, y, z, w)`) with
//! the C# float / double mix of every function. Several subsystems keep local copies of single
//! helpers; this module is the reference port.

use super::numeric::{deg_to_rad_f32, rad_to_deg, rad_to_deg_f32};

/// `System.Numerics.Quaternion` (x, y, z, w).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Quat {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

impl Quat {
    pub const fn new(x: f32, y: f32, z: f32, w: f32) -> Self {
        Self { x, y, z, w }
    }

    /// `new Quaternion(Vector3 vectorPart, float scalarPart)`.
    pub const fn from_parts(v: [f32; 3], w: f32) -> Self {
        Self::new(v[0], v[1], v[2], w)
    }

    /// `Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)`.
    pub fn from_yaw_pitch_roll(yaw: f32, pitch: f32, roll: f32) -> Self {
        let (sr, cr) = (roll * 0.5).sin_cos();
        let (sp, cp) = (pitch * 0.5).sin_cos();
        let (sy, cy) = (yaw * 0.5).sin_cos();
        Self {
            x: cy * sp * cr + sy * cp * sr,
            y: sy * cp * cr - cy * sp * sr,
            z: cy * cp * sr - sy * sp * cr,
            w: cy * cp * cr + sy * sp * sr,
        }
    }

    /// `Quaternion.Conjugate(value)`.
    pub fn conjugate(self) -> Self {
        Self::new(-self.x, -self.y, -self.z, self.w)
    }
}

impl std::ops::Mul for Quat {
    type Output = Quat;

    /// `Quaternion * Quaternion` (System.Numerics formula).
    fn mul(self, b: Quat) -> Quat {
        let (q1x, q1y, q1z, q1w) = (self.x, self.y, self.z, self.w);
        let (q2x, q2y, q2z, q2w) = (b.x, b.y, b.z, b.w);
        let cx = q1y * q2z - q1z * q2y;
        let cy = q1z * q2x - q1x * q2z;
        let cz = q1x * q2y - q1y * q2x;
        let dot = q1x * q2x + q1y * q2y + q1z * q2z;
        Quat {
            x: q1x * q2w + q2x * q1w + cx,
            y: q1y * q2w + q2y * q1w + cy,
            z: q1z * q2w + q2z * q1w + cz,
            w: q1w * q2w - dot,
        }
    }
}

/// `HalfPi`.
pub const HALF_PI: f32 = std::f64::consts::FRAC_PI_2 as f32;

/// `RotatePoint3D(point, xRotation, yRotation, zRotation)`. C# builds the quaternions with the
/// (vector, scalar) constructor (not axis / angle) and discards the conjugate, both mirrored.
pub fn rotate_point_3d(
    point: [f32; 3],
    x_rotation: f32,
    y_rotation: f32,
    z_rotation: f32,
) -> [f32; 3] {
    let xq = Quat::from_parts([1.0, 0.0, 0.0], x_rotation);
    let yq = Quat::from_parts([0.0, 1.0, 0.0], y_rotation);
    let zq = Quat::from_parts([0.0, 0.0, 1.0], z_rotation);
    let pq = Quat::new(point[0], point[1], point[2], 0.0);
    let xyz = xq * yq * zq;
    let xyzc = Quat::new(xyz.x, xyz.y, xyz.z, xyz.w);
    let _ = xyzc.conjugate(); // result discarded in C#
    let q = xyz * pq * xyzc;
    [q.x, q.y, q.z]
}

/// `CalculateAngleFrom(obj1X, obj1Y, obj2X, obj2Y)`: degrees, `% 360`.
pub fn calculate_angle_from(x1: f32, y1: f32, x2: f32, y2: f32) -> f64 {
    let angle = rad_to_deg(f64::from(y2 - y1).atan2(f64::from(x2 - x1)));
    angle % f64::from(360f32)
}

/// `CalculateAngleFrom(GameObject obj1, GameObject obj2)` with world positions and obj1's
/// world yaw (radians): 0 = in front, clockwise negative.
pub fn calculate_angle_from_objects(p1: [f32; 3], rot_z1: f32, p2: [f32; 3]) -> f64 {
    let angle_to_target = calculate_angle_from(p1[0], p1[1], p2[0], p2[1]) - 90.0;
    angle_to_target - f64::from(rad_to_deg_f32(rot_z1))
}

/// `CalculateDirection(obj1, obj2)`: radians.
pub fn calculate_direction(p1: [f32; 3], p2: [f32; 3]) -> f64 {
    f64::from(p2[1] - p1[1]).atan2(f64::from(p2[0] - p1[0]))
}

/// `ConvertSbyteDirectionToDegree(direction)`.
pub fn convert_sbyte_direction_to_degree(direction: i8) -> f64 {
    let angle = f32::from(direction) * (360f32 / 128.0);
    f64::from(angle % 360f32)
}

/// `(sbyte)double`: truncation toward zero, wrapping like the unchecked C# conversion of the
/// truncated integer.
fn to_sbyte(v: f64) -> i8 {
    (v.trunc() as i64) as i8
}

/// `ConvertDegreeToSByteDirection(degree)`.
pub fn convert_degree_to_sbyte_direction(mut degree: f64) -> i8 {
    if degree < 0.0 {
        degree += 360.0;
    }
    let step = f64::from(360f32 / 128.0);
    let mut res = to_sbyte(degree / step);
    if res > 85 {
        res = to_sbyte((degree - 360.0) / step);
    }
    res
}

/// `ConvertDegreeToDoodadDirection(degree)`.
pub fn convert_degree_to_doodad_direction(mut degree: f64) -> i8 {
    while degree < 0.0 {
        degree += 360.0;
    }
    if degree > 90.0 && degree <= 180.0 {
        return to_sbyte(-((degree - 90.0) / 90.0 * 37.0 + 90.0));
    }
    if degree > 180.0 {
        return to_sbyte(-((degree - 270.0) / 90.0 * 37.0 - 90.0));
    }
    to_sbyte(-degree)
}

/// `ClampDegAngle(angle)`: into [-180, 180].
pub fn clamp_deg_angle(mut angle: f64) -> f64 {
    while angle > 180.0 {
        angle -= 360.0;
    }
    while angle < -180.0 {
        angle += 360.0;
    }
    angle
}

/// `IsFront(obj1, obj2, fovScale)`.
pub fn is_front(p1: [f32; 3], rot_z1: f32, p2: [f32; 3], fov_scale: f64) -> bool {
    if fov_scale >= 2.0 {
        return true;
    }
    if fov_scale <= 0.0 {
        return false;
    }
    let degree = clamp_deg_angle(calculate_angle_from_objects(p1, rot_z1, p2));
    degree >= -90.0 * fov_scale && degree <= 90.0 * fov_scale
}

/// `ConvertDirectionToRadian(direction)`.
pub fn convert_direction_to_radian(direction: i8) -> f64 {
    convert_sbyte_direction_to_degree(direction) * std::f64::consts::PI / 180.0
}

/// `GetYawPitchRollFromQuat(quat)`: returns (roll, pitch, yaw) like the C# tuple.
pub fn get_yaw_pitch_roll_from_quat(q: Quat) -> (f32, f32, f32) {
    let roll = f64::from(2.0 * (q.w * q.x + q.y * q.z))
        .atan2(f64::from(1.0 - 2.0 * (q.x * q.x + q.y * q.y))) as f32;
    let sinp = 2.0 * (q.w * q.y - q.z * q.x);
    let pitch = if sinp.abs() >= 1.0 {
        std::f64::consts::FRAC_PI_2.copysign(f64::from(sinp)) as f32
    } else {
        f64::from(sinp).asin() as f32
    };
    let yaw = f64::from(2.0 * (q.w * q.z + q.x * q.y))
        .atan2(f64::from(1.0 - 2.0 * (q.y * q.y + q.z * q.z))) as f32;
    (roll, pitch, yaw)
}

/// The slave rotation scale (`0.00003052f`).
const SLAVE_ROT_SCALE: f32 = 0.000_030_52;

/// `GetSlaveRotationInDegrees(rotX, rotY, rotZ)` (returns radians, despite the name).
pub fn get_slave_rotation_in_degrees(rot_x: i16, rot_y: i16, rot_z: i16) -> (f32, f32, f32) {
    let qx = f32::from(rot_x) * SLAVE_ROT_SCALE;
    let qy = f32::from(rot_y) * SLAVE_ROT_SCALE;
    let qz = f32::from(rot_z) * SLAVE_ROT_SCALE;
    let norm = qx * qx + qy * qy + qz * qz;
    let qw = if f64::from(norm) < 0.99750 {
        (1.0 - f64::from(norm)).sqrt() as f32
    } else {
        0.0
    };
    get_yaw_pitch_roll_from_quat(Quat::new(qx, qy, qz, qw))
}

/// `(short)float`: truncation, wrapping like the unchecked conversion.
fn to_short(v: f32) -> i16 {
    (v.trunc() as i64) as i16
}

/// `GetSlaveRotationFromDegrees(degX, degY, degZ)`.
pub fn get_slave_rotation_from_degrees(deg_x: f32, deg_y: f32, deg_z: f32) -> (i16, i16, i16) {
    let q = Quat::from_yaw_pitch_roll(deg_z, deg_x, deg_y);
    get_slave_rotation_from_quat(q)
}

/// `GetSlaveRotationFromQuat(quaternion)`: (X, Z, Y) scaled.
pub fn get_slave_rotation_from_quat(q: Quat) -> (i16, i16, i16) {
    (
        to_short(q.x / SLAVE_ROT_SCALE),
        to_short(q.z / SLAVE_ROT_SCALE),
        to_short(q.y / SLAVE_ROT_SCALE),
    )
}

/// `AddDistanceToFrontDeg(distance, x, y, deg)`.
pub fn add_distance_to_front_deg(distance: f32, x: f32, y: f32, deg: f32) -> (f32, f32) {
    let rad = f64::from(deg) * std::f64::consts::PI / 180.0;
    (
        distance * (rad.cos() as f32) + x,
        distance * (rad.sin() as f32) + y,
    )
}

/// `AddDistanceToFront(distance, x, y, rotZRad)`.
pub fn add_distance_to_front(distance: f32, x: f32, y: f32, rot_z_rad: f32) -> (f32, f32) {
    (
        distance * (f64::from(rot_z_rad).cos() as f32) + x,
        distance * (f64::from(rot_z_rad).sin() as f32) + y,
    )
}

/// `AddDistanceToRight(distance, x, y, sbyte rotZ)`.
pub fn add_distance_to_right_sbyte(distance: f32, x: f32, y: f32, rot_z: i8) -> (f32, f32) {
    let rad = convert_direction_to_radian(rot_z) - std::f64::consts::FRAC_PI_2;
    (
        distance * (rad.cos() as f32) + x,
        distance * (rad.sin() as f32) + y,
    )
}

/// `AddDistanceToRight(distance, x, y, float rotZRad)`.
pub fn add_distance_to_right(distance: f32, x: f32, y: f32, rot_z_rad: f32) -> (f32, f32) {
    let rad = f64::from(rot_z_rad) - std::f64::consts::FRAC_PI_2;
    (
        distance * (rad.cos() as f32) + x,
        distance * (rad.sin() as f32) + y,
    )
}

#[allow(clippy::too_many_arguments)]
fn cuboid(
    length: f32,
    width: f32,
    x: f32,
    y: f32,
    cf: f32,
    sf: f32,
    cr: f32,
    sr: f32,
) -> [(f32, f32); 4] {
    let p1 = (width * cf + x, width * sf + y);
    let p1 = (length * cr + p1.0, length * sr + p1.1);
    let p2 = (width * cf + x, width * sf + y);
    let p2 = (-length * cr + p2.0, -length * sr + p2.1);
    let p3 = (-width * cf + x, -width * sf + y);
    let p3 = (-length * cr + p3.0, -length * sr + p3.1);
    let p4 = (-width * cf + x, -width * sf + y);
    let p4 = (length * cr + p4.0, length * sr + p4.1);
    [p1, p2, p3, p4]
}

/// `GetCuboidVertices(length, width, x, y, float rotationZ)`.
pub fn get_cuboid_vertices(
    length: f32,
    width: f32,
    x: f32,
    y: f32,
    rotation_z: f32,
) -> [(f32, f32); 4] {
    let rad_front = rotation_z - std::f32::consts::PI / 2.0;
    let rad_right = rotation_z;
    cuboid(
        length,
        width,
        x,
        y,
        rad_front.cos(),
        rad_front.sin(),
        rad_right.cos(),
        rad_right.sin(),
    )
}

/// `GetCuboidVertices(length, width, x, y, sbyte rotZ)` (obsolete in C#).
pub fn get_cuboid_vertices_sbyte(
    length: f32,
    width: f32,
    x: f32,
    y: f32,
    rot_z: i8,
) -> [(f32, f32); 4] {
    let rad_front = convert_direction_to_radian(rot_z);
    let rad_right = convert_direction_to_radian(rot_z) - std::f64::consts::FRAC_PI_2;
    cuboid(
        length,
        width,
        x,
        y,
        rad_front.cos() as f32,
        rad_front.sin() as f32,
        rad_right.cos() as f32,
        rad_right.sin() as f32,
    )
}

fn sign(p1: (f32, f32), p2: (f32, f32), p3: (f32, f32)) -> f32 {
    (p1.0 - p3.0) * (p2.1 - p3.1) - (p2.0 - p3.0) * (p1.1 - p3.1)
}

/// `PointInTriangle(point, v1, v2, v3)`.
pub fn point_in_triangle(
    point: (f32, f32),
    v1: (f32, f32),
    v2: (f32, f32),
    v3: (f32, f32),
) -> bool {
    let b1 = sign(point, v1, v2) < 0.0;
    let b2 = sign(point, v2, v3) < 0.0;
    let b3 = sign(point, v3, v1) < 0.0;
    b1 == b2 && b2 == b3
}

/// `ConvertRadianToDirection(radian)`.
pub fn convert_radian_to_direction(radian: f64) -> i8 {
    convert_degree_to_sbyte_direction(rad_to_deg(radian))
}

/// `DistanceSqVectors(loc, loc2, includeZAxis)` (float differences, double sums).
pub fn distance_sq_vectors(a: [f32; 3], b: [f32; 3], include_z: bool) -> f32 {
    let dx = f64::from(a[0] - b[0]);
    let dy = f64::from(a[1] - b[1]);
    if !include_z {
        return (dx * dx + dy * dy) as f32;
    }
    let dz = f64::from(a[2] - b[2]);
    (dx * dx + dy * dy + dz * dz) as f32
}

/// `CalculateDistance(loc, loc2, includeZAxis)` (Vector3 / WorldSpawnPosition / GameObject
/// world positions).
pub fn calculate_distance(a: [f32; 3], b: [f32; 3], include_z: bool) -> f32 {
    let dx = f64::from(a[0] - b[0]);
    let dy = f64::from(a[1] - b[1]);
    if include_z {
        let dz = f64::from(a[2] - b[2]);
        return (dx * dx + dy * dy + dz * dz).sqrt() as f32;
    }
    (dx * dx + dy * dy).sqrt() as f32
}

/// `GetDistance(v1, v2, point3d)`; the C# 3D variant adds the Y difference twice (mirrored).
pub fn get_distance(v1: [f32; 3], v2: [f32; 3], point3d: bool) -> f32 {
    let sx = (v1[0] - v2[0]).powi(2);
    let sy = (v1[1] - v2[1]).powi(2);
    if point3d {
        (sx + sy + sy).sqrt()
    } else {
        (sx + sy).sqrt()
    }
}

/// `GetVectorFromQuat(quat)`.
pub fn get_vector_from_quat(q: Quat) -> [f32; 3] {
    let sqw = f64::from(q.w * q.w);
    let sqx = f64::from(q.x * q.x);
    let sqy = f64::from(q.y * q.y);
    let sqz = f64::from(q.z * q.z);
    let rot_x = (2.0 * f64::from(q.x * q.y + q.z * q.w)).atan2(sqx - sqy - sqz + sqw) as f32;
    let rot_y = (2.0 * f64::from(q.y * q.z + q.x * q.w)).atan2(-sqx - sqy + sqz + sqw) as f32;
    let rot_z = (-2.0 * f64::from(q.x * q.z - q.y * q.w) / (sqx + sqy + sqz + sqw)).asin() as f32;
    [rot_x, rot_y, rot_z]
}

/// `ConvertRadianToDirectionShort(radian)` (with the C# 5-digit pi).
#[allow(clippy::approx_constant)]
pub fn convert_radian_to_direction_short(mut radian: f64) -> Quat {
    const PI: f64 = 3.14159;
    const PI2: f64 = 3.14159 * 2.0;
    const PI05: f64 = 3.14159 * 0.5;
    if radian < 0.0 {
        radian += PI2;
    }
    radian -= PI05;
    if radian > PI {
        radian -= PI2;
    }
    Quat::from_yaw_pitch_roll(radian as f32, 0.0, 0.0)
}

/// `DegToRad` for floats re-exported for callers of this module.
pub fn deg_to_rad(v: f32) -> f32 {
    deg_to_rad_f32(v)
}

#[cfg(test)]
mod tests {
    //! Port of `AAEmu.UnitTests/Game/Utils/MathUtilTests.cs` + extras.
    use super::*;
    use crate::utils::numeric::deg_to_rad_f32;

    #[test]
    fn calculate_angle_from_cases() {
        assert_eq!(calculate_angle_from(0.0, 0.0, 0.0, 0.0), 0.0);
        assert_eq!(calculate_angle_from(0.0, 0.0, 10.0, 0.0), 0.0);
        assert!((calculate_angle_from(0.0, 0.0, 0.0, 10.0) - 90.0).abs() < 0.00001);
        assert!((calculate_angle_from(0.0, 0.0, -10.0, 0.0) - 180.0).abs() < 0.00001);
    }

    #[test]
    fn degree_to_sbyte_direction() {
        for (deg, expected) in [(0.0, 0), (90.0, 32), (180.0, 64), (270.0, -32), (359.0, 0)] {
            assert_eq!(convert_degree_to_sbyte_direction(deg), expected, "{deg}");
        }
    }

    #[test]
    fn sbyte_direction_to_degree() {
        for (dir, expected) in [
            (0, 0.0),
            (28, 78.75),
            (56, 157.5),
            (85, 239.0625),
            (113, 317.8125),
        ] {
            assert!((convert_sbyte_direction_to_degree(dir) - expected).abs() < 0.00001);
        }
    }

    #[test]
    #[allow(clippy::approx_constant)]
    fn rad_deg_conversions() {
        for (rad, deg) in [
            (0.0f32, 0.0f32),
            (1.0, 57.29578),
            (3.14159, 180.0),
            (6.28318, 360.0),
        ] {
            assert!((rad_to_deg_f32(rad) - deg).abs() < 0.005);
            assert!((deg_to_rad_f32(deg) - rad).abs() < 0.005);
        }
    }

    #[test]
    fn misc_helpers() {
        assert_eq!(clamp_deg_angle(270.0), -90.0);
        assert_eq!(clamp_deg_angle(-190.0), 170.0);
        assert!(point_in_triangle(
            (0.1, 0.1),
            (0.0, 0.0),
            (1.0, 0.0),
            (0.0, 1.0)
        ));
        assert!(!point_in_triangle(
            (1.0, 1.0),
            (0.0, 0.0),
            (1.0, 0.0),
            (0.0, 1.0)
        ));
        assert_eq!(
            calculate_distance([0.0, 0.0, 0.0], [3.0, 4.0, 12.0], true),
            13.0
        );
        assert_eq!(
            calculate_distance([0.0, 0.0, 0.0], [3.0, 4.0, 12.0], false),
            5.0
        );
        assert_eq!(distance_sq_vectors([0.0; 3], [3.0, 4.0, 0.0], false), 25.0);
        // The C# 3D GetDistance uses Y twice.
        assert_eq!(get_distance([0.0; 3], [0.0, 1.0, 5.0], true), 2f32.sqrt());
        assert_eq!(convert_degree_to_doodad_direction(45.0), -45);
        assert_eq!(convert_degree_to_doodad_direction(180.0), -127);
        let (x, y) = add_distance_to_front(10.0, 1.0, 1.0, 0.0);
        assert_eq!((x, y), (11.0, 1.0));
        let q = Quat::from_yaw_pitch_roll(0.5, 0.0, 0.0);
        let (_, _, yaw) = get_yaw_pitch_roll_from_quat(Quat::new(q.x, q.z, q.y, q.w));
        assert!((yaw - 0.5).abs() < 1e-5);
        let (rx, ry, rz) = get_slave_rotation_from_degrees(0.0, 0.0, 0.0);
        assert_eq!((rx, ry, rz), (0, 0, 0));
        assert_eq!(get_slave_rotation_in_degrees(0, 0, 0), (0.0, 0.0, 0.0));
        assert_eq!(rotate_point_3d([0.0; 3], 0.0, 0.0, 0.0), [0.0, 0.0, 0.0]);
        assert_eq!(convert_radian_to_direction(std::f64::consts::PI / 2.0), 32);
    }
}
