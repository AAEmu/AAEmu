//! The subset of `AAEmu.Game.Models.Game.Char.Character` needed for the character list:
//! the fields loaded by `Character.Load(connection, characterId, accountId)` that
//! `Character.Write` serialises.

use aaemu_commons::{Marshal, PacketStream};

use super::item::{Item, UnixTime};
use super::unit_model::UnitCustomModelParams;

/// `EquipmentContainer` default size: `(int)Enum.GetValues<EquipmentItemSlot>().Max() + 1`
/// (`Cosplay = 27`).
pub const EQUIPMENT_CONTAINER_SIZE: usize = 28;

#[derive(Debug, Clone, Default, PartialEq)]
pub struct Character {
    pub id: u32,
    pub account_id: u32,
    pub name: String,
    pub race: u8,
    pub gender: u8,
    pub level: u8,
    pub hp: i32,
    pub mp: i32,
    pub zone_id: u32,
    /// `Faction.Id`. C# resolves `faction_id` through `FactionManager` (game data) and would throw
    /// on an unknown faction; the id written is the same value (later phase: faction templates).
    pub faction_id: u32,
    pub faction_name: String,
    /// `Expedition?.Id ?? 0`: `expedition_id` if that guild exists, else 0.
    pub expedition_id: u32,
    pub family: u32,
    /// `Inventory.Equipment.GetSlottedItemsList()`: one entry per container slot.
    pub equipment: Vec<Option<Item>>,
    pub ability1: u8,
    pub ability2: u8,
    pub ability3: u8,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub model_params: UnitCustomModelParams,
    /// `InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated)`.
    pub labor_power: i16,
    pub labor_power_modified: UnixTime,
    pub dead_count: i16,
    pub dead_time: UnixTime,
    pub rez_wait_duration: i32,
    pub rez_time: UnixTime,
    pub rez_penalty_duration: i32,
    pub leave_time: UnixTime,
    pub money: i64,
    /// Current crime points (clamped to >= 0 by the C# setter).
    pub crime_point: i16,
    /// `InfamyPoint` (column `crime_record`).
    pub infamy_point: i32,
    pub delete_request_time: UnixTime,
    pub transfer_request_time: UnixTime,
    pub delete_time: UnixTime,
    pub consumed_labor_power: i32,
    /// `AccountDetails.Loyalty` (written as `long`).
    pub bm_point: i64,
    pub money2: i64,
    pub auto_use_aa_point: bool,
    pub prev_point: i32,
    pub point: i32,
    pub gift: i32,
    pub updated: UnixTime,
}

/// `Helpers.ConvertLongX` / `ConvertLongY` (float -> fixed-point long).
pub fn convert_long(v: f32) -> i64 {
    ((v * 4096.0) as i64) << 32
}

impl Marshal for Character {
    /// `Character.Write` (used by `SCCharacterListPacket`).
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.gender)
            .write_u8(self.level)
            .write_i32(self.hp)
            .write_i32(self.mp)
            .write_u32(self.zone_id)
            .write_u32(self.faction_id)
            .write_str(&self.faction_name)
            .write_u32(self.expedition_id)
            .write_u32(self.family);

        for item in &self.equipment {
            match item {
                None => {
                    s.write_i32(0);
                }
                Some(item) => item.write(s),
            }
        }

        s.write_u8(self.ability1)
            .write_u8(self.ability2)
            .write_u8(self.ability3);

        s.write_i64(convert_long(self.x))
            .write_i64(convert_long(self.y))
            .write_f32(self.z);

        self.model_params.write(s);
        s.write_i16(self.labor_power)
            .write_i64(self.labor_power_modified)
            .write_i16(self.dead_count)
            .write_i64(self.dead_time)
            .write_i32(self.rez_wait_duration)
            .write_i64(self.rez_time)
            .write_i32(self.rez_penalty_duration)
            .write_i64(self.leave_time) // lastWorldLeaveTime
            .write_i64(self.money)
            .write_i64(0) // moneyAmount ?
            .write_i16(self.crime_point) // current crime points (/50)
            .write_i32(self.infamy_point) // total infamy
            .write_i16(0) // crimeScore? trials served?
            .write_i64(self.delete_request_time)
            .write_i64(self.transfer_request_time)
            .write_i64(self.delete_time) // deleteDelay
            .write_i32(self.consumed_labor_power)
            .write_i64(self.bm_point) // loyalty tokens
            .write_i64(self.money2)
            .write_i64(0) // moneyAmount ?
            .write_bool(self.auto_use_aa_point)
            .write_i32(self.prev_point)
            .write_i32(self.point)
            .write_i32(self.gift)
            .write_i64(self.updated)
            .write_u8(0); // forceNameChange ?
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn convert_long_matches_csharp() {
        assert_eq!(convert_long(1.0), 4096i64 << 32);
        assert_eq!(convert_long(-0.5), -2048i64 << 32);
        assert_eq!(
            convert_long(12345.678),
            ((12345.678f32 * 4096.0) as i64) << 32
        );
    }

    #[test]
    fn empty_equipment_and_fixed_tail_size() {
        let c = Character {
            name: "Ab".into(),
            faction_name: "".into(),
            equipment: vec![None; EQUIPMENT_CONTAINER_SIZE],
            ..Character::default()
        };
        let mut s = PacketStream::new();
        c.write(&mut s);
        let head = 4 + 2 + 2 + 3 + 8 + 4 + 4 + 2 + 4 + 4;
        let equip = 4 * EQUIPMENT_CONTAINER_SIZE;
        let tail = 3
            + 8
            + 8
            + 4
            + 1
            + 2
            + 8
            + 2
            + 8
            + 4
            + 8
            + 4
            + 8
            + 8
            + 8
            + 2
            + 4
            + 2
            + 8
            + 8
            + 8
            + 4
            + 8
            + 8
            + 8
            + 1
            + 4
            + 4
            + 4
            + 8
            + 1;
        assert_eq!(s.len(), head + equip + tail);
    }
}
