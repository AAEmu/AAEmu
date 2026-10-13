//! Positions, distances and neighbourhood queries the skill code uses: `Transform.World`,
//! `BaseUnit.GetDistanceTo` (model radii), `MathUtil.IsFront`, `WorldManager.GetAround<T>(obj,
//! radius, useModelSize)` / `GetAroundByShape`, and the "position units" (`BaseUnit { ObjId =
//! uint.MaxValue }`) skills use for ground targets.

use std::sync::Arc;

use crate::data::world_templates::REGION_SIZE;
use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::world::{WorldInstance, WorldObject};

use super::data::misc::{area_shape_type, AreaShape};

/// The obj id of a position unit.
pub const POSITION_OBJ_ID: u32 = u32::MAX;

/// A position unit (`new BaseUnit { ObjId = uint.MaxValue }` with a detached transform).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PosUnit {
    pub instance_id: u32,
    pub zone_id: u32,
    pub pos: PositionAndRotation,
}

/// A skill / plot target: a world object or a position unit.
#[derive(Debug, Clone)]
pub enum Target {
    Obj(WorldObject),
    Pos(PosUnit),
}

impl Target {
    pub fn obj_id(&self) -> u32 {
        match self {
            Target::Obj(o) => o.obj_id(),
            Target::Pos(_) => POSITION_OBJ_ID,
        }
    }

    pub fn as_obj(&self) -> Option<&WorldObject> {
        match self {
            Target::Obj(o) => Some(o),
            Target::Pos(_) => None,
        }
    }

    /// `target is Unit`.
    pub fn is_unit(&self) -> bool {
        self.as_obj().is_some_and(WorldObject::is_unit)
    }

    /// Same target (handle identity, or equal positions).
    pub fn same(&self, other: &Target) -> bool {
        match (self, other) {
            (Target::Obj(a), Target::Obj(b)) => a.same(b),
            (Target::Pos(a), Target::Pos(b)) => a == b,
            _ => false,
        }
    }
}

impl From<WorldObject> for Target {
    fn from(o: WorldObject) -> Self {
        Target::Obj(o)
    }
}

impl From<&WorldObject> for Target {
    fn from(o: &WorldObject) -> Self {
        Target::Obj(o.clone())
    }
}

/// A retained handle must still identify the object in the world, even after ID recycling.
pub(crate) fn is_live_object(server: &GameServer, obj: &WorldObject) -> bool {
    server
        .world
        .object(obj.obj_id())
        .is_some_and(|current| std::ptr::eq(current.core(), obj.core()))
}

/// The world instance an object lives in (`ParentWorld`), falling back to its transform's
/// instance id.
pub fn world_of(server: &GameServer, obj: &WorldObject) -> Option<Arc<WorldInstance>> {
    server
        .world
        .world_of(obj)
        .or_else(|| server.world.world(obj.transform().instance_id))
}

/// `Transform.World` of a target.
pub fn world_pos(server: &GameServer, t: &Target) -> PositionAndRotation {
    match t {
        Target::Obj(o) => match world_of(server, o) {
            Some(w) => w.world_pos_rot(o),
            None => o.transform().local(),
        },
        Target::Pos(p) => p.pos,
    }
}

/// `Transform.InstanceId` / `ZoneId` of a target.
pub fn instance_and_zone(t: &Target) -> (u32, u32) {
    match t {
        Target::Obj(o) => {
            let tr = o.transform();
            (tr.instance_id, tr.zone_id)
        }
        Target::Pos(p) => (p.instance_id, p.zone_id),
    }
}

/// `Unit.ModelSize` (`ActorModel.Radius * Scale`; 0 for non-units and position units).
pub fn model_size(server: &GameServer, t: &Target) -> f32 {
    match t {
        Target::Obj(o) => o
            .with_unit(|u| (u.unit().model_id, u.unit().scale))
            .map_or(0.0, |(m, s)| {
                server.game_data.systems.skills.model_radius(m) * s
            }),
        Target::Pos(_) => 0.0,
    }
}

/// `MathUtil.CalculateDistance(a, b, includeZ)`.
pub fn raw_distance(a: &PositionAndRotation, b: &PositionAndRotation, include_z: bool) -> f32 {
    let dx = a.x - b.x;
    let dy = a.y - b.y;
    if include_z {
        let dz = a.z - b.z;
        (dx * dx + dy * dy + dz * dz).sqrt()
    } else {
        (dx * dx + dy * dy).sqrt()
    }
}

