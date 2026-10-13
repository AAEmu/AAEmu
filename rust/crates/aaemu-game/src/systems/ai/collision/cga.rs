//! The original 1.2 CGA root tracks used by physicalized housing AnimObjects.
//!
//! Derived from the original `CryAnimation.dll`: controller 0x826 loader 0x315FE580,
//! TCB conversion 0x315E2220, cumulative axis-angle rotation 0x315E5480, and
//! controller output 0x3162BAE0. No articulated skeleton or compressed CAF is read here.

use aaemu_physics::math::{Quat, Vec3};

use super::cgf::{CgfError, CgfFile};

const NODE: u32 = 0xCCCC_000B;
const CONTROLLER: u32 = 0xCCCC_000D;
const TIMING: u32 = 0xCCCC_000E;
const NO_CONTROLLER: u32 = u32::MAX;
const MAX_NODES: usize = 128;
const MAX_KEYS: usize = 4096;

fn unity_scale(scale: Vec3) -> bool {
    // Original main tower CGA/ANM roots export1.0000001192 on X/Y after rotation
    // decomposition. This is float roundoff, not an authored scale transform.
    let tolerance = 2.0 * f32::EPSILON;
    (scale.x - 1.0).abs() <= tolerance
        && (scale.y - 1.0).abs() <= tolerance
        && (scale.z - 1.0).abs() <= tolerance
}

