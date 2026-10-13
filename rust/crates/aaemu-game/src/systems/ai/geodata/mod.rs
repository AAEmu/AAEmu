//! Geodata (navmesh) for the AI: the CryEngine `.bai` files ([`bai`]), `AiGeoDataManager`
//! ([`manager`]) and the A* `PathNode` ([`astar`]), plus the world-side entry points the AI and
//! the height queries use (`Npc.FindPath`, `WorldTemplate.GeoData.GetHeight`).
//!
//! Every world template gets an `AiGeoData` at startup (`WorldManager.LoadWorlds`); the `.bai`
//! files are only read with `World.GeoDataMode` (zone folders at startup like
//! `LoadZoneBaiFiles`, the 4x4 path blocks of a cell when the cell is first used).

pub mod astar;
pub mod bai;
pub mod graph;
pub mod manager;
#[cfg(test)]
mod real_data;

use std::collections::HashMap;
use std::sync::{Arc, RwLock};

use crate::data::client_files::ClientFileManager;
use crate::server::GameServer;
use crate::systems::npcs::heightmap::HeightmapWorlds;
use crate::systems::skills::controllers::Vec3;
use crate::world::WorldObject;

use self::bai::{BaiFiles, UsedAreaNames};
use self::manager::{AiGeoData, GeoError, TerrainHeights};
use super::framework::AiCtx;

/// The client files as a bai source.
pub struct ClientBaiFiles(pub Arc<ClientFileManager>);

impl BaiFiles for ClientBaiFiles {
    fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
        self.0.files_in_directory(dir, pattern, true)
    }

    fn file_exists(&self, name: &str) -> bool {
        self.0.file_exists(name)
    }

    fn read_file(&self, name: &str) -> Option<Vec<u8>> {
        self.0.read_file(name)
    }
}

/// The heightmap of one world template as the geodata's terrain fallback.
pub struct HeightmapTerrain {
    pub worlds: Arc<HeightmapWorlds>,
    pub template_id: u32,
}

impl TerrainHeights for HeightmapTerrain {
    fn raw_height(&self, x: i32, y: i32) -> Result<f32, GeoError> {
        match self.worlds.template(self.template_id) {
            Some(t) => t
                .get_raw_height_map_height(self.worlds.files(), x, y)
                .map_err(|_| GeoError),
            None => Ok(0.0),
        }
    }

    fn height(&self, x: f32, y: f32) -> Result<f32, GeoError> {
        self.worlds
            .world_height(self.template_id, x, y)
            .map_err(|_| GeoError)
    }
}

/// `WorldTemplate.GeoData` of every template (`server.systems.ai.geodata`).
#[derive(Debug, Default)]
pub struct GeoDataStore {
    by_template: RwLock<HashMap<u32, Arc<AiGeoData>>>,
}

impl GeoDataStore {
    /// The geodata of a world template.
    pub fn get(&self, template_id: u32) -> Option<Arc<AiGeoData>> {
        self.by_template
            .read()
            .expect("geodata poisoned")
            .get(&template_id)
            .cloned()
    }

    /// Installs one template's geodata (startup / tests).
    pub fn install(&self, g: Arc<AiGeoData>) {
        self.by_template
            .write()
            .expect("geodata poisoned")
            .insert(g.template_id, g);
    }

