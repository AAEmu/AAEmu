//! Port of the persistence + wire parts of `AAEmu.Game.Models.Game.Items.Item` and its
//! subclasses (`EquipItem`/`Armor`/`Weapon`/`Accessory`, `BodyPart`, `UccItem`, `BigFish`,
//! `MusicSheetItem`, `SummonMate`, `SummonSlave`, `TreasureMap`, `Backpack`, `Rune`).
//!
//! Persistence (`ItemManager.LoadUserItems` / `Save`), creation (`ItemManager.Create` with the
//! subclass constructors) and `Item.Write`. Gameplay behaviour (stats, repair cost, expiration
//! timers, ...) is a later phase.

use std::collections::HashMap;

use aaemu_commons::{Marshal, PacketStream};

use crate::data::formulas::{Formula, FormulaManager, WearableFormulaType};
use crate::data::items::{
    bind_type, FullGradeTemplate, Holdable, ItemData, ItemTemplate, TemplateKind,
};

/// Unix seconds, already clamped like `Helpers.UnixTime(DateTime)` (anything before the epoch is 0).
pub type UnixTime = i64;

/// `ItemDetailType`.
pub mod detail_type {
    pub const INVALID: u8 = 0;
    pub const EQUIPMENT: u8 = 1;
    pub const SLAVE: u8 = 2;
    pub const MATE: u8 = 3;
    pub const UCC: u8 = 4;
    pub const TREASURE: u8 = 5;
    pub const BIG_FISH: u8 = 6;
    pub const DECORATION: u8 = 7;
    pub const MUSIC_SHEET: u8 = 8;
    pub const GLIDER: u8 = 9;
    pub const SLAVE_EQUIPMENT: u8 = 10;
    pub const LOCATION: u8 = 11;
    pub const TYPE_MAX: u8 = 12;
}

/// `ItemFlag.SoulBound`.
pub const ITEM_FLAG_SOUL_BOUND: u8 = 0x01;
/// `ItemFlag.HasUCC`.
pub const ITEM_FLAG_HAS_UCC: u8 = 0x02;

/// `ItemBindType.BindOnUnpack`.
pub const BIND_ON_UNPACK: u32 = 4;

/// The concrete C# item class, resolved from the `items.type` column
/// (e.g. `AAEmu.Game.Models.Game.Items.Armor`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ItemClass {
    Item,
    EquipItem,
    Armor,
    Weapon,
    Accessory,
    Backpack,
    BodyPart,
    Rune,
    UccItem,
    BigFish,
    MusicSheetItem,
    SummonMate,
    SummonSlave,
    TreasureMap,
}

impl ItemClass {
    /// `Type.GetType(itemType)`. Returns `None` for unknown types; C# then falls back to the
    /// template's `ClassType`, which needs the item template tables (later phase).
    pub fn from_type_name(name: &str) -> Option<Self> {
        let short = name.rsplit('.').next().unwrap_or(name);
        Some(match short {
            "Item" => Self::Item,
            "EquipItem" => Self::EquipItem,
            "Armor" => Self::Armor,
            "Weapon" => Self::Weapon,
            "Accessory" => Self::Accessory,
            "Backpack" => Self::Backpack,
            "BodyPart" => Self::BodyPart,
            "Rune" => Self::Rune,
            "UccItem" => Self::UccItem,
            "BigFish" => Self::BigFish,
            "MusicSheetItem" => Self::MusicSheetItem,
            "SummonMate" => Self::SummonMate,
            "SummonSlave" => Self::SummonSlave,
            "TreasureMap" => Self::TreasureMap,
            _ => return None,
        })
    }

    /// `item.GetType().ToString()` (the `items.type` column).
    pub fn type_name(self) -> &'static str {
        match self {
            Self::Item => "AAEmu.Game.Models.Game.Items.Item",
            Self::EquipItem => "AAEmu.Game.Models.Game.Items.EquipItem",
            Self::Armor => "AAEmu.Game.Models.Game.Items.Armor",
            Self::Weapon => "AAEmu.Game.Models.Game.Items.Weapon",
            Self::Accessory => "AAEmu.Game.Models.Game.Items.Accessory",
            Self::Backpack => "AAEmu.Game.Models.Game.Items.Backpack",
            Self::BodyPart => "AAEmu.Game.Models.Game.Items.BodyPart",
            Self::Rune => "AAEmu.Game.Models.Game.Items.Rune",
            Self::UccItem => "AAEmu.Game.Models.Game.Items.UccItem",
            Self::BigFish => "AAEmu.Game.Models.Game.Items.BigFish",
            Self::MusicSheetItem => "AAEmu.Game.Models.Game.Items.MusicSheetItem",
            Self::SummonMate => "AAEmu.Game.Models.Game.Items.SummonMate",
            Self::SummonSlave => "AAEmu.Game.Models.Game.Items.SummonSlave",
            Self::TreasureMap => "AAEmu.Game.Models.Game.Items.TreasureMap",
        }
    }

    pub fn is_equip(self) -> bool {
        matches!(
            self,
            Self::EquipItem | Self::Armor | Self::Weapon | Self::Accessory
        )
    }

    /// The (overridden) `DetailType` property. Base `Item` instances loaded from the DB keep the
    /// default `Invalid`.
    pub fn detail_type(self) -> u8 {
        match self {
            c if c.is_equip() => detail_type::EQUIPMENT,
            Self::UccItem => detail_type::UCC,
            Self::BigFish => detail_type::BIG_FISH,
            Self::MusicSheetItem => detail_type::MUSIC_SHEET,
            Self::SummonMate => detail_type::MATE,
            Self::SummonSlave => detail_type::SLAVE,
            Self::TreasureMap => detail_type::TREASURE,
            _ => detail_type::INVALID,
        }
    }

    /// `DetailBytesLength`: `ReadDetails` is skipped when fewer bytes are available.
    fn detail_bytes_length(self) -> usize {
        match self {
            c if c.is_equip() => 55,
            Self::UccItem => 9,
            Self::BigFish => 16,
            Self::MusicSheetItem => 8,
            Self::SummonMate => 6,
            Self::SummonSlave => 29,
            Self::TreasureMap => 24,
            _ => 0,
        }
    }
}

