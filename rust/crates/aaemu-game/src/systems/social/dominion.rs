//! `Models/Game/DominionData` (`DominionData`, `DominionTerritoryData`, `DominionSiegeTimers`,
//! `DominionUnkData`) with their `Write`. The 1.2 C# has no dominion manager; the data is only
//! built by the `DeclareDominion` special effect (`SCDominionDataPacket`). Null sub-objects (an
//! NRE in C#) are default values here.

use aaemu_commons::helpers::convert_coord_to_long;
use aaemu_commons::{Marshal, PacketStream};
use chrono::{DateTime, Utc};

/// `DominionData`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionData {
    pub zone_id: u16,
    pub expedition_id: u32,
    /// `House` (TODO in C#: an id?).
    pub house: u32,
    pub tax_rate: i32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub cur_house_tax_money: i32,
    pub cur_hunt_tax_money: i32,
    pub peace_tax_money: i32,
    pub cur_house_tax_aa_point: i32,
    pub peace_tax_aa_point: i32,
    pub last_paid_time: DateTime<Utc>,
    pub last_siege_end_time: DateTime<Utc>,
    pub reign_start_time: DateTime<Utc>,
    pub last_tax_rate_changed_time: DateTime<Utc>,
    /// `LastNationalTaxRateChagedTime` (sic).
    pub last_national_tax_rate_changed_time: DateTime<Utc>,
    pub national_tax_rate: u16,
    pub national_monument_db_id: i64,
    pub national_monument_x: f32,
    pub national_monument_y: f32,
    pub national_monument_z: f32,
    pub obj_id: u32,
    pub territory_data: DominionTerritoryData,
    pub siege_timers: DominionSiegeTimers,
    pub non_pvp_start: DateTime<Utc>,
    pub non_pvp_duration: u16,
}

impl Marshal for DominionData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.zone_id)
            .write_u32(self.expedition_id)
            .write_u32(self.house)
            .write_i32(self.tax_rate)
            .write_i64(convert_coord_to_long(self.x))
            .write_i64(convert_coord_to_long(self.y))
            .write_f32(self.z)
            .write_i32(self.cur_house_tax_money)
            .write_i32(self.cur_hunt_tax_money)
            .write_i32(self.peace_tax_money)
            .write_i32(self.cur_house_tax_aa_point)
            .write_i32(self.peace_tax_aa_point)
            .write_date_time(self.last_paid_time)
            .write_date_time(self.last_siege_end_time)
            .write_date_time(self.reign_start_time)
            .write_date_time(self.last_tax_rate_changed_time)
            .write_date_time(self.last_national_tax_rate_changed_time)
            .write_u16(self.national_tax_rate)
            .write_i64(self.national_monument_db_id)
            .write_i64(convert_coord_to_long(self.national_monument_x))
            .write_i64(convert_coord_to_long(self.national_monument_y))
            .write_f32(self.national_monument_z)
            .write_bc(self.obj_id)
            .write(&self.territory_data)
            .write(&self.siege_timers)
            .write_date_time(self.non_pvp_start)
            .write_u16(self.non_pvp_duration);
    }
}

/// `DominionTerritoryData`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DominionTerritoryData {
    pub id: u32,
    pub id2: u32,
    pub max_gates: u8,
    pub max_walls: u8,
    pub radius_declare: i16,
    pub radius_dominion: u16,
    pub radius_offense_hq: i16,
    pub radius_siege: i16,
}

impl Marshal for DominionTerritoryData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.id2)
            .write_u8(self.max_gates)
            .write_u8(self.max_walls)
            .write_i16(self.radius_declare)
            .write_u16(self.radius_dominion)
            .write_i16(self.radius_offense_hq)
            .write_i16(self.radius_siege);
    }
}

/// `DominionSiegeTimers` (TODO in C#: the names may be wrong).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionSiegeTimers {
    pub durations: [i32; 5],
    pub started: DateTime<Utc>,
    pub fixed: DateTime<Utc>,
    pub bdm: i32,
    pub siege_period: u8,
    pub unk_data: DominionUnkData,
    pub unk2_data: DominionUnkData,
}

impl Marshal for DominionSiegeTimers {
    fn write(&self, s: &mut PacketStream) {
        for d in self.durations {
            s.write_i32(d);
        }
        s.write_date_time(self.started)
            .write_date_time(self.fixed)
            .write_i32(self.bdm)
            .write_u8(self.siege_period)
            .write(&self.unk_data)
            .write(&self.unk2_data);
    }
}

