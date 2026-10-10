//! `Core/Managers/World/Debug/WaterDebugSnapshot` and the `WorldManager` auto water probe
//! (`AutoWaterProbeEnable` / `AutoWaterProbeDisable` / `AutoWaterProbeTick`): GM water debug
//! output (`/waterdebug`).
//!
//! The C# subscribes `AutoWaterProbeTick` (every 10 s) in `WorldManager.Initialize`; here the
//! 10 s loop starts with the first `AutoWaterProbeEnable` and runs for the server's lifetime.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::server::GameServer;
use crate::systems::ai::geodata::manager::to_cell_index;
use crate::systems::ai::numerics::Vec3Ext;
use crate::systems::skills::controllers::Vec3;
use crate::world::WorldInstance;

use super::objects_file::{ObjectsFile, Prefab, WaterVolumeType};
use super::water::{river_like_polygon_metrics, RectF, WaterBodyAreaType};

/// `{v:F<decimals>}` of a float.
fn fx(v: f32, decimals: usize) -> String {
    crate::systems::services::commands::scripts::util::fixed(f64::from(v), decimals)
}

fn b(v: bool) -> &'static str {
    if v {
        "True"
    } else {
        "False"
    }
}

/// `WaterDebugSnapshot.FormatOneLine(pos, isWater, flow)`.
pub fn format_one_line(pos: Vec3, is_water: bool, flow: Vec3) -> String {
    format!(
        "[waterprobe] Pos ({},{},{}) IsWater={} flow=({},{},{}) len={}",
        fx(pos.x, 1),
        fx(pos.y, 1),
        fx(pos.z, 1),
        b(is_water),
        fx(flow.x, 3),
        fx(flow.y, 3),
        fx(flow.z, 3),
        fx(flow.length(), 3)
    )
}