#[derive(Debug, Clone, PartialEq, thiserror::Error)]
pub enum CgaError {
    #[error(transparent)]
    Cgf(#[from] CgfError),
    #[error("unsupported CGA root track: {0}")]
    Unsupported(&'static str),
    #[error("invalid CGA root track: {0}")]
    Invalid(&'static str),
}

#[derive(Debug, Clone)]
pub struct Node {
    pub id: i32,
    pub name: String,
    pub object_id: i32,
    pub parent_id: i32,
    pub position: Vec3,
    pub quaternion: Quat,
    pub scale: Vec3,
    pub controllers: [u32; 3],
}

fn word(data: &[u8], offset: usize) -> Result<u32, CgaError> {
    let bytes = data
        .get(offset..offset + 4)
        .ok_or(CgfError::Truncated(offset))?;
    Ok(u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]))
}

fn scalar(data: &[u8], offset: usize) -> Result<f32, CgaError> {
    let value = f32::from_bits(word(data, offset)?);
    if !value.is_finite() {
        return Err(CgaError::Invalid("nonfinite value"));
    }
    Ok(value)
}

fn vector(data: &[u8], offset: usize) -> Result<Vec3, CgaError> {
    Ok(Vec3::new(
        scalar(data, offset)?,
        scalar(data, offset + 4)?,
        scalar(data, offset + 8)?,
    ))
}

/// Node 0x823 offsets are relative to the body after the copied 16-byte chunk header.
pub fn nodes(file: &CgfFile<'_>) -> Result<Vec<Node>, CgaError> {
    let mut result = Vec::new();
    for chunk in file.chunks.iter().filter(|chunk| chunk.ctype == NODE) {
        if chunk.version != 0x823 {
            return Err(CgaError::Unsupported("node version"));
        }
        if result.len() >= MAX_NODES {
            return Err(CgaError::Invalid("too many nodes"));
        }
        let body = file.body(chunk)?;
        if body.len() < 204 {
            return Err(CgfError::Truncated(chunk.offset as usize + 16 + 204).into());
        }
        let end = body[..64].iter().position(|&byte| byte == 0).unwrap_or(64);
        let quaternion = Quat::new(
            scalar(body, 160)?,
            scalar(body, 164)?,
            scalar(body, 168)?,
            scalar(body, 172)?,
        );
        if quaternion.len2_xwyz() < 0.5 || quaternion.len2_xwyz() > 1.5 {
            return Err(CgaError::Invalid("node quaternion"));
        }
        if result.iter().any(|node: &Node| node.id == chunk.id) {
            return Err(CgaError::Invalid("duplicate node id"));
        }
        result.push(Node {
            id: chunk.id,
            name: String::from_utf8_lossy(&body[..end]).into_owned(),
            object_id: word(body, 64)? as i32,
            parent_id: word(body, 68)? as i32,
            position: vector(body, 148)? * 0.01,
            // Native CGA bone defaults and TCB outputs conjugate the authored rotation.
            quaternion: quaternion.conj().normalized_xwyz(),
            scale: vector(body, 176)?,
            controllers: [word(body, 188)?, word(body, 192)?, word(body, 196)?],
        });
    }
    Ok(result)
}

#[derive(Debug, Clone, Copy)]
pub struct PositionKey {
    pub frame: f32,
    pub position: Vec3,
    raw_position: Vec3,
    tangent_in: Vec3,
    tangent_out: Vec3,
}

#[derive(Debug, Clone, Copy)]
pub struct RotationKey {
    pub frame: f32,
    /// Cumulative authored rotation, before native output conjugation.
    pub quaternion: Quat,
    control_in: Quat,
    control_out: Quat,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct RootPose {
    pub position: Vec3,
    pub quaternion: Quat,
}

/// Uncompressed original 1.2 root tracks. Only the verified untuned TCB format is accepted.
#[derive(Debug, Clone)]
pub struct RootClip {
    pub root: Node,
    pub start_frame: f32,
    pub end_frame: f32,
    pub positions: Vec<PositionKey>,
    pub rotations: Vec<RotationKey>,
}

/// A verified single-proxy rigid CGA hierarchy. The animated root replaces its authored
/// default transform; static child transforms compose locally beneath it. Native 1.2
/// GetBonePos/GetBoneDir and physical rays confirm this for both origin guard tower CGAs.
#[derive(Debug, Clone)]
pub struct RootAnimation {
    pub clip: RootClip,
    pub proxy_position: Vec3,
    pub proxy_quaternion: Quat,
}

impl RootAnimation {
    pub fn read(model: &[u8], animation: &[u8]) -> Result<Self, CgaError> {
        let file = CgfFile::parse(model)?;
        let model_nodes = nodes(&file)?;
        let clip = RootClip::read(animation)?;
        let roots = model_nodes
            .iter()
            .filter(|node| node.parent_id < 0 && node.name == clip.root.name)
            .collect::<Vec<_>>();
        if roots.len() != 1 {
            return Err(CgaError::Invalid("animation root binding"));
        }
        let root = roots[0];
        let physics = super::cgf::physics_blobs(model)?;
        let proxy = physics.first().ok_or(CgaError::Invalid("missing proxy"))?;
        if physics.iter().any(|part| part.node != proxy.node) {
            return Err(CgaError::Unsupported("multiple physical nodes"));
        }
        let proxies = model_nodes
            .iter()
            .filter(|node| node.name == proxy.node)
            .collect::<Vec<_>>();
        if proxies.len() != 1 {
            return Err(CgaError::Invalid("proxy node binding"));
        }
        let mut current = proxies[0];
        let mut position = Vec3::ZERO;
        let mut quaternion = Quat::IDENTITY;
        let mut visited = Vec::new();
        while current.id != root.id {
            if visited.contains(&current.id) {
                return Err(CgaError::Invalid("cyclic node hierarchy"));
            }
            visited.push(current.id);
            if !unity_scale(current.scale) {
                return Err(CgaError::Unsupported("node scale"));
            }
            position = current.position + current.quaternion.rotate(position);
            quaternion = current.quaternion * quaternion;
            current = model_nodes
                .iter()
                .find(|node| node.id == current.parent_id)
                .ok_or(CgaError::Invalid("orphan proxy node"))?;
        }
        if !unity_scale(root.scale) || !unity_scale(clip.root.scale) {
            return Err(CgaError::Unsupported("root scale"));
        }
        Ok(Self {
            clip,
            proxy_position: position,
            proxy_quaternion: quaternion,
        })
    }