/// `DominionUnkData`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionUnkData {
    /// `Id` (TODO in C#: the expedition id).
    pub id: u32,
    pub obj_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub ni: u8,
    pub nr: u8,
    pub limit: u8,
    /// `UnkIds` (the count is written as a byte).
    pub unk_ids: Vec<u32>,
}

impl Marshal for DominionUnkData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_bc(self.obj_id)
            .write_i64(convert_coord_to_long(self.x))
            .write_i64(convert_coord_to_long(self.y))
            .write_f32(self.z)
            .write_u8(self.ni)
            .write_u8(self.nr)
            .write_u8(self.limit)
            .write_u8(self.unk_ids.len() as u8);
        for id in &self.unk_ids {
            s.write_u32(*id);
        }
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use chrono::TimeZone;

    fn t(secs: i64) -> DateTime<Utc> {
        Utc.timestamp_opt(secs, 0).unwrap()
    }

    fn long(v: f32) -> i64 {
        ((v * 4096.0) as i64) << 32
    }

    pub(crate) fn sample() -> DominionData {
        DominionData {
            zone_id: 0x0102,
            expedition_id: 3,
            house: 4,
            tax_rate: 5,
            x: 100.5,
            y: 200.25,
            z: 30.0,
            cur_house_tax_money: 6,
            cur_hunt_tax_money: 7,
            peace_tax_money: 8,
            cur_house_tax_aa_point: 9,
            peace_tax_aa_point: 10,
            last_paid_time: t(11),
            last_siege_end_time: t(12),
            reign_start_time: t(13),
            last_tax_rate_changed_time: t(14),
            last_national_tax_rate_changed_time: t(15),
            national_tax_rate: 16,
            national_monument_db_id: 17,
            national_monument_x: 18.0,
            national_monument_y: 19.0,
            national_monument_z: 20.0,
            obj_id: 0x0A0B0C,
            territory_data: DominionTerritoryData {
                id: 21,
                id2: 22,
                max_gates: 23,
                max_walls: 24,
                radius_declare: -25,
                radius_dominion: 26,
                radius_offense_hq: 27,
                radius_siege: 28,
            },
            siege_timers: DominionSiegeTimers {
                durations: [1, 2, 3, 4, 5],
                started: t(29),
                fixed: t(30),
                bdm: 31,
                siege_period: 32,
                unk_data: DominionUnkData {
                    id: 33,
                    obj_id: 34,
                    x: 1.0,
                    y: 2.0,
                    z: 3.0,
                    ni: 4,
                    nr: 5,
                    limit: 6,
                    unk_ids: vec![7, 8],
                },
                unk2_data: DominionUnkData::default(),
            },
            non_pvp_start: t(35),
            non_pvp_duration: 36,
        }
    }

    /// The expected `Write` of [`sample`], spelled out with the C# primitives.
    pub(crate) fn sample_bytes() -> Vec<u8> {
        let mut e = PacketStream::new();
        e.write_u16(0x0102)
            .write_u32(3)
            .write_u32(4)
            .write_i32(5)
            .write_i64(long(100.5))
            .write_i64(long(200.25))
            .write_f32(30.0);
        for v in 6..=10 {
            e.write_i32(v);
        }
        for v in 11..=15 {
            e.write_i64(v);
        }
        e.write_u16(16)
            .write_i64(17)
            .write_i64(long(18.0))
            .write_i64(long(19.0))
            .write_f32(20.0)
            .write_bc(0x0A0B0C)
            // territory
            .write_u32(21)
            .write_u32(22)
            .write_u8(23)
            .write_u8(24)
            .write_i16(-25)
            .write_u16(26)
            .write_i16(27)
            .write_i16(28);
        // siege timers
        for v in 1..=5 {
            e.write_i32(v);
        }
        e.write_i64(29).write_i64(30).write_i32(31).write_u8(32);
        e.write_u32(33)
            .write_bc(34)
            .write_i64(long(1.0))
            .write_i64(long(2.0))
            .write_f32(3.0)
            .write_u8(4)
            .write_u8(5)
            .write_u8(6)
            .write_u8(2)
            .write_u32(7)
            .write_u32(8);
        e.write_u32(0)
            .write_bc(0)
            .write_i64(0)
            .write_i64(0)
            .write_f32(0.0)
            .write_raw(&[0, 0, 0, 0]);
        e.write_i64(35).write_u16(36);
        e.into_bytes()
    }

    #[test]
    fn dominion_data_write_is_byte_exact() {
        let mut s = PacketStream::new();
        sample().write(&mut s);
        assert_eq!(s.into_bytes(), sample_bytes());
    }
}