/// Class-specific detail fields (what each subclass' `ReadDetails` keeps).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ItemDetails {
    // EquipItem
    pub image_item_template_id: u32,
    pub durability: u8,
    pub rune_id: u32,
    pub dye_item_id: u32,
    pub gem_ids: [u32; 7],
    pub temper_physical: u16,
    pub temper_magical: u16,
    // MusicSheetItem
    pub song_id: u32,
    // SummonMate
    pub mate_exp: i32,
    /// The "dead" byte of the 1.2 SummonMate detail (client `GetTotalRepairsForPetItems`): a
    /// non-zero value marks a corpse that only a stablehand revive (`CSRepairPetItems`) clears.
    pub mate_dead: u8,
    pub mate_level: u8,
    // SummonSlave
    pub slave_type: u8,
    pub slave_db_id: u32,
    pub slave_is_destroyed: u8,
    /// `SummonSlave.RepairStartTime` (unix seconds, 0 = `DateTime.MinValue`). Never restored
    /// from the DB (the C# `ReadDetails` always falls back to `MinValue`).
    pub slave_repair_start_time: UnixTime,
    // TreasureMap
    pub map_position_x: i64,
    pub map_position_y: i64,
    // BigFish (in memory only: `ReadDetails` discards them)
    pub fish_weight: f32,
    pub fish_length: f32,
}

/// One row of the `items` table plus the template facts the wire format depends on.
#[derive(Debug, Clone, PartialEq)]
pub struct Item {
    pub class: ItemClass,
    pub id: u64,
    pub template_id: u32,
    pub slot: i32,
    pub grade: u8,
    pub flags: u8,
    pub count: i32,
    pub lifespan_mins: i32,
    pub made_unit_id: u32,
    /// `Item()` sets this to `AppConfiguration.Id`; it is not stored in the DB.
    pub world_id: u8,
    pub create_time: UnixTime,
    pub unsecure_time: UnixTime,
    pub unpack_time: UnixTime,
    pub charge_start_time: UnixTime,
    pub ucc_id: u64,
    pub details: ItemDetails,
    /// `Template.BindType == BindOnUnpack` (selects `UnpackTime` vs `ChargeStartTime` in
    /// `EquipItem.WriteDetails`).
    pub bind_on_unpack: bool,
    /// `OwnerId` (character id).
    pub owner_id: u64,
    /// `SlotType`.
    pub slot_type: u8,
    /// `ExpirationTime` (unix seconds, 0 = MinValue).
    pub expiration_time: UnixTime,
    pub expiration_online_minutes_left: f64,
    pub charge_count: i32,
    /// `IsDirty` (needs to be written by the next `ItemManager.Save`).
    pub dirty: bool,
}

/// Raw `items` columns, as read by `ItemManager.LoadUserItems`.
#[derive(Debug, Clone, Default)]
pub struct ItemRow {
    pub type_name: String,
    pub id: u64,
    pub template_id: u32,
    pub slot: i32,
    pub count: i32,
    pub lifespan_mins: i32,
    pub made_unit_id: u32,
    pub unsecure_time: UnixTime,
    pub unpack_time: UnixTime,
    pub created_at: UnixTime,
    pub flags: u8,
    pub ucc: u32,
    pub details: Vec<u8>,
    pub grade: u8,
    pub charge_time: UnixTime,
    pub owner: u64,
    pub container_id: u64,
    pub slot_type: u8,
    pub expire_time: UnixTime,
    pub expire_online_minutes: f64,
    pub charge_count: i32,
}

