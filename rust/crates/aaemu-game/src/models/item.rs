//! Port of the persistence + wire parts of `AAEmu.Game.Models.Game.Items.Item` and its
//! subclasses (`EquipItem`/`Armor`/`Weapon`/`Accessory`, `BodyPart`, `UccItem`, `BigFish`,
//! `MusicSheetItem`, `SummonMate`, `SummonSlave`, `TreasureMap`, `Backpack`, `Rune`).
//!
//! Only what `Character.Write` (character list) needs: loading a row of the `items` table
//! (`ItemManager.LoadUserItems`) and `Item.Write`. Gameplay behaviour (stats, durability,
//! repair cost, expiration, ...) is a later phase.

use aaemu_commons::{Marshal, PacketStream};

use crate::data::ItemTemplateInfo;

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
    pub const MUSIC_SHEET: u8 = 8;
}

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

    fn is_equip(self) -> bool {
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
    pub mate_level: u8,
    // SummonSlave
    pub slave_type: u8,
    pub slave_db_id: u32,
    pub slave_is_destroyed: u8,
    // TreasureMap
    pub map_position_x: i64,
    pub map_position_y: i64,
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
}

impl Item {
    /// Mirrors the per-row body of `ItemManager.LoadUserItems`.
    ///
    /// `template` is `None` when the client game data is unavailable; C# would throw on
    /// `item.Template.FixedGrade`, here the DB grade is kept and `BindType` is treated as not
    /// `BindOnUnpack` (documented later-phase gap).
    pub fn from_row(row: ItemRow, template: Option<&ItemTemplateInfo>, world_id: u8) -> Self {
        let class = ItemClass::from_type_name(&row.type_name).unwrap_or_else(|| {
            tracing::warn!(
                "Item type {} not found for id {}! (template ClassType fallback is a later phase)",
                row.type_name,
                row.id
            );
            ItemClass::Item
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
                    let _ = s.read_u8()?;
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
                s.write_f32(0.0).write_f32(0.0).write_i64(self.create_time);
            }
            ItemClass::MusicSheetItem => {
                s.write_u64(u64::from(d.song_id));
            }
            ItemClass::SummonMate => {
                s.write_i32(d.mate_exp).write_u8(0).write_u8(d.mate_level);
            }
            ItemClass::SummonSlave => {
                s.write_u8(d.slave_type)
                    .write_bc(d.slave_db_id)
                    .write_u8(d.slave_is_destroyed);
                s.write_i32(0); // RepairStartTime == MinValue
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
        let t = ItemTemplateInfo {
            bind_type: 1,
            fixed_grade: -1,
            gradable: true,
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
            Some(&ItemTemplateInfo {
                bind_type: 1,
                fixed_grade: 1,
                gradable: true,
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
}