    pub fn sample(&self, elapsed_seconds: f32, looped: bool, speed: f32) -> Option<RootPose> {
        if !elapsed_seconds.is_finite()
            || elapsed_seconds < 0.0
            || !speed.is_finite()
            || speed <= 0.0
        {
            return None;
        }
        let duration = self.clip.duration_seconds();
        let elapsed = elapsed_seconds * speed;
        let elapsed = if looped && duration > 0.0 {
            elapsed % duration
        } else {
            elapsed.min(duration)
        };
        let root = self.clip.sample(self.clip.start_frame + elapsed * 30.0)?;
        Some(RootPose {
            position: root.position + root.quaternion.rotate(self.proxy_position),
            quaternion: root.quaternion * self.proxy_quaternion,
        })
    }
}

impl RootClip {
    pub fn read(data: &[u8]) -> Result<Self, CgaError> {
        // Bound the animation table before the shared CGF reader allocates it.
        if data.len() < 20 || &data[..6] != b"CryTek" {
            return Err(CgfError::Signature.into());
        }
        let table = word(data, 16)? as usize;
        if word(data, table)? > 4096 {
            return Err(CgaError::Invalid("too many chunks"));
        }
        let file = CgfFile::parse(data)?;
        let parsed_nodes = nodes(&file)?;
        let roots = parsed_nodes
            .iter()
            .filter(|node| node.parent_id < 0)
            .collect::<Vec<_>>();
        if roots.len() != 1 {
            return Err(CgaError::Unsupported("root node count"));
        }
        let root = roots[0].clone();
        if root.controllers[2] != NO_CONTROLLER
            || parsed_nodes.iter().any(|node| {
                node.id != root.id && node.controllers.iter().any(|&id| id != NO_CONTROLLER)
            })
        {
            return Err(CgaError::Unsupported("child or scale animation"));
        }
        let timing = file
            .chunks
            .iter()
            .find(|chunk| chunk.ctype == TIMING)
            .ok_or(CgaError::Invalid("missing timing"))?;
        if timing.version != 0x918 {
            return Err(CgaError::Unsupported("timing version"));
        }
        let timing = file.body(timing)?;
        if word(timing, 4)? != 160 {
            return Err(CgaError::Unsupported("ticks per frame"));
        }
        if (scalar(timing, 0)? * 160.0 - 1.0 / 30.0).abs() > 1e-6 {
            return Err(CgaError::Unsupported("frame rate"));
        }
        let start_frame = word(timing, 40)? as i32 as f32;
        let end_frame = word(timing, 44)? as i32 as f32;
        if start_frame < 0.0 || end_frame < start_frame {
            return Err(CgaError::Invalid("frame range"));
        }
        let mut result = Self {
            root,
            start_frame,
            end_frame,
            positions: Vec::new(),
            rotations: Vec::new(),
        };
        for channel in 0..2 {
            let id = result.root.controllers[channel];
            if id == NO_CONTROLLER {
                continue;
            }
            let chunk = file
                .chunk(id as i32)
                .ok_or(CgaError::Invalid("missing controller"))?;
            if chunk.ctype != CONTROLLER || chunk.version != 0x826 {
                return Err(CgaError::Unsupported("controller version"));
            }
            let body = file.body(chunk)?;
            let expected_type = if channel == 0 { 9 } else { 10 };
            if word(body, 0)? != expected_type || word(body, 8)? != 0 {
                return Err(CgaError::Unsupported("controller type or flags"));
            }
            let count = word(body, 4)? as usize;
            if count == 0 || count > MAX_KEYS {
                return Err(CgaError::Invalid("key count"));
            }
            let stride = if channel == 0 { 36 } else { 40 };
            if body.len() != 16 + count * stride {
                return Err(CgaError::Invalid("controller length"));
            }
            let mut previous_tick = -1;
            let mut cumulative = Quat::IDENTITY;
            for index in 0..count {
                let offset = 16 + index * stride;
                let tick = word(body, offset)? as i32;
                if tick < 0 || tick <= previous_tick {
                    return Err(CgaError::Invalid("key order"));
                }
                previous_tick = tick;
                let frame = tick as f32 * 0.00625;
                // Native builder reads five tuning floats after position/axis-angle.
                let tuning = offset + if channel == 0 { 16 } else { 20 };
                for field in 0..5 {
                    if scalar(body, tuning + field * 4)? != 0.0 {
                        return Err(CgaError::Unsupported("spline tuning"));
                    }
                }
                let value = vector(body, offset + 4)?;
                if channel == 0 {
                    result.positions.push(PositionKey {
                        frame,
                        position: value * 0.01,
                        raw_position: value,
                        tangent_in: Vec3::ZERO,
                        tangent_out: Vec3::ZERO,
                    });
                } else {
                    let angle = scalar(body, offset + 16)?;
                    // Native 0x315E8510 has a separate multi-turn branch. Original housing
                    // tracks use arcs <= pi; retain a strict boundary instead of guessing it.
                    if angle.abs() > std::f32::consts::PI {
                        return Err(CgaError::Unsupported("multi-turn rotation key"));
                    }
                    let half = angle * 0.5;
                    let delta = Quat::from_vw(value * half.sin(), half.cos());
                    if delta.len2_xwyz() < 0.5 || delta.len2_xwyz() > 1.5 {
                        return Err(CgaError::Invalid("rotation axis"));
                    }
                    // 0x315E55BC..0x315E5679: previous * normalized axis-angle delta.
                    cumulative = cumulative * delta.normalized_xwyz();
                    result.rotations.push(RotationKey {
                        frame,
                        quaternion: cumulative,
                        control_in: cumulative,
                        control_out: cumulative,
                    });
                }
            }
        }
        position_tangents(&mut result.positions);
        rotation_controls(&mut result.rotations);
        Ok(result)
    }