impl Item {
    /// Mirrors the per-row body of `ItemManager.LoadUserItems`.
    ///
    /// `template` is `None` when the client game data is unavailable; C# would throw on
    /// `item.Template.FixedGrade`, here the DB grade is kept and `BindType` is treated as not
    /// `BindOnUnpack` (documented later-phase gap).
    pub fn from_row(row: ItemRow, template: Option<&ItemTemplate>, world_id: u8) -> Self {
        // `Type.GetType(itemType)`, falling back to the template's `ClassType` (an unknown type
        // without a template is skipped by `LoadUserItems`; here it becomes a plain `Item`).
        let class = ItemClass::from_type_name(&row.type_name).unwrap_or_else(|| {
            let class = template.map_or(ItemClass::Item, ItemTemplate::class_type);
            tracing::info!(
                "Item {} defined as {} in the database is being restored using template {} with class {:?}",
                row.id,
                row.type_name,
                row.template_id,
                class
            );
            class
        });
        let mut item = Item {
            class,
            id: row.id,
            template_id: row.template_id,
            slot: row.slot,
            grade: 0,
            flags: row.flags,
            count: row.count,
            lifespan_mins: row.lifespan_mins,
            made_unit_id: row.made_unit_id,
            world_id,
            create_time: row.created_at,
            unsecure_time: row.unsecure_time,
            unpack_time: row.unpack_time,
            charge_start_time: 0,
            ucc_id: 0,
            details: ItemDetails::default(),
            bind_on_unpack: template.is_some_and(|t| t.bind_type == BIND_ON_UNPACK),
            owner_id: row.owner,
            slot_type: row.slot_type,
            expiration_time: row.expire_time,
            expiration_online_minutes_left: row.expire_online_minutes,
            charge_count: row.charge_count,
            dirty: false,
        };
        // `item.UccId = ucc` (setter toggles HasUCC) happens before ReadDetails.
        item.set_ucc_id(u64::from(row.ucc));
        item.read_details(&row.details);
        item.grade = match template {
            Some(t) if t.fixed_grade >= 0 => t.fixed_grade as u8,
            Some(t) if t.gradable => row.grade,
            Some(_) => 0,
            None => row.grade,
        };
        // ChargeStartTime from the DB column overrides the one read from the details blob.
        item.charge_start_time = row.charge_time;
        item
    }

    /// `ItemManager.Create(templateId, count, grade)` for an already allocated `id`: runs the
    /// subclass constructor (`EquipItem` default dye, `Armor/Weapon/Accessory` durability), then
    /// the grade / bind / fixed-grade rules. As in C#, a constructor that throws (missing grade
    /// template for the durability formula) falls back to a plain `Item`.
    pub fn create(
        id: u64,
        template: &ItemTemplate,
        count: i32,
        grade: u8,
        world_id: u8,
        data: &ItemData,
        now: UnixTime,
    ) -> Self {
        Self::create_ex(id, template, count, grade, world_id, data, now, None)
    }

    /// [`Item::create`] with the target `TreasureMap.GenerateTreasureMapData` picked (`None`: no
    /// treasure chest spawners known, as with the npcs subsystem not installed).
    #[allow(clippy::too_many_arguments)]
    pub fn create_ex(
        id: u64,
        template: &ItemTemplate,
        count: i32,
        grade: u8,
        world_id: u8,
        data: &ItemData,
        now: UnixTime,
        treasure: Option<TreasureMapTarget>,
    ) -> Self {
        let mut class = template.class_type();
        let mut details = ItemDetails::default();
        if let Some(e) = template.equip() {
            // `Durability = MaxDurability` runs while Grade is still 0.
            match data.max_durability(template, 0) {
                Some(d) => {
                    details.durability = d;
                    details.dye_item_id = e.default_dye_item_id;
                }
                None => {
                    tracing::error!(
                        "Item {id}: {:?} constructor failed (no grade template), creating a plain Item",
                        class
                    );
                    class = ItemClass::Item;
                }
            }
        }
        let mut item = Item {
            class,
            id,
            template_id: template.id,
            slot: -1,
            grade: 0,
            flags: 0,
            count,
            lifespan_mins: 0,
            made_unit_id: 0,
            world_id,
            create_time: now,
            unsecure_time: 0,
            unpack_time: 0,
            charge_start_time: 0,
            ucc_id: 0,
            details,
            bind_on_unpack: template.bind_type == BIND_ON_UNPACK,
            owner_id: 0,
            slot_type: 0,
            expiration_time: 0,
            expiration_online_minutes_left: 0.0,
            charge_count: 0,
            dirty: true,
        };
        if class == ItemClass::TreasureMap {
            item.generate_treasure_map_data(treasure);
        }
        // If item already has a default generated grade, then do not override it
        if item.grade == 0 {
            item.grade = grade;
        }
        if template.bind_type == bind_type::BIND_ON_PICKUP {
            item.flags |= ITEM_FLAG_SOUL_BOUND;
        }
        if template.fixed_grade >= 0 {
            item.grade = template.fixed_grade as u8;
        }
        item
    }

    /// `new Item(worldId)` (`new Item()` passes `AppConfiguration.Id`): a plain `Item` with no
    /// template, slot -1, dirty.
    pub fn new(world_id: u8) -> Self {
        Item {
            class: ItemClass::Item,
            id: 0,
            template_id: 0,
            slot: -1,
            grade: 0,
            flags: 0,
            count: 0,
            lifespan_mins: 0,
            made_unit_id: 0,
            world_id,
            create_time: 0,
            unsecure_time: 0,
            unpack_time: 0,
            charge_start_time: 0,
            ucc_id: 0,
            details: ItemDetails::default(),
            bind_on_unpack: false,
            owner_id: 0,
            slot_type: 0,
            expiration_time: 0,
            expiration_online_minutes_left: 0.0,
            charge_count: 0,
            dirty: true,
        }
    }

