//! [`StreamWorld`] backed by the ported world / name / item state, installed at startup with
//! `server.streams.set_world(..)`. Doodads are a later phase (every cell is empty).

use std::sync::Arc;

use crate::data::GameData;
use crate::managers::id::IdManagers;
use crate::managers::item::ItemManager;
use crate::managers::name::NameManager;
use crate::stream::world::{ActiveCharacter, ItemUcc, StreamDoodad, StreamWorld};
use crate::world::WorldManager;

/// The game-side lookups of the stream server.
#[derive(Debug)]
pub struct GameStreamWorld {
    pub world: Arc<WorldManager>,
    pub names: Arc<NameManager>,
    pub items: Arc<ItemManager>,
    pub ids: Arc<IdManagers>,
    pub game_data: Arc<GameData>,
    /// `AppConfiguration.Id` (new items' `WorldId`).
    pub server_id: u8,
}

impl StreamWorld for GameStreamWorld {
    fn doodads_in_cell(&self, instance_id: u32, x: i32, y: i32) -> Option<Vec<StreamDoodad>> {
        // An unknown instance is `None` (C# NRE).
        let world = self.world.world(instance_id)?;
        let now = crate::world::now_ms();
        let doodads = world
            .objects_in_cell(x, y)
            .into_iter()
            .filter(|o| o.as_doodad().is_some())
            .map(|o| {
                let pos = world.world_pos_rot(&o);
                let (roll, pitch, yaw) = pos.roll_pitch_yaw_shorts();
                let d = o.as_doodad().expect("doodad").data();
                StreamDoodad {
                    obj_id: o.obj_id(),
                    template_id: d.template_id,
                    x: pos.x,
                    y: pos.y,
                    z: pos.z,
                    roll,
                    pitch,
                    yaw,
                    scale: d.scale,
                    func_group_id: d.func_group_id,
                    time_left: d.time_left(now),
                    plant_time: d.plant_time,
                }
            })
            .collect();
        Some(doodads)
    }

    fn character_name(&self, character_id: u32) -> Option<String> {
        self.names.get_character_name(character_id)
    }

    fn item_ucc(&self, item_id: u64) -> Option<ItemUcc> {
        self.items.item_by_id(item_id).map(|i| ItemUcc {
            item_id: i.id,
            ucc_id: i.ucc_id,
        })
    }

    fn active_character(&self, game_connection_id: u32) -> Option<ActiveCharacter> {
        self.world
            .character_by_connection(game_connection_id)
            .map(|p| {
                let c = p.data();
                ActiveCharacter {
                    id: c.id,
                    name: c.unit.name.clone(),
                }
            })
    }
}