/// Edge-to-edge distance uses both scaled actor radii (or the housing radius).
pub fn distance_to(server: &GameServer, from: &Target, to: &Target, include_z: bool) -> f32 {
    let a = world_pos(server, from);
    let b = world_pos(server, to);
    if a.x == b.x && a.y == b.y && a.z == b.z {
        return 0.0;
    }
    let mut d = raw_distance(&a, &b, include_z);
    if let Target::Obj(o) = to {
        if let Some(r) = crate::systems::housing::house_radius(server, o) {
            // Subtract house radius (GardenRadius * Scale)
            d -= r;
        } else {
            d -= model_size(server, to);
        }
    }
    d -= model_size(server, from);
    d.max(0.0)
}

/// `MathUtil.ClampDegAngle`: [-180, 180].
fn clamp_deg(mut a: f64) -> f64 {
    while a > 180.0 {
        a -= 360.0;
    }
    while a < -180.0 {
        a += 360.0;
    }
    a
}

/// `MathUtil.IsFront(obj1, obj2)`.
pub fn is_front(a: &PositionAndRotation, b: &PositionAndRotation) -> bool {
    let angle = (f64::from(b.y - a.y)
        .atan2(f64::from(b.x - a.x))
        .to_degrees())
        % 360.0;
    let deg = clamp_deg(angle - 90.0 - f64::from(a.rot_z).to_degrees());
    (-90.0..=90.0).contains(&deg)
}

/// A full cone angle in degrees around the engine's +Y forward axis.
pub fn in_angle(a: &PositionAndRotation, b: &PositionAndRotation, angle: f32) -> bool {
    if angle <= 0.0 || angle >= 360.0 || (a.x == b.x && a.y == b.y) {
        return true;
    }
    let heading = f64::from(b.y - a.y)
        .atan2(f64::from(b.x - a.x))
        .to_degrees();
    let relative = clamp_deg(heading - 90.0 - f64::from(a.rot_z).to_degrees());
    relative.abs() <= f64::from(angle) / 2.0
}

/// `RadiusFitsCurrentRegion`.
fn radius_fits_region(x: f32, y: f32, radius: f32) -> bool {
    let size = REGION_SIZE as f32;
    let xm = x % size;
    if xm - radius < 0.0 || xm + radius > size {
        return false;
    }
    let ym = y % size;
    !(ym - radius < 0.0 || ym + radius > size)
}

/// The world a target lives in.
pub fn target_world(server: &GameServer, t: &Target) -> Option<Arc<WorldInstance>> {
    match t {
        Target::Obj(o) => world_of(server, o),
        Target::Pos(p) => server.world.world(p.instance_id),
    }
}

/// `WorldManager.GetAround<T>(obj, radius, useModelSize)` (every kind; callers filter). The
/// origin's region comes from its position (`GetRegion`).
pub fn around_radius(
    server: &GameServer,
    origin: &Target,
    radius: f32,
    use_model_size: bool,
) -> Vec<WorldObject> {
    let mut out = Vec::new();
    if radius <= 0.0 {
        return out;
    }
    let Some(world) = target_world(server, origin) else {
        return out;
    };
    let pos = world_pos(server, origin);
    let Some(region) = (match origin {
        Target::Obj(o) => world.region_of(o).filter(|_| o.region().is_some()),
        Target::Pos(_) => world.region_by_pos(pos.x, pos.y),
    }) else {
        return out;
    };
    let mut radius = radius;
    if use_model_size {
        radius += model_size(server, origin);
    }
    let regions = if radius_fits_region(pos.x, pos.y, radius) {
        vec![region]
    } else {
        world.neighbors(region)
    };
    let exclude = origin.obj_id();
    let sq = radius * radius;
    for r in regions {
        for o in world.objects_in_region(r) {
            if o.obj_id() == exclude {
                continue;
            }
            let mut final_rad = sq;
            if use_model_size {
                let ms = model_size(server, &Target::Obj(o.clone()));
                final_rad += ms * ms;
            }
            let p = world.world_pos_rot(&o);
            let dx = (p.x - pos.x) * (p.x - pos.x);
            if dx > final_rad {
                continue;
            }
            let dy = (p.y - pos.y) * (p.y - pos.y);
            if dx + dy < final_rad {
                out.push(o);
            }
        }
    }
    out
}

/// `MathUtil.GetCuboidVertices(length, width, x, y, rotationZ)` (radians).
fn cuboid_vertices(length: f32, width: f32, x: f32, y: f32, rot_z: f32) -> [(f32, f32); 4] {
    let rad_front = rot_z - std::f32::consts::PI / 2.0;
    let rad_right = rot_z;
    let (cf, sf) = (rad_front.cos(), rad_front.sin());
    let (cr, sr) = (rad_right.cos(), rad_right.sin());
    let p = |w: f32, l: f32| {
        let a = (w * cf + x, w * sf + y);
        (l * cr + a.0, l * sr + a.1)
    };
    [
        p(width, length),
        p(width, -length),
        p(-width, -length),
        p(-width, length),
    ]
}