    /// `new Item(worldId, id, template, count)` (`new Item(id, template, count)` passes
    /// `AppConfiguration.Id`): the base-class constructor only (no subclass, grade or bind
    /// rules; see [`Item::create`] for `ItemManager.Create`).
    pub fn with_template(world_id: u8, id: u64, template: &ItemTemplate, count: i32) -> Self {
        Item {
            id,
            template_id: template.id,
            count,
            bind_on_unpack: template.bind_type == BIND_ON_UNPACK,
            ..Self::new(world_id)
        }
    }

    /// `Grade` setter (marks the item dirty).
    pub fn set_grade(&mut self, v: u8) {
        self.grade = v;
        self.dirty = true;
    }

    /// `Count` setter (marks the item dirty).
    pub fn set_count(&mut self, v: i32) {
        self.count = v;
        self.dirty = true;
    }

    /// `Slot` setter (marks the item dirty).
    pub fn set_slot(&mut self, v: i32) {
        self.slot = v;
        self.dirty = true;
    }

    /// `UccId` setter.
    pub fn set_ucc(&mut self, v: u64) {
        self.set_ucc_id(v);
        self.dirty = true;
    }

    /// `DetailType` + `WriteDetails` (the 128-byte detail block of item task packets, unpadded).
    pub fn detail_type_and_details(&self) -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u8(self.class.detail_type());
        self.write_details(&mut s);
        s.into_bytes()
    }

    /// `WriteDetails` into a fresh stream (the `items.details` column).
    pub fn details_blob(&self) -> Vec<u8> {
        let mut s = PacketStream::new();
        self.write_details(&mut s);
        s.into_bytes()
    }

    fn set_ucc_id(&mut self, v: u64) {
        self.ucc_id = v;
        if v > 0 {
            self.flags |= ITEM_FLAG_HAS_UCC;
        } else {
            self.flags &= !ITEM_FLAG_HAS_UCC;
        }
    }

    /// The subclass `ReadDetails` overrides, applied to the `details` blob.
    fn read_details(&mut self, blob: &[u8]) {
        let need = self.class.detail_bytes_length();
        if need == 0 || blob.len() < need {
            // Base Item.ReadDetails with DetailType Invalid reads nothing; subclasses bail out
            // when `LeftBytes < DetailBytesLength`.
            return;
        }
        let mut s = PacketStream::from_bytes(blob.to_vec());
        // Lengths were checked above, so the reads below cannot run out of data.
        let r = (|| -> aaemu_commons::packet_stream::Result<()> {
            let d = &mut self.details;
            match self.class {
                c if c.is_equip() => {
                    d.image_item_template_id = s.read_u32()?;
                    d.durability = s.read_u8()?;
                    s.read_i16()?;
                    d.rune_id = s.read_u32()?;
                    let _charge_start = s.read_i64()?; // overwritten by the charge_time column
                    d.dye_item_id = s.read_u32()?;
                    for g in d.gem_ids.iter_mut() {
                        *g = s.read_u32()?;
                    }
                    d.temper_physical = s.read_u16()?;
                    d.temper_magical = s.read_u16()?;
                }
                ItemClass::UccItem => {
                    let ucc = s.read_u64()?;
                    let _ = s.read_u8()?;
                    self.ucc_id = ucc;
                    if ucc > 0 {
                        self.flags |= ITEM_FLAG_HAS_UCC;
                    } else {
                        self.flags &= !ITEM_FLAG_HAS_UCC;
                    }
                }
                ItemClass::BigFish => {
                    // Weight / Length / capture date are read and discarded (C# quirk).
                }
                ItemClass::MusicSheetItem => d.song_id = s.read_u64()? as u32,
                ItemClass::SummonMate => {
                    d.mate_exp = s.read_i32()?;
                    d.mate_dead = s.read_u8()?;
                    d.mate_level = s.read_u8()?;
                }
                ItemClass::SummonSlave => {
                    d.slave_type = s.read_u8()?;
                    d.slave_db_id = s.read_bc()?;
                    d.slave_is_destroyed = s.read_u8()?;
                    // C#: `Convert.ToInt32(byte[])` always throws InvalidCastException, so
                    // RepairStartTime ends up DateTime.MinValue and the rest is skipped.
                }
                ItemClass::TreasureMap => {
                    let _ = s.read_i64()?;
                    d.map_position_x = s.read_i64()?;
                    d.map_position_y = s.read_i64()?;
                }
                _ => {}
            }
            Ok(())
        })();
        debug_assert!(r.is_ok());
    }

    /// The subclass `WriteDetails` overrides (`ItemTask` pads these to 128 bytes).
    pub fn write_details_to(&self, s: &mut PacketStream) {
        self.write_details(s);
    }

    /// The subclass `WriteDetails` overrides.
    fn write_details(&self, s: &mut PacketStream) {
        let d = &self.details;
        match self.class {
            c if c.is_equip() => {
                s.write_u32(d.image_item_template_id)
                    .write_u8(d.durability)
                    .write_i16(0)
                    .write_u32(d.rune_id);
                s.write_i64(if self.bind_on_unpack {
                    self.unpack_time
                } else {
                    self.charge_start_time
                });
                s.write_u32(d.dye_item_id);
                for g in d.gem_ids {
                    s.write_u32(g);
                }
                s.write_u16(d.temper_physical).write_u16(d.temper_magical);
            }
            ItemClass::UccItem => {
                s.write_u64(self.ucc_id).write_u8(0);
            }
            ItemClass::BigFish => {
                s.write_f32(d.fish_weight)
                    .write_f32(d.fish_length)
                    .write_i64(self.create_time);
            }
            ItemClass::MusicSheetItem => {
                s.write_u64(u64::from(d.song_id));
            }
            ItemClass::SummonMate => {
                s.write_i32(d.mate_exp)
                    .write_u8(d.mate_dead)
                    .write_u8(d.mate_level);
            }
            ItemClass::SummonSlave => {
                s.write_u8(d.slave_type)
                    .write_bc(d.slave_db_id)
                    .write_u8(d.slave_is_destroyed);
                if d.slave_repair_start_time == 0 {
                    s.write_i32(0); // RepairStartTime == MinValue
                } else {
                    s.write_i64(d.slave_repair_start_time); // Write(DateTime)
                }
                for _ in 0..5 {
                    s.write_i32(0);
                }
            }
            ItemClass::TreasureMap => {
                s.write_i64(0)
                    .write_i64(d.map_position_x)
                    .write_i64(d.map_position_y);
            }
            // Base Item.WriteDetails with DetailType Invalid writes nothing.
            _ => {}
        }
    }
}

