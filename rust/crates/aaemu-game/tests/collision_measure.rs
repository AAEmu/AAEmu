//! Memory / time of the collision world on the real 1.2 data (gated):
//! `AAEMU_GAME_PAK=D:\AA1.2\game_pak cargo test --release -p aaemu-game --test collision_measure -- --ignored --nocapture`
//! (`AAEMU_MEASURE_CELLS=x0,y0,x1,y1` picks the block of main_world cells, default 9,13,11,15).
//!
//! A counting global allocator gives the heap growth of each phase.

use std::alloc::{GlobalAlloc, Layout, System};
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicIsize, AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Instant;

use aaemu_game::data::client_files::ClientFileManager;
use aaemu_game::data::world_templates::WorldTemplates;
use aaemu_game::systems::ai::collision::cells::read_vegetation_groups;
use aaemu_game::systems::ai::collision::models::ModelCache;
use aaemu_game::systems::ai::collision::query::{mask, ray_flags};
use aaemu_game::systems::ai::collision::terrain::TerrainSource;
use aaemu_game::systems::ai::collision::world::{prepare_cell, PhysWorld};
use aaemu_game::systems::ai::collision::{Files, HeightmapTerrain};
use aaemu_game::systems::npcs::heightmap::{
    HeightmapConfig, HeightmapWorlds, CELL_HMAP_RESOLUTION,
};
use aaemu_physics::math::Vec3;

struct Counting;
static CUR: AtomicIsize = AtomicIsize::new(0);
static PEAK: AtomicUsize = AtomicUsize::new(0);

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, l: Layout) -> *mut u8 {
        let p = unsafe { System.alloc(l) };
        if !p.is_null() {
            let c = CUR.fetch_add(l.size() as isize, Ordering::Relaxed) + l.size() as isize;
            PEAK.fetch_max(c.max(0) as usize, Ordering::Relaxed);
        }
        p
    }
    unsafe fn dealloc(&self, p: *mut u8, l: Layout) {
        unsafe { System.dealloc(p, l) };
        CUR.fetch_sub(l.size() as isize, Ordering::Relaxed);
    }
}

#[global_allocator]
static A: Counting = Counting;

fn mb(b: isize) -> f64 {
    b as f64 / (1024.0 * 1024.0)
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK; run with --release"]
fn measure_main_world_block() {
    let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
        return;
    };
    let block: Vec<i32> = std::env::var("AAEMU_MEASURE_CELLS")
        .unwrap_or_else(|_| "9,13,11,15".into())
        .split(',')
        .filter_map(|v| v.trim().parse().ok())
        .collect();
    let app = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = Arc::new(ClientFileManager::from_sources(&[pak], &app));
    let worlds = WorldTemplates::load(&files, &app).expect("templates");
    let t = worlds
        .templates()
        .iter()
        .find(|t| t.name == "main_world")
        .unwrap()
        .clone();
    let hm = Arc::new(HeightmapWorlds::new(
        files.clone(),
        &worlds,
        HeightmapConfig {
            heightmaps_enable: true,
            geo_data_mode: false,
            pre_load_terrain: false,
        },
    ));
    let base = CUR.load(Ordering::Relaxed);
    let terrain = Arc::new(HeightmapTerrain {
        worlds: hm.clone(),
        template_id: t.id,
        size: (
            t.cell_x * CELL_HMAP_RESOLUTION,
            t.cell_y * CELL_HMAP_RESOLUTION,
        ),
    });
    let pw = PhysWorld::new(
        1,
        t.id,
        "main_world",
        t.cell_x,
        t.cell_y,
        t.ocean_level,
        Some(terrain.clone() as Arc<dyn TerrainSource>),
    );
    let f = Files::Client(files.clone());
    let models = ModelCache::new(Arc::new(f.clone()));
    let xml = files
        .read_file("game/worlds/main_world/vegetation.xml")
        .map(|b| aaemu_game::data::client_files::decode_text(&b))
        .unwrap_or_default();
    let groups: HashMap<_, _> = read_vegetation_groups(&xml)
        .into_iter()
        .map(|g| (g.id, g))
        .collect();
    let after_setup = CUR.load(Ordering::Relaxed);
    let t0 = Instant::now();
    let mut ncells = 0;
    for cx in block[0]..=block[2] {
        for cy in block[1]..=block[3] {
            let c = prepare_cell(&f, &models, "main_world", &groups, (cx, cy), true, true);
            pw.end_load(Some(c), (cx, cy));
            // the cell's terrain samples
            terrain.sample(cx * 512 + 1, cy * 512 + 1);
            ncells += 1;
        }
    }
    let load = t0.elapsed();
    let after_load = CUR.load(Ordering::Relaxed);
    let st = pw.lock();
    println!(
        "{ncells} cells: {} brushes, {} vegetation, {} static entities; {} models ({} KB physics blobs)",
        st.stats.brushes,
        st.stats.vegetation,
        st.stats.static_entities,
        models.len(),
        models.blob_bytes() / 1024
    );
    drop(st);
    println!(
        "load {:?} ({:?} per cell); heap: world+groups {:.1} MB, cells+models+heightmaps {:.1} MB, peak {:.1} MB",
        load,
        load / ncells.max(1) as u32,
        mb(after_setup - base),
        mb(after_load - after_setup),
        mb(PEAK.load(Ordering::Relaxed) as isize - base)
    );
    // ray throughput (down rays over the block, mask 0x101)
    let n = 20_000;
    let x0 = block[0] as f32 * 1024.0;
    let y0 = block[1] as f32 * 1024.0;
    let w = (block[2] - block[0] + 1) as f32 * 1024.0;
    let h = (block[3] - block[1] + 1) as f32 * 1024.0;
    let mut hits = 0;
    let tq = Instant::now();
    let mut st = pw.lock();
    for i in 0..n {
        let fx = x0 + (i as f32 * 37.13) % w;
        let fy = y0 + (i as f32 * 91.71) % h;
        if st
            .raycast(
                Vec3::new(fx, fy, 2000.0),
                Vec3::new(fx, fy, -100.0),
                mask::STATIC_TERRAIN,
                ray_flags::DEFAULT,
                &[],
            )
            .is_some()
        {
            hits += 1;
        }
    }
    let rays = tq.elapsed();
    println!(
        "{n} vertical rays: {hits} hits, {:?} per ray",
        rays / n as u32
    );
    assert!(hits > n / 2);
}
