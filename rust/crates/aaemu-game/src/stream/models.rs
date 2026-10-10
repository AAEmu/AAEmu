//! Port of `Models/Stream` (`Ucc`, `DefaultUcc`, `CustomUcc`, `UccPart`, `UccUploadHandle`) and
//! the .NET `DateTime` binary format the UCC packets carry.

use aaemu_commons::{MarshalError, PacketStream};
use chrono::{Local, NaiveDate, NaiveDateTime, Offset, TimeZone};

/// `UccType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum UccType {
    /// `DefaultUcc`: pattern + colours only.
    Simple = 1,
    /// `CustomUcc`: `DefaultUcc` + an uploaded image (`Data`).
    Complex = 2,
}

impl UccType {
    pub fn from_u8(v: u8) -> Option<Self> {
        match v {
            1 => Some(Self::Simple),
            2 => Some(Self::Complex),
            _ => None,
        }
    }
}

const TICKS_PER_SECOND: i64 = 10_000_000;
const TICKS_PER_DAY: i64 = 864_000_000_000;
/// `DateTime.MaxValue.Ticks`.
pub const MAX_TICKS: i64 = 3_155_378_975_999_999_999;
/// `DateTime.UnixEpoch.Ticks`.
pub const UNIX_EPOCH_TICKS: i64 = 621_355_968_000_000_000;
const TICKS_MASK: i64 = 0x3FFF_FFFF_FFFF_FFFF;
const KIND_UTC: i64 = 0x4000_0000_0000_0000;
/// Top bit (`0x8000_0000_0000_0000`): Local kind.
const LOCAL_MASK: i64 = i64::MIN;
const TICKS_CEILING: i64 = 0x4000_0000_0000_0000;

/// A .NET `DateTime`, stored in its `ToBinary()` form so `DateTime.FromBinary(x).ToBinary()`
/// round-trips exactly (the UCC packets echo the client's value back).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DotNetDateTime {
    binary: i64,
}

impl DotNetDateTime {
    /// `DateTime.MinValue` (kind Unspecified).
    pub const MIN: Self = Self { binary: 0 };

    /// `DateTime.FromBinary`. Values whose tick count is outside `[MinValue, MaxValue]` throw
    /// `ArgumentException` in C#; that is an error here.
    pub fn from_binary(binary: i64) -> Result<Self, MarshalError> {
        let ok = if binary & LOCAL_MASK != 0 {
            (0..=MAX_TICKS).contains(&local_ticks(binary))
        } else {
            binary & TICKS_MASK <= MAX_TICKS
        };
        if ok {
            Ok(Self { binary })
        } else {
            Err(MarshalError::Invalid(format!(
                "bad DateTime binary data {binary:#x}"
            )))
        }
    }

    /// `DateTime.ToBinary()`.
    pub fn to_binary(self) -> i64 {
        self.binary
    }

    /// A `DateTimeKind.Unspecified` value (what MySql.Data returns for a DATETIME column).
    pub fn from_naive(dt: NaiveDateTime) -> Self {
        let base = NaiveDate::from_ymd_opt(1, 1, 1)
            .expect("valid date")
            .and_hms_opt(0, 0, 0)
            .expect("valid time");
        let d = dt - base;
        let ticks = d.num_seconds() * TICKS_PER_SECOND + i64::from(d.subsec_nanos()) / 100;
        Self {
            binary: ticks.clamp(0, MAX_TICKS),
        }
    }

    /// `DateTime.Ticks` (for a Local value: the local wall-clock ticks).
    pub fn ticks(self) -> i64 {
        if self.binary & LOCAL_MASK != 0 {
            local_ticks(self.binary)
        } else {
            self.binary & TICKS_MASK
        }
    }

    pub fn is_utc(self) -> bool {
        self.binary & (KIND_UTC | LOCAL_MASK) == KIND_UTC
    }

    /// `Helpers.UnixTime(DateTime)` (what `PacketStream.Write(DateTime)` sends): values at or
    /// before `MinValue` or before the epoch are `0`; otherwise `(long)TimeSpan.TotalSeconds`.
    pub fn unix_time(self) -> i64 {
        let ticks = self.ticks();
        if ticks <= 0 || ticks < UNIX_EPOCH_TICKS {
            return 0;
        }
        ((ticks - UNIX_EPOCH_TICKS) as f64 / TICKS_PER_SECOND as f64) as i64
    }
}

/// `FromBinary` for a Local value: stored UTC ticks + the local offset at that instant.
fn local_ticks(binary: i64) -> i64 {
    let mut ticks = binary & TICKS_MASK;
    if ticks > TICKS_CEILING - TICKS_PER_DAY {
        ticks -= TICKS_CEILING;
    }
    let local = ticks + local_offset_ticks(ticks.clamp(0, MAX_TICKS));
    if local < 0 {
        local + TICKS_PER_DAY
    } else {
        local
    }
}

/// UTC offset of the local time zone at the given UTC tick count.
fn local_offset_ticks(utc_ticks: i64) -> i64 {
    let secs = (utc_ticks - UNIX_EPOCH_TICKS).div_euclid(TICKS_PER_SECOND);
    match Local.timestamp_opt(secs, 0) {
        chrono::LocalResult::Single(t) | chrono::LocalResult::Ambiguous(t, _) => {
            i64::from(t.offset().fix().local_minus_utc()) * TICKS_PER_SECOND
        }
        chrono::LocalResult::None => 0,
    }
}