/// `ItemFlag.Secure`.
pub const ITEM_FLAG_SECURE: u8 = 0x04;
/// `ItemFlag.Skinized`.
pub const ITEM_FLAG_SKINIZED: u8 = 0x08;
/// `ItemFlag.Unpacked`.
pub const ITEM_FLAG_UNPACKED: u8 = 0x10;
/// `ItemFlag.AuctionWin`.
pub const ITEM_FLAG_AUCTION_WIN: u8 = 0x20;

/// The C# float literal `0.0099999998f`.
#[allow(clippy::excessive_precision)]
const F_001: f32 = 0.009_999_999_8f32;

/// The treasure chest a new `TreasureMap` points at (`GenerateTreasureMapData`'s random pick of
/// `SpawnManager.GetTreasureChestDoodadSpawners()`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TreasureMapTarget {
    /// `chest.Position.X / Y`.
    pub x: f32,
    pub y: f32,
    /// `chestTemplate?.GroupId` (`None` when neither doodad template exists).
    pub chest_group_id: Option<u32>,
}

/// Which `EquipItem` stat (`Str` / `Dex` / `Sta` / `Int` / `Spi`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EquipStat {
    Str,
    Dex,
    Sta,
    Int,
    Spi,
}

impl Item {
    /// `HasFlag(flag)`.
    pub fn has_flag(&self, flag: u8) -> bool {
        self.flags & flag == flag
    }

    /// `SetFlag(flag)` (marks the item dirty).
    pub fn set_flag(&mut self, flag: u8) {
        self.flags |= flag;
        self.dirty = true;
    }

    /// `RemoveFlag(flag)` (marks the item dirty).
    pub fn remove_flag(&mut self, flag: u8) {
        self.flags &= !flag;
        self.dirty = true;
    }

    /// `item is EquipItem`.
    pub fn is_equip(&self) -> bool {
        self.class.is_equip()
    }

    /// `TreasureMap.MapPositionX` (`_mapPositionX / 4096.0`).
    pub fn map_position_x(&self) -> f64 {
        self.details.map_position_x as f64 / 4096.0
    }

    /// `TreasureMap.MapPositionY`.
    pub fn map_position_y(&self) -> f64 {
        self.details.map_position_y as f64 / 4096.0
    }

    /// `TreasureMap.GetMapPosition(z)`: `new Vector3((float)MapPositionX, (float)MapPositionY, z)`.
    pub fn map_position(&self, z: f32) -> (f32, f32, f32) {
        (
            self.map_position_x() as f32,
            self.map_position_y() as f32,
            z,
        )
    }

    /// `TreasureMap.MapPositionX = value`.
    pub fn set_map_position_x(&mut self, v: f64) {
        self.details.map_position_x = (v * 4096.0).round_ties_even() as i64;
    }

    /// `TreasureMap.MapPositionY = value`.
    pub fn set_map_position_y(&mut self, v: f64) {
        self.details.map_position_y = (v * 4096.0).round_ties_even() as i64;
    }

    /// `TreasureMap.GenerateTreasureMapData()`.
    pub fn generate_treasure_map_data(&mut self, target: Option<TreasureMapTarget>) {
        let Some(chest) = target else {
            // No valid location found: mark it Mythic (it will point at 0, 0).
            self.grade = crate::data::items::item_grade::MYTHIC;
            return;
        };
        self.set_map_position_x(f64::from(chest.x));
        self.set_map_position_y(f64::from(chest.y));
        self.grade = match chest.chest_group_id.unwrap_or(0) {
            55 => 0,
            56 => crate::data::items::item_grade::GRAND,
            57 => crate::data::items::item_grade::RARE,
            58 => crate::data::items::item_grade::ARCANE,
            59 => crate::data::items::item_grade::HEROIC,
            g => {
                tracing::error!(
                    "Generated treasure map data for chest did not have a valid category for chests ({g})"
                );
                crate::data::items::item_grade::LEGENDARY
            }
        };
    }