    /// Duration from original timing frames (30 FPS). This differs from model dying_time.
    pub fn duration_seconds(&self) -> f32 {
        (self.end_frame - self.start_frame) / 30.0
    }

    /// Constant clips (the guard tower's lower standing part) need no query-time updates.
    pub fn has_motion(&self) -> bool {
        self.positions.windows(2).any(|keys| {
            keys[1].frame > self.start_frame
                && keys[0].frame < self.end_frame
                && (keys[0].raw_position != keys[1].raw_position
                    || keys[0].tangent_out != Vec3::ZERO
                    || keys[1].tangent_in != Vec3::ZERO)
        }) || self.rotations.windows(2).any(|keys| {
            keys[1].frame > self.start_frame
                && keys[0].frame < self.end_frame
                && (keys[0].quaternion != keys[1].quaternion
                    || keys[0].control_out != keys[0].quaternion
                    || keys[1].control_in != keys[1].quaternion)
        })
    }

    /// Untuned TCB position spline, from 0x3158AD50/0x3158B070/0x3158B150 and
    /// Hermite sampling 0x3158B3E0. The spline clamps beyond the complete key array;
    /// the authored clip range and loop are applied by the caller.
    pub fn sample_position(&self, frame: f32) -> Option<Vec3> {
        if !frame.is_finite() {
            return None;
        }
        if self.positions.is_empty() {
            return Some(self.root.position);
        }
        if let Some(key) = anchor(&self.positions, frame, |key| key.frame) {
            return Some(key.position);
        }
        let index = self.positions.partition_point(|key| key.frame <= frame) - 1;
        let a = &self.positions[index];
        let b = &self.positions[index + 1];
        let u = (frame - a.frame) / (b.frame - a.frame);
        let square = u * u;
        let cube = square * u;
        let h00 = (2.0 * cube - 3.0 * square) + 1.0;
        let h01 = 3.0 * square - 2.0 * cube;
        let h10 = (cube - 2.0 * square) + u;
        let h11 = cube - square;
        // Native CControllerTCB converts centimeters after sampling the spline.
        Some(
            (((a.raw_position * h00 + b.raw_position * h01) + a.tangent_out * h10)
                + b.tangent_in * h11)
                * 0.01,
        )
    }

    /// Original untuned angle-axis TCB branch: quaternion SQUAD with time-weighted
    /// controls (0x315EAD40) and native output conjugation (0x3162BAE0).
    pub fn sample(&self, frame: f32) -> Option<RootPose> {
        let position = self.sample_position(frame)?;
        let quaternion = if self.rotations.is_empty() {
            self.root.quaternion
        } else if let Some(key) = anchor(&self.rotations, frame, |key| key.frame) {
            key.quaternion.conj()
        } else {
            let index = self.rotations.partition_point(|key| key.frame <= frame) - 1;
            let a = &self.rotations[index];
            let b = &self.rotations[index + 1];
            let u = (frame - a.frame) / (b.frame - a.frame);
            let controls = slerp(a.control_out, b.control_in, u);
            let keys = slerp(a.quaternion, b.quaternion, u);
            slerp(keys, controls, (1.0 - u) * 2.0 * u)
                .normalized_xwyz()
                .conj()
        };
        Some(RootPose {
            position,
            quaternion,
        })
    }

