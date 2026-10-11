//! Water volumes as physics areas (buoyancy for hulls on lakes and rivers above the ocean).
//!
//! The engine creates one area per water volume while loading cell objects (`CWaterVolume`,
//! world slot 83 `AddArea(points, …)`: a 2-D polygon area with a water plane). Slot 83 is not
//! ported in `aaemu-physics`, so each volume of `ai::physics::water` (`object.dat`) becomes a
//! **geometry area** (slot 82) instead:
//!
//! * polygon volumes: one box over the polygon's bounding rectangle, from `surface − depth` to
//!   the surface;
//! * rivers (line arrays): one oriented box per segment, `river_width` wide, at the segment's
//!   mean surface height;
//!
//! each with `pe_params_buoyancy` (`waterDensity 1000`, `waterResistance 1000`, plane normal
//! +z at the surface, the volume's flow). The boxes cover land next to irregular shores, but
//! buoyancy only acts below the plane, i.e. where the terrain is below the water surface.

use std::sync::Arc;

use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
use aaemu_physics::math::{Quat, Vec3};
use aaemu_physics::params::ParamsBuoyancy;

use crate::systems::ai::physics::water::{WaterBodyArea, WaterBodyAreaType};

use super::world::WorldState;

/// The identity of a volume (name + rounded bounds).
pub fn area_key(a: &WaterBodyArea) -> String {
    let b = &a.bounding_box;
    format!(
        "{}|{}|{}|{}|{}|{}",
        a.name,
        b.x.round() as i64,
        b.y.round() as i64,
        b.width.round() as i64,
        b.height.round() as i64,
        (a.highest * 10.0).round() as i64
    )
}

fn add_box(
    st: &mut WorldState,
    center: Vec3,
    half: Vec3,
    yaw: f32,
    surface: f32,
    flow: Vec3,
) -> Option<aaemu_physics::world::EntIdx> {
    if half.x <= 0.0 || half.y <= 0.0 || half.z <= 0.0 {
        return None;
    }
    let g = st
        .world
        .geometry(&Primitive::Box(BoxPrim::aligned(Vec3::ZERO, half)))?;
    let q = Quat::new(0.0, 0.0, (yaw * 0.5).sin(), (yaw * 0.5).cos());
    let a = st
        .world
        .add_area_geometry(Arc::clone(&g.geom), center, q, 1.0);
    let p = ParamsBuoyancy {
        water_density: 1000.0,
        water_resistance: 1000.0,
        water_flow: flow,
        water_plane_n: Vec3::new(0.0, 0.0, 1.0),
        water_plane_origin: Vec3::new(center.x, center.y, surface),
        i_medium: 0,
        ..ParamsBuoyancy::default()
    };
    st.world.area_set_params_buoyancy(a, &p);
    Some(a)
}

/// Adds the areas of the volumes not added yet; returns the number of areas created.
pub fn sync_areas(st: &mut WorldState, areas: &[WaterBodyArea]) -> usize {
    let mut n = 0;
    for a in areas {
        let key = area_key(a);
        if st.water_areas.contains_key(&key) {
            continue;
        }
        let depth = if a.depth > 0.0 { a.depth } else { 50.0 };
        let mut ents = Vec::new();
        match a.area_type {
            WaterBodyAreaType::Polygon => {
                let b = &a.bounding_box;
                let top = a.highest;
                let center =
                    Vec3::new(b.x + b.width * 0.5, b.y + b.height * 0.5, top - depth * 0.5);
                let flow = Vec3::ZERO;
                if let Some(e) = add_box(
                    st,
                    center,
                    Vec3::new(b.width * 0.5, b.height * 0.5, depth * 0.5),
                    0.0,
                    top,
                    flow,
                ) {
                    ents.push(e);
                }
            }
            WaterBodyAreaType::LineArray => {
                let w = a.river_width.max(1.0);
                let flow = Vec3::new(
                    a.flow_axis.0 * a.flow_speed_signed,
                    a.flow_axis.1 * a.flow_speed_signed,
                    0.0,
                );
                for s in a.points.windows(2) {
                    let (p0, p1) = (s[0], s[1]);
                    let (dx, dy) = (p1.x - p0.x, p1.y - p0.y);
                    let len = (dx * dx + dy * dy).sqrt();
                    if len < 0.01 {
                        continue;
                    }
                    let top = (p0.z + p1.z) * 0.5;
                    let yaw = dy.atan2(dx);
                    let center =
                        Vec3::new((p0.x + p1.x) * 0.5, (p0.y + p1.y) * 0.5, top - depth * 0.5);
                    if let Some(e) = add_box(
                        st,
                        center,
                        Vec3::new(len * 0.5 + w * 0.5, w, depth * 0.5),
                        yaw,
                        top,
                        flow,
                    ) {
                        ents.push(e);
                    }
                }
            }
        }
        n += ents.len();
        st.water_areas.insert(key, ents);
    }
    n
}
