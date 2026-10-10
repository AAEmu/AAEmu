//! `NpcGameData`: `npc_spawners`, `npc_spawner_npcs` (stub: TODO(npcs)).

use crate::systems::DataLoadCtx;

#[derive(Debug, Default)]
pub struct NpcSpawnerData {}

impl NpcSpawnerData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}
