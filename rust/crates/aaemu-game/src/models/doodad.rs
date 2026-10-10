//! `Models/Game/DoodadObj/Doodad`: identity, the persistent / owner fields and the `Write`
//! wire format used by `SCDoodadCreatedPacket` / `SCDoodadsCreatedPacket`. Funcs, phases,
//! timers, `Use`, persistence and spawners belong to the doodad subsystem (`systems::npcs`),
//! which keeps the cached values below up to date (`current_func_types`, `final_phase_end_ms`).

use aaemu_commons::PacketStream;

use crate::models::buff::Buffs;
use crate::models::item::UnixTime;
use crate::models::transform::{PositionAndRotation, Transform};
use crate::models::unit::{BaseUnitData, GameObjectData};

/// `DoodadOwnerType`.
pub mod doodad_owner_type {
    pub const SLAVE: u8 = 2;
    pub const HOUSING: u8 = 3;
    pub const CHARACTER: u8 = 254;
    pub const SYSTEM: u8 = 255;
}

/// `Doodad.s_funcDrivenLootFuncTypes`.
const FUNC_DRIVEN_LOOT_FUNC_TYPES: [&str; 4] = [
    "DoodadFuncLootItem",
    "DoodadFuncLootPack",
    "DoodadFuncRecoverItem",
    "DoodadFuncCutdowning",
];

/// `Doodad.IsFuncDrivenLootFunc(funcType)`.
pub fn is_func_driven_loot_func(func_type: &str) -> bool {
    FUNC_DRIVEN_LOOT_FUNC_TYPES.contains(&func_type)
}

/// `Doodad` (a `BaseUnit`, not a `Unit`).
#[derive(Debug, Clone, PartialEq)]
pub struct Doodad {
    // --- GameObject / BaseUnit ---
    pub transform: Transform,
    pub template_id: u32,
    pub name: String,
    pub faction_id: u32,
    /// `Scale` (`_scale`).
    pub scale: f32,
    pub buffs: Buffs,
    // --- Doodad ---
    /// `DbId` (doodads table id; 0 = not persistent).
    pub db_id: u32,
    pub is_persistent: bool,
    /// `FuncGroupId` (current phase).
    pub func_group_id: u32,
    /// `FuncType` of each entry of `CurrentFuncs` (what `Write`'s `hasLootItem` needs).
    pub current_func_types: Vec<String>,
    pub item_id: u64,
    pub ucc_id: u64,
    pub item_template_id: u32,
    /// `GrowthTime` (unix ms, 0 = MinValue).
    pub growth_time_ms: i64,
    /// `PlantTime` (unix seconds as `Helpers.UnixTime`).
    pub plant_time: UnixTime,
    /// `PhaseTime` (unix ms).
    pub phase_time_ms: i64,
    /// End of the current phase's `DoodadFuncFinal { After > 0 }` (`PhaseTime + After`), when
    /// the phase has one; computed by the doodad subsystem from the phase funcs.
    pub final_phase_end_ms: Option<i64>,
    /// `OwnerId` (character id, DB relative).
    pub owner_id: u32,
    pub owner_obj_id: u32,
    pub parent_obj_id: u32,
    /// `DoodadOwnerType` ([`doodad_owner_type`]).
    pub owner_type: u8,
    /// `AttachPointKind`.
    pub attach_point: u8,
    /// `OwnerDbId` (e.g. the house id).
    pub owner_db_id: u32,
    pub type2: u32,
    pub data: i32,
    /// `FarmGroupKind`.
    pub farm_type: u8,
    pub quest_glow: u32,
    pub puzzle_group: i32,
    /// `Spawner?.Id` (0 = none).
    pub spawner_id: u32,
}

impl Doodad {
    /// `new Doodad()` for `template_id` (`DoodadManager.Create` fills the rest).
    pub fn new(template_id: u32) -> Self {
        Self {
            transform: Transform::default(),
            template_id,
            name: String::new(),
            faction_id: 0,
            scale: 1.0,
            buffs: Buffs::default(),
            db_id: 0,
            is_persistent: false,
            func_group_id: 0,
            current_func_types: Vec::new(),
            item_id: 0,
            ucc_id: 0,
            item_template_id: 0,
            growth_time_ms: 0,
            plant_time: 0,
            phase_time_ms: 0,
            final_phase_end_ms: None,
            owner_id: 0,
            owner_obj_id: 0,
            parent_obj_id: 0,
            owner_type: doodad_owner_type::SYSTEM,
            attach_point: crate::models::unit::attach_point::SYSTEM,
            owner_db_id: 0,
            type2: 0,
            data: 0,
            farm_type: 0,
            quest_glow: 0,
            puzzle_group: -1,
            spawner_id: 0,
        }
    }