/// `WaterDebugSnapshot.Capture(world, pos, includeClientDump)`.
pub fn capture(
    server: &GameServer,
    world: &WorldInstance,
    pos: Vec3,
    include_client_dump: bool,
) -> Vec<String> {
    let mut out = Vec::new();
    let w = super::world_water(server, world);
    let template = &world.template;
    let ocean_tpl = template.ocean_level;
    let areas = w.areas_snapshot();
    out.push(format!(
        "[waterprobe] Water.OceanLevel={} (template {}), areas={}",
        crate::systems::services::webapi::json::dotnet_single_string(w.ocean_level),
        crate::systems::services::webapi::json::dotnet_single_string(ocean_tpl),
        areas.len()
    ));
    out.push(format!(
        "[waterprobe] Pos ({}, {}, {})",
        fx(pos.x, 2),
        fx(pos.y, 2),
        fx(pos.z, 2)
    ));
    let zero = Vec3::new(0.0, 0.0, 0.0);
    let is_w = w.is_water(pos);
    let flow = is_w.unwrap_or(zero);
    out.push(format!(
        "[waterprobe] IsWater={}, flow=({},{},{}), len={}",
        b(is_w.is_some()),
        fx(flow.x, 3),
        fx(flow.y, 3),
        fx(flow.z, 3),
        fx(flow.length(), 3)
    ));
    let (surf_z, flow_surf) = w.get_water_surface(pos);
    out.push(format!(
        "[waterprobe] GetWaterSurface Z={}, flowSurf=({},{},{})",
        fx(surf_z, 3),
        fx(flow_surf.x, 3),
        fx(flow_surf.y, 3),
        fx(flow_surf.z, 3)
    ));
    out.push(format!(
        "[waterprobe] depthBelowSurface (surface - pos.Z)={}",
        fx(surf_z - pos.z, 3)
    ));

    let (cx, cy) = to_cell_index(pos);
    if cx < 0 || cx >= template.cell_x || cy < 0 || cy >= template.cell_y {
        out.push(format!(
            "[waterprobe] Cell ({cx},{cy}) — outside template grid ({}x{} cells)",
            template.cell_x, template.cell_y
        ));
    } else {
        let (loaded, objects) = super::cell_object_dat(server, world, cx, cy);
        out.push(format!(
            "[waterprobe] Cell ({cx},{cy}) Loaded={}, object.dat={}",
            b(loaded),
            if objects.is_some() { "yes" } else { "no" }
        ));
        if include_client_dump {
            out.extend(capture_cell_client_water_dump(objects.as_ref()));
        }
    }

    let (px, py) = (pos.x, pos.y);
    out.push(format!(
        "[waterprobe] Areas with XY inside rough bbox: {}",
        areas
            .iter()
            .filter(|a| a.bounding_box.contains(px, py))
            .count()
    ));
    out.push("[waterprobe] Nearest 4 water bodies (short id, full checks):".into());

    // OrderBy (stable) + Take(4)
    let mut ranked: Vec<(usize, f32)> = areas
        .iter()
        .enumerate()
        .map(|(i, a)| (i, dist_sq_point_to_rect(px, py, a.bounding_box)))
        .collect();
    ranked.sort_by(|a, b| a.1.total_cmp(&b.1));
    for (i, dist_sq) in ranked.into_iter().take(4) {
        let a = &areas[i];
        let dist = dist_sq.sqrt();
        let tag = if a.area_type == WaterBodyAreaType::Polygon {
            "[P]"
        } else {
            "[L]"
        };
        let short_name = short_water_name(&a.name);
        let bb = a.bounding_box.contains(px, py);
        let rw_extra = if a.area_type == WaterBodyAreaType::LineArray {
            format!(" rw={}", fx(a.river_width, 1))
        } else {
            String::new()
        };
        let (sp_extra, axis_extra) = if a.flow_speed_signed != 0.0 {
            (
                format!(" sp={}", fx(a.flow_speed_signed, 1)),
                format!(" axis=({},{})", fx(a.flow_axis.0, 2), fx(a.flow_axis.1, 2)),
            )
        } else {
            (String::new(), String::new())
        };
        let mut poly_extra = String::new();
        if a.area_type == WaterBodyAreaType::Polygon {
            if let Some(m) = river_like_polygon_metrics(&a.points) {
                poly_extra = format!(
                    " poly(len={} hw={} meanW={} area={} asp={})",
                    fx(m.length, 0),
                    fx(m.max_half_width, 0),
                    fx(m.mean_full_width, 0),
                    fx(m.area, 0),
                    fx(m.aspect, 1)
                );
            }
        }
        let head = format!(
            "{tag} id={} ~ {short_name} dist={} bbox={}{rw_extra}{sp_extra}{axis_extra}{poly_extra}",
            a.id,
            fx(dist, 2),
            b(bb)
        );
        let Some((s_pt, f_surf)) = a.get_surface(pos) else {
            out.push(format!("{head} XY-in-surface=False"));
            continue;
        };
        let z_min = s_pt.z - a.depth;
        let z_max = s_pt.z;
        let vert_ok = pos.z <= z_max && pos.z >= z_min;
        out.push(format!(
            "{head} surfZ={} depth={} [zMin,zMax]=[{},{}] vertOk={} flowLen={}",
            fx(s_pt.z, 3),
            fx(a.depth, 3),
            fx(z_min, 3),
            fx(z_max, 3),
            b(vert_ok),
            fx(f_surf.length(), 3)
        ));
    }
    out
}

/// The water volumes of a cell's `LoadedObjectDat`, per `WaterObjectVolumeType`
/// (total, River, Area, Ocean, Sector) and those with a flow speed (`Speed != 0`).
pub fn count_client_water(objects: Option<&ObjectsFile>) -> ([usize; 5], usize) {
    let mut c = [0usize; 5];
    let mut with_flow = 0;
    for p in objects.map_or(&[][..], |o| &o.prefabs[..]) {
        let Prefab::Water(w) = p else { continue };
        c[0] += 1;
        match w.volume_type {
            WaterVolumeType::River => c[1] += 1,
            WaterVolumeType::Area => c[2] += 1,
            WaterVolumeType::Ocean => c[3] += 1,
            WaterVolumeType::Sector => c[4] += 1,
            WaterVolumeType::Other(_) => {}
        }
        if w.speed != 0.0 {
            with_flow += 1;
        }
    }
    (c, with_flow)
}

/// `CaptureCellClientWaterDump(cell)`.
fn capture_cell_client_water_dump(objects: Option<&ObjectsFile>) -> Vec<String> {
    let ([n, river, area, ocean, sector], _) = count_client_water(objects);
    if n == 0 {
        return Vec::new();
    }
    vec![format!(
        "[waterprobe] Cell client [object] water volumes={n} (River={river} Area={area} Ocean={ocean} Sector={sector})"
    )]
}