    pub fn len(&self) -> usize {
        self.by_template.read().expect("geodata poisoned").len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

/// Startup: an `AiGeoDataManager` per world template (`worldTemplate.GeoData.Load()` is a
/// no-op) and, with `GeoDataMode`, `LoadZoneBaiFiles` for every template.
pub fn start(server: &Arc<GameServer>) {
    let Some(heights) = server.systems.npcs.heightmaps.get() else {
        return;
    };
    let enabled = server.config.world.geo_data_mode;
    let files: Arc<dyn BaiFiles + Send + Sync> = Arc::new(ClientBaiFiles(heights.files().clone()));
    let used = Arc::new(UsedAreaNames::default());
    let mut all: HashMap<u32, Arc<AiGeoData>> = HashMap::new();
    for t in server.game_data.worlds.templates() {
        let terrain: Arc<dyn TerrainHeights> = Arc::new(HeightmapTerrain {
            worlds: heights.clone(),
            template_id: t.id,
        });
        let g = Arc::new(AiGeoData::new(
            t,
            enabled,
            files.clone(),
            used.clone(),
            Some(terrain),
        ));
        if enabled {
            g.zone_bai();
        }
        all.insert(t.id, g.clone());
        server.systems.ai.geodata.install(g);
    }
    // WorldManager.GetHeight: the navmesh first with GeoDataMode
    heights.install_geo_data(Arc::new(move |template_id, x, y, z| {
        all.get(&template_id)
            .map_or(0.0, |g| g.get_height(Vec3::new(x, y, z)))
    }));
}

/// The geodata of the world an object is in.
fn geo_of(server: &GameServer, obj: &WorldObject) -> Option<Arc<AiGeoData>> {
    let world = crate::systems::skills::geometry::world_of(server, obj)?;
    server.systems.ai.geodata.get(world.template.id)
}

/// `ParentWorld.Template.GeoData.GetHeight(pos)` (raw heightmap fallback; 0 without data).
pub fn geo_data_height(server: &GameServer, obj: &WorldObject, pos: Vec3) -> f32 {
    match geo_of(server, obj) {
        Some(g) => g.get_height(pos),
        None => 0.0,
    }
}

/// `Npc.FindPath(abuser)`: an A* route from the NPC to the abuser, reduced, as `FoundPath`; a
/// chased player gets the debug chat lines the C# sends.
pub fn npc_find_path(c: &mut AiCtx<'_>, abuser: &WorldObject) {
    let me = super::npc::world_pos(c.server, &c.obj);
    let target = super::npc::world_pos(c.server, abuser);
    c.ai.path_node.start_point_pos = me;
    c.ai.path_node.end_point_pos = target;
    c.ai.path_node.zone_key = c.obj.transform().zone_id;
    c.ai.path_node.found_path.clear();
    let Some(geo) = geo_of(c.server, &c.obj) else {
        return;
    };
    let (start, end) = (c.ai.path_node.start_point_pos, c.ai.path_node.end_point_pos);
    let result = c.ai.path_node.find_path(&geo, start, end);
    // The search resolves the destination to a graph node. Chase regeneration must
    // compare with the victim's actual position, not that triangle's centroid.
    c.ai.path_node.end_point_pos = target;
    let mut res_list = match result {
        Ok(r) => r,
        Err(e) => {
            tracing::error!("FindPath for npc {}: {}", c.npc.obj_id, e.0);
            return;
        }
    };
    // The enclosing nodes select topology, not mandatory detours through the start
    // and goal triangle centroids. Native direct paths (0x31062728) use actor positions.
    if let Some(first) = res_list.first_mut() {
        *first = me;
    }
    if res_list.len() > 1 {
        *res_list.last_mut().expect("nonempty path") = target;
    }
    if res_list.last() != Some(&target) {
        res_list.push(target);
    }
    let reduced = match geo.reduce_path(&res_list, 10) {
        Ok(r) => r,
        Err(GeoError) => {
            tracing::error!(
                "ReducePath for npc {}: NullReferenceException (no bai at the start node)",
                c.npc.obj_id
            );
            return;
        }
    };
    c.ai.path_node.found_path = reduced;
    if let Some(p) = abuser.as_player() {
        crate::world::session::send_message(
            p,
            -2,
            &format!(
                "Aggro from {}, getting attack path in {}/{} steps",
                c.npc.obj_id,
                c.ai.path_node.found_path.len(),
                res_list.len()
            ),
        );
        for v in &c.ai.path_node.found_path {
            crate::world::session::send_message(
                p,
                -2,
                &format!("Path step -> <{}, {}, {}>", v.x, v.y, v.z),
            );
        }
    }
}