/// `Ucc` / `DefaultUcc` / `CustomUcc` in one struct (`ty` tells which; `data` is only used by
/// `Complex`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Ucc {
    pub id: u32,
    pub uploader_id: u32,
    pub ty: UccType,
    /// Background pattern.
    pub pattern1: u32,
    /// Crest.
    pub pattern2: u32,
    /// `Color1R/G/B`.
    pub color1: [u32; 3],
    pub color2: [u32; 3],
    pub color3: [u32; 3],
    pub modified: DotNetDateTime,
    /// `CustomUcc.Data`.
    pub data: Vec<u8>,
    /// `CustomUcc.SaveDataInDB` (always true: `STORE_UCC_AS_FILE` is off in C#).
    pub save_data_in_db: bool,
}

impl Ucc {
    pub fn new(ty: UccType, uploader_id: u32) -> Self {
        Self {
            id: 0,
            uploader_id,
            ty,
            pattern1: 0,
            pattern2: 0,
            color1: [0; 3],
            color2: [0; 3],
            color3: [0; 3],
            modified: DotNetDateTime::MIN,
            data: Vec::new(),
            save_data_in_db: true,
        }
    }

    pub fn is_custom(&self) -> bool {
        self.ty == UccType::Complex
    }

    /// `CustomUcc.Data.Count` for a custom UCC, 0 otherwise.
    pub fn data_len(&self) -> usize {
        if self.is_custom() {
            self.data.len()
        } else {
            0
        }
    }

    /// `DefaultUcc.Write` (also used by `CustomUcc`).
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.pattern1).write_u32(self.pattern2);
        for c in [self.color1, self.color2, self.color3] {
            s.write_u32(c[0]).write_u32(c[1]).write_u32(c[2]);
        }
        s.write_i64(self.modified.to_binary());
    }

    /// `DefaultUcc.Read` (also used by `CustomUcc`).
    pub fn read(&mut self, s: &mut PacketStream) -> Result<(), MarshalError> {
        self.pattern1 = s.read_u32()?;
        self.pattern2 = s.read_u32()?;
        for c in [&mut self.color1, &mut self.color2, &mut self.color3] {
            c[0] = s.read_u32()?;
            c[1] = s.read_u32()?;
            c[2] = s.read_u32()?;
        }
        self.modified = DotNetDateTime::from_binary(s.read_i64()?)?;
        Ok(())
    }
}

/// `UccPart`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UccPart {
    pub total: i32,
    pub index: u32,
    pub size: i32,
    pub data: Vec<u8>,
}

/// `UccUploadHandle`. `UploadingUcc` is the `_uploadQueue` entry of the same connection, so
/// [`UccUploadHandle::finalize`] returns the assembled data for the caller to store there.
#[derive(Debug, Clone, Default)]
pub struct UccUploadHandle {
    pub expected_size: i32,
    pub uploaded_size: i32,
    pub parts: Vec<UccPart>,
}

impl UccUploadHandle {
    pub fn new(expected_size: i32) -> Self {
        Self {
            expected_size,
            ..Self::default()
        }
    }

    pub fn add_part(&mut self, part: UccPart) {
        self.uploaded_size = self.uploaded_size.wrapping_add(part.data.len() as i32);
        self.parts.push(part);
    }

    pub fn upload_complete(&self) -> bool {
        self.uploaded_size >= self.expected_size
    }

    /// `FinalizeUpload`: every part's data, in arrival order.
    pub fn finalize(&self) -> Vec<u8> {
        self.parts
            .iter()
            .flat_map(|p| p.data.iter().copied())
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn datetime_binary() {
        // Unspecified 2024-01-02 03:04:05
        let dt = NaiveDate::from_ymd_opt(2024, 1, 2)
            .unwrap()
            .and_hms_opt(3, 4, 5)
            .unwrap();
        let d = DotNetDateTime::from_naive(dt);
        assert_eq!(d.to_binary(), 638_397_614_450_000_000);
        assert_eq!(d.unix_time(), dt.and_utc().timestamp());
        // Utc kind round-trips and keeps its tick count.
        let utc = DotNetDateTime::from_binary(638_397_614_450_000_000 | KIND_UTC).unwrap();
        assert!(utc.is_utc());
        assert_eq!(utc.to_binary(), 638_397_614_450_000_000 | KIND_UTC);
        assert_eq!(utc.unix_time(), 1_704_164_645);
        assert_eq!(DotNetDateTime::MIN.unix_time(), 0);
        assert_eq!(DotNetDateTime::from_binary(1).unwrap().unix_time(), 0);
        // Ticks beyond MaxValue: ArgumentException in C#.
        assert!(DotNetDateTime::from_binary(MAX_TICKS + 1).is_err());
        assert!(DotNetDateTime::from_binary(MAX_TICKS).is_ok());
    }

    #[test]
    fn upload_handle() {
        let mut h = UccUploadHandle::new(5);
        h.add_part(UccPart {
            total: 5,
            index: 0,
            size: 3,
            data: vec![1, 2, 3],
        });
        assert!(!h.upload_complete());
        h.add_part(UccPart {
            total: 5,
            index: 1,
            size: 2,
            data: vec![4, 5],
        });
        assert!(h.upload_complete());
        assert_eq!(h.finalize(), vec![1, 2, 3, 4, 5]);
    }
}
