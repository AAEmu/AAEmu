//! Game-world lookups the stream server needs but which are owned by other subsystems
//! (WorldManager / Doodad, NameManager, ItemManager, the connection's ActiveChar and its
//! inventory). Those subsystems are not ported yet, so [`NullStreamWorld`] answers "not found"
//! everywhere; once they exist, implement [`StreamWorld`] and install it with
//! `server.streams.set_world(..)`.

/// Snapshot of what `TCDoodadStreamPacket` writes for one `Doodad`.
#[derive(Debug, Clone, PartialEq)]
pub struct StreamDoodad {
    /// `ObjId` (written as a 24-bit bc).
    pub obj_id: u32,
    pub template_id: u32,
    /// `Transform.World.Position`.
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// `Transform.World.ToRollPitchYawShorts()`.
    pub roll: i16,
    pub pitch: i16,
    pub yaw: i16,
    pub scale: f32,
    /// `FuncGroupId` (doodad_func_groups id).
    pub func_group_id: u32,
    /// `TimeLeft` (growing).
    pub time_left: u32,
    /// `PlantTime` as `Helpers.UnixTime` (what `PacketStream.Write(DateTime)` sends).
    pub plant_time: i64,
}

/// `ItemManager.GetItemByItemId(id)` -> (`Item.Id`, `Item.UccId`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ItemUcc {
    pub item_id: u64,
    pub ucc_id: u64,
}

/// `connection.GameConnection.ActiveChar` (the members the stream code reads).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ActiveCharacter {
    pub id: u32,
    pub name: String,
}

/// `Item.CrestInk` template id (granted by `UccManager.ConfirmDefaultUcc`).
pub const ITEM_CREST_INK: u32 = 17663;
/// `Item.CrestStamp` template id.
pub const ITEM_CREST_STAMP: u32 = 17662;

pub trait StreamWorld: Send + Sync {
    /// `WorldManager.GetWorld(instanceId).GetInCell<Doodad>(x, y)`. `None` = no such world
    /// instance (a `NullReferenceException` in C#, which closes the stream connection).
    fn doodads_in_cell(&self, instance_id: u32, x: i32, y: i32) -> Option<Vec<StreamDoodad>>;

    /// `NameManager.GetCharacterName(id)`.
    fn character_name(&self, character_id: u32) -> Option<String>;

    /// `ItemManager.GetItemByItemId(itemId)`.
    fn item_ucc(&self, item_id: u64) -> Option<ItemUcc>;

    /// `GameConnectionTable.GetConnection(id)?.ActiveChar`.
    fn active_character(&self, game_connection_id: u32) -> Option<ActiveCharacter>;
}

/// Placeholder until the world / character / item subsystems are ported: every world instance
/// is empty, and no character, name or item exists.
#[derive(Debug, Clone, Copy, Default)]
pub struct NullStreamWorld;

impl StreamWorld for NullStreamWorld {
    fn doodads_in_cell(&self, _instance_id: u32, _x: i32, _y: i32) -> Option<Vec<StreamDoodad>> {
        Some(Vec::new())
    }

    fn character_name(&self, _character_id: u32) -> Option<String> {
        None
    }

    fn item_ucc(&self, _item_id: u64) -> Option<ItemUcc> {
        None
    }

    fn active_character(&self, _game_connection_id: u32) -> Option<ActiveCharacter> {
        None
    }
}