    /// The template if it matches this item's equipment class (the C# casts
    /// `(ArmorTemplate)Template` etc. throw otherwise).
    fn equip_template<'a>(&self, data: &'a ItemData) -> Option<&'a ItemTemplate> {
        let t = data.template(self.template_id)?;
        let ok = matches!(
            (self.class, &t.kind),
            (ItemClass::Armor, TemplateKind::Armor(_))
                | (ItemClass::Weapon, TemplateKind::Weapon(_))
                | (ItemClass::Accessory, TemplateKind::Accessory(_))
        );
        ok.then_some(t)
    }

    /// `EquipItem.MaxDurability` (`Armor` / `Weapon` / `Accessory` formula, 0 for any other
    /// class). `None` where the C# throws (missing grade template, template of another class).
    pub fn try_max_durability(&self, data: &ItemData) -> Option<u8> {
        if !matches!(
            self.class,
            ItemClass::Armor | ItemClass::Weapon | ItemClass::Accessory
        ) {
            return Some(0);
        }
        let t = self.equip_template(data)?;
        data.max_durability(t, i32::from(self.grade))
    }

    /// `EquipItem.MaxDurability` (0 where the C# would throw).
    pub fn max_durability(&self, data: &ItemData) -> u8 {
        self.try_max_durability(data).unwrap_or(0)
    }

    /// `EquipItem.IsNotDestroyed`.
    pub fn is_not_destroyed(&self, data: &ItemData) -> bool {
        self.max_durability(data) == 0 || self.details.durability > 0
    }

    /// `EquipItem.RepairCost`. `None` where the C# throws (not an equipment template, missing
    /// grade).
    pub fn repair_cost(&self, data: &ItemData) -> Option<i32> {
        let template = data.template(self.template_id)?;
        template.equip()?;
        let grade = data.full_grade(i32::from(self.grade))?;
        let max = self.try_max_durability(data)?;
        // (1f - Durability * 1f / MaxDurability): float division (MaxDurability 0 -> inf / NaN)
        let ratio = 1f32 - f32::from(self.details.durability) * 1f32 / f32::from(max);
        let mut cost =
            data.config.durability_repair_cost_factor * F_001 * ratio * template.price as f32;
        cost = cost * grade.refund_multiplier as f32 * F_001;
        cost = f64::from(cost).ceil() as f32;
        if cost.is_nan() {
            // Every comparison with NaN is false in C#, and (int)NaN is int.MinValue (x86).
            return Some(i32::MIN);
        }
        if cost < 0.0 || cost < i32::MIN as f32 || cost > i32::MAX as f32 {
            cost = 0.0;
        }
        Some(cost as i32)
    }

    /// `Armor/Weapon/Accessory.Str` etc. (0 for other classes and for `ModSetId == 0`). `None`
    /// where the C# throws (missing modifiers / grade).
    pub fn stat(&self, data: &ItemData, which: EquipStat) -> Option<i32> {
        if !matches!(
            self.class,
            ItemClass::Armor | ItemClass::Weapon | ItemClass::Accessory
        ) {
            return Some(0);
        }
        let t = self.equip_template(data)?;
        let e = t.equip()?;
        if e.mod_set_id == 0 {
            return Some(0);
        }
        let modifiers = data.attribute_modifiers(e.mod_set_id)?;
        let grade = data.full_grade(i32::from(self.grade))?;
        let c = &data.config;
        let mut m = 1f32;
        if modifiers.count() == 1 {
            m = 3f32;
        }
        if modifiers.count() == 2 {
            m = 1.5f32;
        }
        let exponent = 1f32 / (c.stat_value_const as f32 * F_001);
        m = f64::from(m).powf(f64::from(exponent)) as f32;
        let weight = match which {
            EquipStat::Str => modifiers.str_weight,
            EquipStat::Dex => modifiers.dex_weight,
            EquipStat::Sta => modifiers.sta_weight,
            EquipStat::Int => modifiers.int_weight,
            EquipStat::Spi => modifiers.spi_weight,
        };
        let count = modifiers.count() as f32;
        let all = modifiers.all_weight() as f32;
        match &t.kind {
            TemplateKind::Weapon(_) => {
                let holdable = data.holdable_of(t)?;
                let temp = c.item_stat_const as f32
                    * F_001
                    * t.level as f32
                    * m
                    * c.holdable_stat_const as f32
                    * F_001;
                let temp2 = count * temp * weight as f32 * 1f32 / all
                    * grade.stat_multiplier as f32
                    * F_001
                    + 0.5f32;
                let res =
                    ((temp2 as i32).wrapping_mul(holdable.stat_multiplier)) as f32 * F_001 + 0.5f32;
                Some(res as i32)
            }
            _ => {
                let temp = c.item_stat_const as f32
                    * F_001
                    * t.level as f32
                    * m
                    * c.wearable_stat_const as f32
                    * F_001
                    * e.coverage as f32
                    * 0.01f32;
                let res = count * temp * weight as f32 * 1f32 / all
                    * grade.stat_multiplier as f32
                    * F_001
                    + 0.5f32;
                Some(res as i32)
            }
        }
    }

    /// `Armor.BaseArmor` / `Accessory.BaseArmor` (armor and accessory items only).
    pub fn base_armor(&self, data: &ItemData, formulas: &FormulaManager) -> Option<i32> {
        self.armor_value(data, formulas, true)
    }

    /// `Armor.BaseMagicResistance` / `Accessory.BaseMagicResistance`.
    pub fn base_magic_resistance(&self, data: &ItemData, formulas: &FormulaManager) -> Option<i32> {
        self.armor_value(data, formulas, false)
    }

    fn armor_value(&self, data: &ItemData, formulas: &FormulaManager, armor: bool) -> Option<i32> {
        if !matches!(self.class, ItemClass::Armor | ItemClass::Accessory) {
            return None;
        }
        // The accessory getters ignore the tempers.
        let tempered = self.class == ItemClass::Armor;
        let t = self.equip_template(data)?;
        let e = t.equip()?;
        let grade = data.full_grade(i32::from(self.grade))?;
        let kind = data.wearable_kind_of(t)?;
        let (formula_type, grade_value, ratio, temper) = if armor {
            (
                WearableFormulaType::MaxBaseArmor,
                grade.wearable_armor,
                kind.armor_ratio,
                self.details.temper_physical,
            )
        } else {
            (
                WearableFormulaType::MaxBaseMagicResistance,
                grade.wearable_magic_resistance,
                kind.magic_resistance_ratio,
                self.details.temper_magical,
            )
        };
        let formula = formulas.get_wearable_formula(formula_type)?;
        let params = HashMap::from([
            ("item_level", f64::from(t.level)),
            ("item_grade", f64::from(grade_value)),
        ]);
        let mut res = formula.evaluate(&params);
        res = res * f64::from(ratio) * f64::from(F_001);
        if tempered && temper > 100 {
            res *= f64::from(f32::from(temper) / 100.0f32);
        }
        Some((res * f64::from(e.coverage) * f64::from(F_001)) as i32)
    }

    fn weapon_formula(
        &self,
        data: &ItemData,
        pick: impl Fn(&Holdable) -> &Formula,
        grade_value: impl Fn(&FullGradeTemplate) -> f32,
        temper: u16,
    ) -> Option<f64> {
        if self.class != ItemClass::Weapon {
            return None;
        }
        let t = self.equip_template(data)?;
        let grade = data.full_grade(i32::from(self.grade))?;
        let holdable = data.holdable_of(t)?;
        let params = HashMap::from([
            ("item_level", f64::from(t.level)),
            ("item_grade", f64::from(grade_value(grade))),
        ]);
        let mut res = pick(holdable).evaluate(&params);
        if temper > 100 {
            res *= f64::from(f32::from(temper) / 100.0f32);
        }
        Some(res)
    }

    /// `Weapon.Dps`.
    pub fn dps(&self, data: &ItemData) -> Option<f32> {
        self.weapon_formula(
            data,
            |h| &h.formula_dps,
            |g| g.holdable_dps,
            self.details.temper_physical,
        )
        .map(|v| v as f32)
    }

    /// `Weapon.MDps`.
    pub fn mdps(&self, data: &ItemData) -> Option<f64> {
        self.weapon_formula(
            data,
            |h| &h.formula_mdps,
            |g| g.holdable_magic_dps,
            self.details.temper_magical,
        )
    }

    /// `Weapon.HDps` (uses `HoldableMagicDps` for the grade, as the C#).
    pub fn hdps(&self, data: &ItemData) -> Option<f64> {
        self.weapon_formula(
            data,
            |h| &h.formula_hdps,
            |g| g.holdable_magic_dps,
            self.details.temper_magical,
        )
    }

    /// `Weapon.Armor`.
    pub fn weapon_armor(&self, data: &ItemData) -> Option<i32> {
        self.weapon_formula(
            data,
            |h| &h.formula_armor,
            |g| g.holdable_armor,
            self.details.temper_physical,
        )
        .map(|v| v as i32)
    }

    /// `Item.CompareTo` (by slot).
    pub fn cmp_slot(&self, other: &Item) -> std::cmp::Ordering {
        self.slot.cmp(&other.slot)
    }
}