    /// Exact authored key or clamped endpoint, for native anchor comparisons. Intermediate
    /// spline samples are deliberately not approximated by linear interpolation.
    pub fn key_pose(&self, frame: f32) -> Option<RootPose> {
        if !frame.is_finite() {
            return None;
        }
        let position = anchor(&self.positions, frame, |key| key.frame)
            .map(|key| key.position)
            .or_else(|| self.positions.is_empty().then_some(self.root.position))?;
        let quaternion = anchor(&self.rotations, frame, |key| key.frame)
            .map(|key| key.quaternion.conj())
            .or_else(|| self.rotations.is_empty().then_some(self.root.quaternion))?;
        Some(RootPose {
            position,
            quaternion,
        })
    }
}

fn dot(a: Quat, b: Quat) -> f32 {
    ((a.x * b.x + a.y * b.y) + a.z * b.z) + a.w * b.w
}

fn negate(q: Quat) -> Quat {
    Quat::new(-q.x, -q.y, -q.z, -q.w)
}

fn rotation_log(mut a: Quat, b: Quat) -> Vec3 {
    if dot(a, b) < 0.0 {
        a = negate(a);
    }
    let q = a.conj() * b;
    let length = q.v().len();
    if length == 0.0 {
        return Vec3::ZERO;
    }
    let angle = (length as f64).atan2(q.w as f64) as f32;
    q.v() * (angle / length)
}

fn rotation_exp(v: Vec3) -> Quat {
    let angle = v.len();
    if angle == 0.0 {
        return Quat::IDENTITY;
    }
    Quat::from_vw(v * (angle.sin() / angle), angle.cos())
}

fn rotation_controls(keys: &mut [RotationKey]) {
    for index in 0..keys.len() {
        let key = keys[index];
        let previous = index.checked_sub(1).map(|index| keys[index]);
        let next = keys.get(index + 1).copied();
        let before = previous.map(|previous| rotation_log(previous.quaternion, key.quaternion));
        let after = next.map(|next| rotation_log(key.quaternion, next.quaternion));
        let before = before.or(after).unwrap_or(Vec3::ZERO);
        let after = after.unwrap_or(before);
        let (previous_weight, next_weight) = match (previous, next) {
            (Some(previous), Some(next)) => {
                let scale = 2.0 / (next.frame - previous.frame);
                (
                    (key.frame - previous.frame) * scale,
                    (next.frame - key.frame) * scale,
                )
            }
            _ => (1.0, 1.0),
        };
        // 0x315EB487..0x315EB5CB, specialized to tension/continuity/bias=0.
        let outgoing = (after * (0.5 * next_weight - 1.0) + before * (0.5 * next_weight)) * 0.5;
        let incoming =
            (after * (-0.5 * previous_weight) + before * (1.0 - 0.5 * previous_weight)) * 0.5;
        keys[index].control_out = key.quaternion * rotation_exp(outgoing);
        keys[index].control_in = key.quaternion * rotation_exp(incoming);
    }
}

fn slerp(a: Quat, mut b: Quat, u: f32) -> Quat {
    let mut cosine = dot(a, b);
    if cosine < 0.0 {
        b = negate(b);
        cosine = -cosine;
    }
    if cosine > 0.9999 {
        return Quat::new(
            (b.x - a.x) * u + a.x,
            (b.y - a.y) * u + a.y,
            (b.z - a.z) * u + a.z,
            (b.w - a.w) * u + a.w,
        )
        .normalized_xwyz();
    }
    let orthogonal = Quat::new(
        b.x - a.x * cosine,
        b.y - a.y * cosine,
        b.z - a.z * cosine,
        b.w - a.w * cosine,
    );
    let length = dot(orthogonal, orthogonal).sqrt();
    let angle = (length as f64).atan2(cosine as f64) as f32 * u;
    let scale = angle.sin() / length;
    let cosine = angle.cos();
    Quat::new(
        a.x * cosine + orthogonal.x * scale,
        a.y * cosine + orthogonal.y * scale,
        a.z * cosine + orthogonal.z * scale,
        a.w * cosine + orthogonal.w * scale,
    )
}

fn position_tangents(keys: &mut [PositionKey]) {
    if keys.len() < 2 {
        return;
    }
    if keys.len() == 2 {
        let delta = keys[1].raw_position - keys[0].raw_position;
        keys[0].tangent_out = delta;
        keys[1].tangent_in = delta;
        return;
    }
    for index in 1..keys.len() - 1 {
        let prev = keys[index - 1];
        let current = keys[index];
        let next = keys[index + 1];
        let scale = 2.0 / (next.frame - prev.frame);
        let before = (current.frame - prev.frame) * scale;
        let after = (next.frame - current.frame) * scale;
        let delta_before = current.raw_position - prev.raw_position;
        let delta_after = next.raw_position - current.raw_position;
        keys[index].tangent_in = delta_before * (0.5 * before) + delta_after * (0.5 * before);
        keys[index].tangent_out = delta_before * (0.5 * after) + delta_after * (0.5 * after);
    }
    // The native untuned endpoint rule multiplies (adjacent delta - adjacent
    // tangent) by 3, then by 0.5; it does not copy the adjacent secant.
    keys[0].tangent_out =
        ((keys[1].raw_position - keys[0].raw_position) - keys[1].tangent_in) * 3.0 * 0.5;
    let last = keys.len() - 1;
    keys[last].tangent_in = ((keys[last - 1].raw_position - keys[last].raw_position)
        + keys[last - 1].tangent_out)
        * 3.0
        * -0.5;
}

fn anchor<T>(keys: &[T], frame: f32, time: impl Fn(&T) -> f32) -> Option<&T> {
    let first = keys.first()?;
    let last = keys.last()?;
    if frame <= time(first) {
        return Some(first);
    }
    if frame >= time(last) {
        return Some(last);
    }
    keys.iter().find(|key| time(key) == frame)
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    fn put_word(data: &mut [u8], offset: usize, value: u32) {
        data[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
    }

    pub(crate) fn fixture(
        position: &[(i32, [f32; 3])],
        rotation: &[(i32, [f32; 3], f32)],
    ) -> Vec<u8> {
        let mut node = vec![0; 204];
        node[..4].copy_from_slice(b"root");
        put_word(&mut node, 68, u32::MAX);
        put_word(&mut node, 172, 1.0f32.to_bits());
        for offset in [176, 180, 184] {
            put_word(&mut node, offset, 1.0f32.to_bits());
        }
        put_word(&mut node, 188, 4);
        put_word(
            &mut node,
            192,
            if rotation.is_empty() {
                NO_CONTROLLER
            } else {
                5
            },
        );
        put_word(&mut node, 196, NO_CONTROLLER);
        let mut timing = vec![0; 52];
        put_word(&mut timing, 0, (1.0f32 / 4800.0).to_bits());
        put_word(&mut timing, 4, 160);
        put_word(&mut timing, 44, 700);
        let mut p = vec![0; 16 + position.len() * 36];
        put_word(&mut p, 0, 9);
        put_word(&mut p, 4, position.len() as u32);
        put_word(&mut p, 12, 4);
        for (index, &(tick, value)) in position.iter().enumerate() {
            let offset = 16 + index * 36;
            put_word(&mut p, offset, tick as u32);
            for (component, value) in value.into_iter().enumerate() {
                put_word(&mut p, offset + 4 + component * 4, value.to_bits());
            }
        }
        let mut r = vec![0; 16 + rotation.len() * 40];
        put_word(&mut r, 0, 10);
        put_word(&mut r, 4, rotation.len() as u32);
        put_word(&mut r, 12, 5);
        for (index, &(tick, axis, angle)) in rotation.iter().enumerate() {
            let offset = 16 + index * 40;
            put_word(&mut r, offset, tick as u32);
            for (component, value) in axis.into_iter().enumerate() {
                put_word(&mut r, offset + 4 + component * 4, value.to_bits());
            }
            put_word(&mut r, offset + 16, angle.to_bits());
        }
        let mut chunks = vec![
            (TIMING, 0x918, 2, timing),
            (NODE, 0x823, 3, node),
            (CONTROLLER, 0x826, 4, p),
            (CONTROLLER, 0x826, 5, r),
        ];
        if rotation.is_empty() {
            chunks.pop();
        }
        assemble(chunks)
    }

    fn assemble(chunks: Vec<(u32, u32, i32, Vec<u8>)>) -> Vec<u8> {
        let mut data = vec![0; 20];
        data[..6].copy_from_slice(b"CryTek");
        put_word(&mut data, 12, 0x745);
        let mut table = Vec::new();
        for (kind, version, id, body) in chunks {
            let offset = data.len() as u32;
            let size = 16 + body.len() as u32;
            for value in [kind, version, offset, id as u32] {
                data.extend_from_slice(&value.to_le_bytes());
            }
            data.extend_from_slice(&body);
            table.push([kind, version, offset, id as u32, size]);
        }
        let offset = data.len() as u32;
        put_word(&mut data, 16, offset);
        data.extend_from_slice(&(table.len() as u32).to_le_bytes());
        for entry in table {
            for value in entry {
                data.extend_from_slice(&value.to_le_bytes());
            }
        }
        data
    }

    fn model_fixture(parent_id: i32) -> Vec<u8> {
        let old = super::super::cgf::tests::synthetic_cgf(&[1, 2, 3]);
        let file = CgfFile::parse(&old).unwrap();
        let mut chunks = file
            .chunks
            .iter()
            .map(|chunk| {
                let mut body = file.body(chunk).unwrap().to_vec();
                if chunk.ctype == NODE {
                    put_word(&mut body, 68, parent_id as u32);
                    put_word(&mut body, 148, 100.0f32.to_bits());
                    put_word(&mut body, 160, std::f32::consts::FRAC_1_SQRT_2.to_bits());
                    put_word(&mut body, 172, std::f32::consts::FRAC_1_SQRT_2.to_bits());
                    for offset in [176, 180, 184] {
                        put_word(&mut body, offset, 1.0f32.to_bits());
                    }
                    for offset in [188, 192, 196] {
                        put_word(&mut body, offset, NO_CONTROLLER);
                    }
                }
                (chunk.ctype, chunk.version, chunk.id, body)
            })
            .collect::<Vec<_>>();
        let mut root = vec![0; 204];
        root[..4].copy_from_slice(b"root");
        put_word(&mut root, 68, u32::MAX);
        put_word(&mut root, 148, 9900.0f32.to_bits());
        put_word(&mut root, 172, 1.0f32.to_bits());
        for offset in [176, 180, 184] {
            put_word(&mut root, offset, 1.0f32.to_bits());
        }
        for offset in [188, 192, 196] {
            put_word(&mut root, offset, NO_CONTROLLER);
        }
        chunks.push((NODE, 0x823, 6, root));
        assemble(chunks)
    }

    #[test]
    fn proxy_bind_is_local_and_animation_replaces_root_default_transform() {
        let animation = fixture(
            &[(0, [0.0, 0.0, 1000.0])],
            &[(0, [0.0, 0.0, 1.0], std::f32::consts::FRAC_PI_2)],
        );
        let model = model_fixture(6);
        let track = RootAnimation::read(&model, &animation).unwrap();
        let pose = track.sample(0.0, false, 1.0).unwrap();
        assert!((pose.position - Vec3::new(0.0, -1.0, 10.0)).len() < 1e-5);
        let expected = track.clip.sample(0.0).unwrap().quaternion * track.proxy_quaternion;
        assert!((pose.quaternion.rotate(Vec3::Y) - expected.rotate(Vec3::Y)).len() < 1e-6);
        // Cancelling the animated root, or applying the model root's default +99m,
        // would put the physical proxy somewhere different from its native bone.
        assert!(pose.position.x.abs() < 1e-5);
        assert!(
            (pose.quaternion.rotate(Vec3::X) - track.proxy_quaternion.rotate(Vec3::X)).len() > 0.5
        );
    }

    #[test]
    fn rejects_orphaned_and_cyclic_proxy_hierarchies() {
        let animation = fixture(&[(0, [0.0; 3])], &[]);
        assert_eq!(
            RootAnimation::read(&model_fixture(99), &animation).unwrap_err(),
            CgaError::Invalid("orphan proxy node")
        );
        assert_eq!(
            RootAnimation::read(&model_fixture(1), &animation).unwrap_err(),
            CgaError::Invalid("cyclic node hierarchy")
        );
    }

    #[test]
    fn accepts_original_tower_unity_scale_roundoff_but_rejects_authored_scaling() {
        let mut model = model_fixture(6);
        let mut animation = fixture(&[(0, [0.0; 3])], &[]);
        let model_root = CgfFile::parse(&model).unwrap().chunk(6).unwrap().offset as usize + 16;
        let animation_root =
            CgfFile::parse(&animation).unwrap().chunk(3).unwrap().offset as usize + 16;
        for (data, root) in [(&mut model, model_root), (&mut animation, animation_root)] {
            for offset in [176, 180] {
                put_word(data, root + offset, (1.0 + f32::EPSILON).to_bits());
            }
        }
        assert!(RootAnimation::read(&model, &animation).is_ok());
        put_word(&mut model, model_root + 176, 1.01f32.to_bits());
        assert_eq!(
            RootAnimation::read(&model, &animation).unwrap_err(),
            CgaError::Unsupported("root scale")
        );
    }

    #[test]
    fn constant_clips_do_not_schedule_pose_updates_and_looped_motion_wraps() {
        let constant = RootClip::read(&fixture(
            &[(0, [0.0, 0.0, 830.0]), (160, [0.0, 0.0, 830.0])],
            &[],
        ))
        .unwrap();
        assert!(!constant.has_motion());
        let mut clip = RootClip::read(&fixture(
            &[(0, [0.0; 3]), (30 * 160, [0.0, 0.0, 1000.0])],
            &[],
        ))
        .unwrap();
        clip.end_frame = 30.0;
        assert!(clip.has_motion());
        let track = RootAnimation {
            clip,
            proxy_position: Vec3::ZERO,
            proxy_quaternion: Quat::IDENTITY,
        };
        assert_eq!(track.sample(1.5, true, 1.0), track.sample(0.5, false, 1.0));
        assert_eq!(track.sample(0.5, false, 2.0).unwrap().position.z, 10.0);
        assert_eq!(track.sample(99.0, false, 1.0).unwrap().position.z, 10.0);
    }

    #[test]
    fn original_ticks_and_position_units_and_endpoint_clamping() {
        let data = fixture(
            &[
                (570 * 160, [0.0, 0.0, 1000.0]),
                (689 * 160, [0.0, 0.0, 0.0]),
            ],
            &[],
        );
        let clip = RootClip::read(&data).unwrap();
        assert_eq!(clip.positions[0].frame, 570.0);
        assert_eq!(clip.key_pose(570.0).unwrap().position.z, 10.0);
        assert_eq!(clip.key_pose(700.0).unwrap().position.z, 0.0);
        assert!(clip.key_pose(600.0).is_none());
        assert!(clip.key_pose(f32::NAN).is_none());
    }

    #[test]
    fn rotations_accumulate_in_authored_order_then_conjugate() {
        let h = std::f32::consts::FRAC_PI_2;
        let data = fixture(
            &[(0, [0.0; 3])],
            &[(0, [1.0, 0.0, 0.0], h), (160, [0.0, 0.0, 1.0], h)],
        );
        let clip = RootClip::read(&data).unwrap();
        let qx = Quat::new((h * 0.5).sin(), 0.0, 0.0, (h * 0.5).cos());
        let qz = Quat::new(0.0, 0.0, (h * 0.5).sin(), (h * 0.5).cos());
        let expected = (qx * qz).conj().rotate(Vec3::X);
        assert!((clip.key_pose(1.0).unwrap().quaternion.rotate(Vec3::X) - expected).len() < 1e-6);
        assert!((expected - (qz * qx).conj().rotate(Vec3::X)).len() > 0.5);
    }

    #[test]
    fn untuned_tcb_uses_adjacent_time_spans_and_hermite_tangents() {
        let data = fixture(
            &[
                (0, [0.0; 3]),
                (160, [0.0, 0.0, 100.0]),
                (480, [0.0, 0.0, 0.0]),
            ],
            &[],
        );
        let clip = RootClip::read(&data).unwrap();
        // At the interior peak, equal opposite position deltas cancel despite
        // unequal time spans. Native endpoint tangents make this curved.
        assert_eq!(clip.positions[1].tangent_in, Vec3::ZERO);
        assert_eq!(clip.positions[1].tangent_out, Vec3::ZERO);
        assert!((clip.sample_position(0.5).unwrap().z - 0.6875).abs() < 1e-6);
        assert!((clip.sample_position(2.0).unwrap().z - 0.6875).abs() < 1e-6);
        assert_eq!(clip.sample_position(700.0).unwrap(), Vec3::ZERO);
    }

    #[test]
    fn rotation_spline_preserves_full_turn_through_incremental_keys() {
        let quarter = std::f32::consts::FRAC_PI_2;
        let data = fixture(
            &[(0, [0.0; 3])],
            &[
                (0, [0.0, 0.0, 1.0], 0.0),
                (160, [0.0, 0.0, 1.0], quarter),
                (320, [0.0, 0.0, 1.0], quarter),
                (480, [0.0, 0.0, 1.0], quarter),
                (640, [0.0, 0.0, 1.0], quarter),
            ],
        );
        let clip = RootClip::read(&data).unwrap();
        let pose = clip.sample(0.5).unwrap();
        let expected = Vec3::new(
            std::f32::consts::FRAC_1_SQRT_2,
            -std::f32::consts::FRAC_1_SQRT_2,
            0.0,
        );
        assert!((pose.quaternion.rotate(Vec3::X) - expected).len() < 1e-5);
        assert!((clip.sample(2.0).unwrap().quaternion.rotate(Vec3::X) + Vec3::X).len() < 1e-5);
        assert!((clip.sample(4.0).unwrap().quaternion.rotate(Vec3::X) - Vec3::X).len() < 1e-5);
    }

    #[test]
    fn rejects_duplicate_times_nonfinite_values_and_unbounded_key_counts() {
        assert!(RootClip::read(&fixture(&[(160, [0.0; 3]), (160, [0.0; 3])], &[])).is_err());
        assert!(RootClip::read(&fixture(&[(0, [f32::NAN, 0.0, 0.0])], &[])).is_err());
        let mut data = fixture(&[(0, [0.0; 3])], &[]);
        let file = CgfFile::parse(&data).unwrap();
        let offset = file.chunk(4).unwrap().offset as usize + 16 + 4;
        put_word(&mut data, offset, u32::MAX);
        assert_eq!(
            RootClip::read(&data).unwrap_err(),
            CgaError::Invalid("key count")
        );
    }
}