    /// `TimeLeft` (ms).
    pub fn time_left(&self, now_ms: i64) -> u32 {
        if let Some(end) = self.final_phase_end_ms {
            let left = (end - now_ms) as f64;
            return left.max(1.0).round_ties_even() as u32;
        }
        if self.growth_time_ms > now_ms {
            return (self.growth_time_ms - now_ms) as u32;
        }
        0
    }

    /// `hasLootItem` of `Write`: the phase is driven exclusively by loot / recover funcs.
    pub fn has_loot_item(&self) -> bool {
        !self.current_func_types.is_empty()
            && self
                .current_func_types
                .iter()
                .all(|f| is_func_driven_loot_func(f))
    }

    /// `Doodad.Write(stream)`. `world` is `Transform.World` (equal to the local transform for a
    /// doodad without a parent; see `WorldInstance::world_pos_rot`).
    pub fn write(
        &self,
        obj_id: u32,
        world: &PositionAndRotation,
        now_ms: i64,
        s: &mut PacketStream,
    ) {
        s.write_bc(obj_id)
            .write_u32(self.template_id)
            .write_bc(self.owner_obj_id)
            .write_bc(self.parent_obj_id)
            .write_u8(self.attach_point);
        // Note: AttachPointKind.System (255) is "> 0" too, as in the C# (`AttachPoint > 0`).
        let p = if self.attach_point > 0 || self.parent_obj_id > 0 {
            self.transform.local()
        } else {
            *world
        };
        let (roll, pitch, yaw) = p.roll_pitch_yaw_shorts();
        s.write_position(p.x, p.y, p.z)
            .write_i16(roll)
            .write_i16(pitch)
            .write_i16(yaw)
            .write_f32(self.scale)
            .write_bool(self.has_loot_item())
            .write_u32(self.func_group_id)
            .write_u32(self.owner_id)
            .write_u64(self.ucc_id)
            .write_u32(self.item_template_id)
            .write_u32(self.type2)
            .write_u32(self.time_left(now_ms))
            .write_i64(self.plant_time)
            .write_u32(self.quest_glow)
            .write_i32(0) // family
            .write_i32(self.puzzle_group)
            .write_u8(self.owner_type)
            .write_u32(self.owner_db_id)
            .write_i32(self.data);
    }
}

impl GameObjectData for Doodad {
    fn transform(&self) -> &Transform {
        &self.transform
    }
    fn transform_mut(&mut self) -> &mut Transform {
        &mut self.transform
    }
}

impl BaseUnitData for Doodad {
    fn name(&self) -> &str {
        &self.name
    }
    fn template_id(&self) -> u32 {
        self.template_id
    }
    fn faction_id(&self) -> u32 {
        self.faction_id
    }
    fn scale(&self) -> f32 {
        self.scale
    }
    fn buffs(&self) -> &Buffs {
        &self.buffs
    }
    fn buffs_mut(&mut self) -> &mut Buffs {
        &mut self.buffs
    }
}

/// The `Doodad` members the phase machine (`Use` / `DoFunc` / `DoPhaseFuncs`) and the func
/// timers work with (`systems::npcs::doodad`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadPhaseState {
    /// `ToNextPhase`.
    pub to_next_phase: bool,
    /// `OverridePhase`.
    pub override_phase: i32,
    /// `PhaseRatio` (0..10000, rolled per phase func).
    pub phase_ratio: i32,
    /// `CumulativePhaseRatio`.
    pub cumulative_phase_ratio: i32,
    /// `OverridePhaseTime` (unix ms, 0 = MinValue).
    pub override_phase_time_ms: i64,
    /// `ListGroupId` (recursion guard of `DoPhaseFuncs`).
    pub list_group_id: Vec<u32>,
    /// `CurrentToDTriggers` (tod hours, next phase) in insertion order.
    pub tod_triggers: Vec<(f32, i32)>,
    /// `FuncTask`: generation of the pending func timer (0 = none). A timer task only fires
    /// when the generation it was created with is still current (`FuncTask.Cancel` bumps it).
    pub func_task: u64,
    /// `_deleted`.
    pub deleted: bool,
    /// `Despawn` (unix ms, 0 = MinValue) / `Respawn` (GameObject members).
    pub despawn_ms: i64,
    pub respawn_ms: i64,
}
