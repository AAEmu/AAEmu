//! Which cells are loaded. The zone loads every cell of its zone at start
//! (`SeamlessSystem::LoadZone` 0x365704D0); the whole main_world (244 k brushes, 2.4 M
//! vegetation instances, 5.7 k models) is too much for one process, so cells are loaded around
//! players and ships (`Physics.LoadRadiusCells`), on demand by queries
//! (`Physics.LoadOnQuery`), and unloaded after `Physics.UnloadAfterSeconds` without anyone
//! near. A background task runs [`tick`] every `Physics.SyncPeriodMs`; it also syncs the
//! doodad colliders, frees destroyed entities and drops unused models.

use std::sync::Arc;
use std::time::{Duration, Instant};

use crate::server::GameServer;
use crate::world::WorldObject;

/// Starts the residency task (no-op with `Physics.Enable = false`).
pub fn start(server: &Arc<GameServer>) {
    let cfg = &server.config.physics;
    if !cfg.enable {
        return;
    }
    if cfg.preload_all_cells {
        let srv = server.clone();
        tokio::task::spawn_blocking(move || preload_all(&srv));
    }
    let weak = Arc::downgrade(server);
    let period = Duration::from_millis(cfg.sync_period_ms.max(100));
    tokio::spawn(async move {
        let mut iv = tokio::time::interval(period);
        loop {
            iv.tick().await;
            let Some(s) = weak.upgrade() else {
                break;
            };
            let r = tokio::task::spawn_blocking(move || tick(&s)).await;
            if r.is_err() {
                tracing::error!("collision: residency tick panicked");
            }
        }
    });
}

/// The positions that keep cells loaded in an instance: players and ships.
pub fn focus_points(server: &GameServer, instance_id: u32) -> Vec<(f32, f32)> {
    let Some(world) = server.world.world(instance_id) else {
        return Vec::new();
    };
    let mut out: Vec<(f32, f32)> = world
        .all_characters()
        .iter()
        .map(|p| {
            let p = world.world_pos_rot(&WorldObject::from(p));
            (p.x, p.y)
        })
        .collect();
    for s in world.all_slaves() {
        let p = world.world_pos_rot(&WorldObject::from(&s));
        out.push((p.x, p.y));
    }
    out
}

/// One residency pass over every instance.
pub fn tick(server: &GameServer) {
    let cfg = server.config.physics.clone();
    let sys = &server.systems.ai.collision;
    for world in server.world.worlds() {
        let focus = focus_points(server, world.id);
        if focus.is_empty() && sys.existing(world.id).is_none() {
            continue;
        }
        let Some(pw) = super::phys_world(server, world.id) else {
            continue;
        };
        let r = cfg.load_radius_cells.max(0);
        let mut wanted = Vec::new();
        for (x, y) in &focus {
            if let Some((cx, cy)) = pw.cell_of(*x, *y) {
                for dx in -r..=r {
                    for dy in -r..=r {
                        let c = (cx + dx, cy + dy);
                        if c.0 >= 0
                            && c.1 >= 0
                            && c.0 < pw.cells_x
                            && c.1 < pw.cells_y
                            && !wanted.contains(&c)
                        {
                            wanted.push(c);
                        }
                    }
                }
            }
        }
        for c in &wanted {
            super::load_cell(server, &pw, *c);
        }
        if cfg.unload_after_seconds > 0 {
            let limit = Duration::from_secs(cfg.unload_after_seconds);
            let now = Instant::now();
            let mut st = pw.lock();
            let stale: Vec<(i32, i32)> = st
                .cells
                .iter()
                .filter(|(c, lc)| !wanted.contains(c) && now - lc.last_used > limit)
                .map(|(c, _)| *c)
                .collect();
            for c in stale {
                tracing::debug!("collision: instance {} unloads cell {c:?}", pw.instance_id);
                st.remove_cell(c);
            }
        }
        super::objects::sync_world(server, &pw);
        super::sync_water(server, &pw);
        super::living::sync_world(server, &pw);
        pw.lock().collect_garbage();
    }
    // instances that were destroyed
    for pw in sys.all() {
        if !server.world.has_world(pw.instance_id) {
            sys.remove(pw.instance_id);
        }
    }
    if let Some(m) = sys.models() {
        m.evict_unused();
    }
}

/// Loads every cell of every world template's main instance (`Physics.PreloadAllCells`).
pub fn preload_all(server: &GameServer) {
    let t = Instant::now();
    for world in server.world.worlds() {
        let Some(pw) = super::phys_world(server, world.id) else {
            continue;
        };
        for cx in 0..pw.cells_x {
            for cy in 0..pw.cells_y {
                super::load_cell(server, &pw, (cx, cy));
            }
        }
        let st = pw.lock();
        tracing::info!(
            "collision: preloaded {} ({} cells, {} brushes, {} vegetation)",
            pw.world_name,
            st.cells.len(),
            st.stats.brushes,
            st.stats.vegetation
        );
    }
    tracing::info!("collision: preload took {:?}", t.elapsed());
}