/// `DistSqPointToRect(px, py, r)`.
fn dist_sq_point_to_rect(px: f32, py: f32, r: RectF) -> f32 {
    // Math.Clamp(px, r.Left, r.Right)
    let nx = px.max(r.x).min(r.x + r.width);
    let ny = py.max(r.y).min(r.y + r.height);
    let dx = px - nx;
    let dy = py - ny;
    dx * dx + dy * dy
}

/// `ShortWaterName(name)`.
fn short_water_name(name: &str) -> String {
    if name.is_empty() {
        return "?".into();
    }
    let mut s = name;
    if let Some(r) = s.strip_prefix("Segment_") {
        s = r;
    }
    if let Some(r) = s.strip_prefix("Water_") {
        s = r;
    }
    if let Some(r) = s.strip_prefix("WaterContour_") {
        s = r;
    }
    const MAX: usize = 48;
    let units: Vec<u16> = s.encode_utf16().collect();
    if units.len() <= MAX {
        s.to_string()
    } else {
        format!("{}…", String::from_utf16_lossy(&units[..MAX]))
    }
}

// ---------------------------------------------------------------------------------------------
// AutoWaterProbe
// ---------------------------------------------------------------------------------------------

#[derive(Debug)]
struct Probe {
    enabled: bool,
    pos: Vec3,
    last_len: f32,
    /// Unix ms of the last log line (0 = `DateTime.MinValue`).
    last_log_ms: i64,
}

static PROBE: Mutex<Probe> = Mutex::new(Probe {
    enabled: false,
    pos: Vec3 {
        x: 0.0,
        y: 0.0,
        z: 0.0,
    },
    last_len: f32::NAN,
    last_log_ms: 0,
});
static PROBE_TICK_STARTED: AtomicBool = AtomicBool::new(false);

fn probe() -> std::sync::MutexGuard<'static, Probe> {
    PROBE.lock().unwrap_or_else(|e| e.into_inner())
}

/// Whether the auto probe is on, and where.
pub fn auto_water_probe_state() -> (bool, Vec3) {
    let p = probe();
    (p.enabled, p.pos)
}

/// `WorldManager.AutoWaterProbeEnable(pos)` (+ starts the 10 s tick on first use).
pub fn auto_water_probe_enable(server: &GameServer, pos: Vec3) {
    {
        let mut p = probe();
        p.enabled = true;
        p.pos = pos;
        p.last_len = f32::NAN;
        p.last_log_ms = 0;
    }
    if PROBE_TICK_STARTED.swap(true, Ordering::SeqCst) {
        return;
    }
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        PROBE_TICK_STARTED.store(false, Ordering::SeqCst);
        return;
    };
    let weak = Arc::downgrade(&server);
    drop(server);
    rt.spawn(async move {
        loop {
            tokio::time::sleep(Duration::from_secs(10)).await;
            let Some(server) = weak.upgrade() else {
                PROBE_TICK_STARTED.store(false, Ordering::SeqCst);
                return;
            };
            auto_water_probe_tick(&server);
        }
    });
}

/// `WorldManager.AutoWaterProbeDisable()`.
pub fn auto_water_probe_disable() {
    probe().enabled = false;
}

/// `AutoWaterProbeTick(delta)`: logs the probe line when the flow changed or once a minute.
pub fn auto_water_probe_tick(server: &GameServer) -> Option<String> {
    let (enabled, pos) = auto_water_probe_state();
    if !enabled {
        return None;
    }
    // MainWorld
    let world = server
        .world
        .world(crate::models::character::DEFAULT_INSTANCE_ID)?;
    let w = super::world_water(server, &world);
    let is_w = w.is_water(pos);
    let flow = is_w.unwrap_or(Vec3::new(0.0, 0.0, 0.0));
    let len = flow.length();
    let line = format_one_line(pos, is_w.is_some(), flow);
    let now = crate::world::now_ms();
    {
        let mut p = probe();
        let changed = !p.last_len.is_finite() || (p.last_len - len).abs() > 0.05;
        let heartbeat = now - p.last_log_ms >= 60_000;
        if !changed && !heartbeat {
            return None;
        }
        p.last_len = len;
        p.last_log_ms = now;
    }
    tracing::info!("{line}");
    Some(line)
}