/// `ItemProc` (runtime proc of an equipped item; the skill cast itself is the skills
/// subsystem's `new Skill(Template.SkillTemplate).Use(owner, unitCaster, doodadTarget)`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ItemProc {
    pub template_id: u32,
    /// `LastProc` (unix milliseconds, 0 = `DateTime.MinValue`; the C# never updates it).
    pub last_proc_ms: i64,
}

impl ItemProc {
    pub fn new(template_id: u32) -> Self {
        Self {
            template_id,
            last_proc_ms: 0,
        }
    }

    /// The checks of `ItemProc.Apply(owner, ignoreRoll)`: `Some(skill_id)` when the proc skill
    /// must be used. `roll` = `Random.Shared.Next(0, 100)` (only drawn when `ignore_roll` is
    /// false). C# quirks mirrored: `ignoreRoll == true` never procs, and `LastProc` is never set.
    /// `None` too where the C# throws (unknown proc template).
    pub fn apply(
        &self,
        data: &ItemData,
        now_ms: i64,
        ignore_roll: bool,
        roll: impl FnOnce() -> i32,
    ) -> Option<u32> {
        let t = data.item_proc(self.template_id)?;
        // DateTime.MinValue.AddSeconds(cooldown) is always in the past.
        if self.last_proc_ms > 0 && now_ms < self.last_proc_ms + i64::from(t.cooldown_sec) * 1000 {
            return None;
        }
        if ignore_roll || i64::from(roll()) > i64::from(t.chance_rate) {
            return None;
        }
        Some(t.skill_id)
    }
}