fn sign(p1: (f32, f32), p2: (f32, f32), p3: (f32, f32)) -> f32 {
    (p1.0 - p3.0) * (p2.1 - p3.1) - (p2.0 - p3.0) * (p1.1 - p3.1)
}

/// `MathUtil.PointInTriangle`.
fn point_in_triangle(pt: (f32, f32), v1: (f32, f32), v2: (f32, f32), v3: (f32, f32)) -> bool {
    let b1 = sign(pt, v1, v2) < 0.0;
    let b2 = sign(pt, v2, v3) < 0.0;
    let b3 = sign(pt, v3, v1) < 0.0;
    b1 == b2 && b2 == b3
}

/// `WorldManager.GetAroundByShape<T>(obj, shape)` (`None` for an unknown shape type, the C#
/// returns null).
pub fn around_shape(
    server: &GameServer,
    origin: &Target,
    shape: &AreaShape,
) -> Option<Vec<WorldObject>> {
    match shape.kind {
        area_shape_type::SPHERE => {
            let radius = if shape.value1 > 0.0 {
                shape.value1
            } else {
                40.0
            };
            Some(around_radius(server, origin, radius, true))
        }
        area_shape_type::CUBOID => {
            let diagonal = (shape.value1 * shape.value1 + shape.value2 * shape.value2).sqrt();
            let res = around_radius(server, origin, diagonal, true);
            // AreaShape.ComputeCuboid
            let o = world_pos(server, origin);
            let z_off = shape.value3;
            let world = target_world(server, origin)?;
            let res: Vec<WorldObject> = res
                .into_iter()
                .filter(|t| {
                    let p = world.world_pos_rot(t);
                    p.z >= o.z - z_off && p.z <= o.z + z_off
                })
                .collect();
            if res.is_empty() {
                return Some(res);
            }
            let v = cuboid_vertices(shape.value1, shape.value2, o.x, o.y, o.rot_z);
            Some(
                res.into_iter()
                    .filter(|t| {
                        let p = world.world_pos_rot(t);
                        point_in_triangle((p.x, p.y), v[0], v[1], v[2])
                            || point_in_triangle((p.x, p.y), v[1], v[2], v[3])
                    })
                    .collect(),
            )
        }
        _ => {
            tracing::error!("AreaShape had impossible type");
            None
        }
    }
}

/// A position unit at `target`'s world position (`posUnit.Transform = X.Transform.CloneDetached`).
pub fn pos_unit_from(server: &GameServer, t: &Target) -> PosUnit {
    let (instance_id, zone_id) = instance_and_zone(t);
    PosUnit {
        instance_id,
        zone_id,
        pos: world_pos(server, t),
    }
}

impl PosUnit {
    /// `Transform.Local.Rotate(0, 0, rad)` (adds to the yaw).
    pub fn rotate_z(&mut self, rad: f32) {
        self.pos.rot_z += rad;
    }

    /// `Transform.Local.AddDistanceToFront(distance)` (horizontal only).
    pub fn add_distance_to_front(&mut self, distance: f32) {
        self.pos.x += -distance * self.pos.rot_z.sin();
        self.pos.y += distance * self.pos.rot_z.cos();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn p(x: f32, y: f32, rz: f32) -> PositionAndRotation {
        PositionAndRotation::new(x, y, 0.0, 0.0, 0.0, rz)
    }

    #[test]
    fn front_check_uses_yaw_facing_plus_y() {
        // rotation 0 faces +Y
        assert!(is_front(&p(0.0, 0.0, 0.0), &p(0.0, 5.0, 0.0)));
        assert!(!is_front(&p(0.0, 0.0, 0.0), &p(0.0, -5.0, 0.0)));
        assert!(is_front(&p(0.0, 0.0, 0.0), &p(5.0, 0.0, 0.0)));
        // turned around
        assert!(is_front(
            &p(0.0, 0.0, std::f32::consts::PI),
            &p(0.0, -5.0, 0.0)
        ));
    }

    #[test]
    fn cuboid_contains_center() {
        let v = cuboid_vertices(2.0, 3.0, 10.0, 10.0, 0.0);
        let c = (
            (v[0].0 + v[1].0 + v[2].0) / 3.0,
            (v[0].1 + v[1].1 + v[2].1) / 3.0,
        );
        assert!(point_in_triangle(c, v[0], v[1], v[2]));
        assert!(
            !point_in_triangle((20.0, 20.0), v[0], v[1], v[2])
                && !point_in_triangle((20.0, 20.0), v[1], v[2], v[3])
        );
    }

    #[test]
    fn region_fit() {
        assert!(radius_fits_region(32.0, 32.0, 10.0));
        assert!(!radius_fits_region(2.0, 32.0, 10.0));
    }
}