impl Marshal for Item {
    /// `Item.Write` (and the `BodyPart.Write` override, which only writes the template id).
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.template_id);
        if self.class == ItemClass::BodyPart || self.template_id == 0 {
            return;
        }
        s.write_u64(self.id)
            .write_u8(self.grade)
            .write_u8(self.flags)
            .write_i32(self.count)
            .write_u8(self.class.detail_type());
        self.write_details(s);
        s.write_i64(self.create_time)
            .write_i32(self.lifespan_mins)
            .write_u32(self.made_unit_id)
            .write_u8(self.world_id)
            .write_i64(self.unsecure_time)
            .write_i64(self.unpack_time);
    }
}

#[cfg(test)]
#[path = "item_tests.rs"]
mod item_tests;

#[cfg(test)]
mod tests {
    use super::*;

    fn equip_details_blob() -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u32(777)
            .write_u8(90)
            .write_i16(0)
            .write_u32(5)
            .write_i64(123)
            .write_u32(42);
        for i in 0..7u32 {
            s.write_u32(i);
        }
        s.write_u16(3).write_u16(4);
        assert_eq!(s.len(), 55);
        s.into_bytes()
    }

    #[test]
    fn armor_write_is_byte_exact() {
        let row = ItemRow {
            type_name: "AAEmu.Game.Models.Game.Items.Armor".into(),
            id: 0x0102030405060708,
            template_id: 0x1000,
            count: 1,
            flags: 0x01,
            details: equip_details_blob(),
            grade: 2,
            charge_time: 500,
            unpack_time: 600,
            created_at: 700,
            ..ItemRow::default()
        };
        let t = ItemTemplate {
            bind_type: 1,
            fixed_grade: -1,
            gradable: true,
            ..ItemTemplate::plain(0x1000, 1)
        };
        let item = Item::from_row(row, Some(&t), 1);
        let mut s = PacketStream::new();
        item.write(&mut s);
        let mut want = PacketStream::new();
        want.write_u32(0x1000)
            .write_u64(0x0102030405060708)
            .write_u8(2)
            .write_u8(1)
            .write_i32(1)
            .write_u8(1);
        want.write_u32(777)
            .write_u8(90)
            .write_i16(0)
            .write_u32(5)
            .write_i64(500)
            .write_u32(42);
        for i in 0..7u32 {
            want.write_u32(i);
        }
        want.write_u16(3).write_u16(4);
        want.write_i64(700)
            .write_i32(0)
            .write_u32(0)
            .write_u8(1)
            .write_i64(0)
            .write_i64(600);
        assert_eq!(s.as_bytes(), want.as_bytes());
    }

    #[test]
    fn body_part_writes_template_only_and_fixed_grade_wins() {
        let row = ItemRow {
            type_name: "AAEmu.Game.Models.Game.Items.BodyPart".into(),
            template_id: 99,
            grade: 4,
            ..ItemRow::default()
        };
        let item = Item::from_row(
            row,
            Some(&ItemTemplate {
                bind_type: 1,
                fixed_grade: 1,
                gradable: true,
                ..ItemTemplate::plain(99, 1)
            }),
            1,
        );
        assert_eq!(item.grade, 1);
        let mut s = PacketStream::new();
        item.write(&mut s);
        assert_eq!(s.as_bytes(), &99u32.to_le_bytes());
    }

    #[test]
    fn ucc_flag_follows_ucc_column() {
        let row = ItemRow {
            type_name: "AAEmu.Game.Models.Game.Items.Item".into(),
            template_id: 5,
            flags: ITEM_FLAG_HAS_UCC,
            ucc: 0,
            ..ItemRow::default()
        };
        let item = Item::from_row(row, None, 1);
        assert_eq!(item.flags & ITEM_FLAG_HAS_UCC, 0);
    }

    #[test]
    fn summon_mate_details_keep_the_dead_byte() {
        let blob = {
            let mut s = PacketStream::new();
            s.write_i32(600).write_u8(1).write_u8(7);
            s.into_bytes()
        };
        let row = ItemRow {
            type_name: "AAEmu.Game.Models.Game.Items.SummonMate".into(),
            template_id: 5,
            details: blob.clone(),
            ..ItemRow::default()
        };
        let item = Item::from_row(row, None, 1);
        assert_eq!(
            (
                item.details.mate_exp,
                item.details.mate_dead,
                item.details.mate_level
            ),
            (600, 1, 7)
        );
        assert_eq!(item.details_blob(), blob);
        let mut want = vec![detail_type::MATE];
        want.extend_from_slice(&blob);
        assert_eq!(item.detail_type_and_details(), want);
    }
}
