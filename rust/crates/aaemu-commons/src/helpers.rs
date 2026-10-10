//! Port of the wire-relevant parts of `AAEmu.Commons.Utils.Helpers`.

use std::net::Ipv4Addr;
use std::time::{SystemTime, UNIX_EPOCH};

use chrono::{DateTime, NaiveDate, Utc};

pub fn unix_time_now() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

pub fn unix_time_now_millis() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

/// Multiplicative checksum used by the S->C stream cipher.
pub fn crc8(data: &[u8]) -> u8 {
    data.iter().fold(0u32, |c, &b| {
        c.wrapping_mul(0x13).wrapping_add(u32::from(b))
    }) as u8
}

/// IPv4 in the byte order the client expects (reversed octets).
pub fn convert_ip(ip: Ipv4Addr) -> [u8; 4] {
    let mut o = ip.octets();
    o.reverse();
    o
}

fn long_to_coord(v: i64) -> f32 {
    (v >> 32) as f32 / 4096.0
}

fn coord_to_long(v: f32) -> i64 {
    ((v * 4096.0) as i64) << 32
}

/// Decodes the 9-byte packed world position (x/y: 24-bit fixed point with sign bits in byte 8,
/// z: 22-bit fixed point).
pub fn decode_position(v: &[u8; 9]) -> (f32, f32, f32) {
    let raw = |a: u8, b: u8, c: u8| 8 * (i64::from(a) | i64::from(b) << 8 | i64::from(c) << 16);
    let mut x = raw(v[0], v[1], v[2]) << 32;
    let mut y = raw(v[3], v[4], v[5]) << 32;
    if v[8] & 0x80 != 0 {
        x = x.wrapping_neg();
    }
    if v[8] & 0x40 != 0 {
        y = y.wrapping_neg();
    }
    let z_raw = u64::from(v[6]) | u64::from(v[7]) << 8 | u64::from(v[8] & 0x3F) << 16;
    let z = z_raw as f64 * 0.000_000_238_418_58 * 4196.0 - 100.0;
    let z = ((z * 10_000.0).round_ties_even() / 10_000.0) as f32;
    (long_to_coord(x), long_to_coord(y), z)
}

pub fn encode_position(x: f32, y: f32, z: f32) -> [u8; 9] {
    let pack = |l: i64| {
        let pre = l >> 31;
        (pre ^ l.wrapping_add(pre).wrapping_add(i64::from(pre < 0))) >> 3
    };
    let rx = pack(coord_to_long(x));
    let ry = pack(coord_to_long(y));
    let rz = (f64::from((z + 100.0) / 4196.0 * 4_194_304.0) + 0.5).floor() as i64;

    let sign = (u8::from(y < 0.0) + 2 * u8::from(x < 0.0)) << 6;
    [
        (rx >> 32) as u8,
        (rx >> 40) as u8,
        (rx >> 48) as u8,
        (ry >> 32) as u8,
        (ry >> 40) as u8,
        (ry >> 48) as u8,
        rz as u8,
        (rz >> 8) as u8,
        (((rz >> 16) & 0x3F) as u8).wrapping_add(sign),
    ]
}

#[allow(clippy::excessive_precision)]
pub fn convert_rotation_to_short(rotation: i8) -> i16 {
    (f32::from(rotation) * 0.007_874_016 / 0.000_030_518_509) as i16
}

#[allow(clippy::excessive_precision)]
pub fn convert_rotation_to_sbyte(rotation: i16) -> i8 {
    (f32::from(rotation) * 0.000_030_518_509 / 0.007_874_016) as i8
}

// ---- remaining `AAEmu.Commons.Utils.Helpers` -------------------------------------------------

/// `Helpers.ExePath`: full path of the running executable.
pub fn exe_path() -> Option<std::path::PathBuf> {
    std::env::current_exe().ok()
}

/// `Helpers.BaseDirectory`: directory of the running executable (empty if unknown).
pub fn base_directory() -> std::path::PathBuf {
    exe_path()
        .and_then(|p| p.parent().map(std::path::Path::to_path_buf))
        .unwrap_or_default()
}

/// `Helpers.Is64Bit`. Note: the C# reports whether the *OS* is 64-bit; this reports the build
/// target's pointer width.
pub const IS_64BIT: bool = cfg!(target_pointer_width = "64");

/// `Helpers.UnixTime(DateTime)`: seconds since the Unix epoch, 0 for earlier times.
pub fn unix_time(time: DateTime<Utc>) -> i64 {
    if time < DateTime::UNIX_EPOCH {
        return 0;
    }
    time.timestamp()
}

/// `Helpers.UnixTime(long)`: the `DateTime` for a Unix timestamp, clamped to .NET's
/// `DateTime.MinValue..=MaxValue` (years 1..=9999).
///
/// Note: the C# compared against `DateTime.MaxValue.Second` / `MinValue.Second` (the *seconds
/// component*, 59 / 0), so every timestamp above 59 returned `DateTime.MaxValue` and negative
/// ones `DateTime.MinValue`. This implements the evident intent instead.
pub fn unix_time_to_date_time(time: i64) -> DateTime<Utc> {
    let min = NaiveDate::from_ymd_opt(1, 1, 1)
        .and_then(|d| d.and_hms_opt(0, 0, 0))
        .map(|d| d.and_utc())
        .unwrap_or(DateTime::<Utc>::MIN_UTC);
    let max = NaiveDate::from_ymd_opt(9999, 12, 31)
        .and_then(|d| d.and_hms_nano_opt(23, 59, 59, 999_999_900))
        .map(|d| d.and_utc())
        .unwrap_or(DateTime::<Utc>::MAX_UTC);
    match DateTime::from_timestamp(time, 0) {
        Some(t) => t.clamp(min, max),
        None if time < 0 => min,
        None => max,
    }
}

/// `Math.Round(value, 4, MidpointRounding.ToEven)` for doubles.
fn round4_even(v: f64) -> f64 {
    if v.abs() < 1e16 {
        (v * 1e4).round_ties_even() / 1e4
    } else {
        v
    }
}

/// `Helpers.ConvertX(byte[])` (obsolete in C#; use [`decode_position`]).
#[deprecated(note = "use decode_position")]
pub fn convert_x_from_bytes(coords: [u8; 3]) -> f32 {
    let v =
        f32::from(coords[0]) * 0.002 + f32::from(coords[1]) * 0.5 + f32::from(coords[2]) * 128.0;
    round4_even(f64::from(v)) as f32
}

/// `Helpers.ConvertX(float)` (obsolete in C#; use [`encode_position`]).
#[deprecated(note = "use encode_position")]
pub fn convert_x_to_bytes(x: f32) -> [u8; 3] {
    let mut temp = x;
    let c2 = (temp / 128.0) as u8;
    temp -= f32::from(c2) * 128.0;
    let c1 = (temp / 0.5) as u8;
    temp -= f32::from(c1) * 0.5;
    let c0 = (temp * 512.0) as u8;
    [c0, c1, c2]
}

/// `Helpers.ConvertY(byte[])` (identical to X).
#[deprecated(note = "use decode_position")]
pub fn convert_y_from_bytes(coords: [u8; 3]) -> f32 {
    #[allow(deprecated)]
    convert_x_from_bytes(coords)
}

/// `Helpers.ConvertY(float)` (identical to X).
#[deprecated(note = "use encode_position")]
pub fn convert_y_to_bytes(y: f32) -> [u8; 3] {
    #[allow(deprecated)]
    convert_x_to_bytes(y)
}

/// `Helpers.ConvertZ(byte[])`.
#[deprecated(note = "use decode_position")]
pub fn convert_z_from_bytes(coords: [u8; 3]) -> f32 {
    let v = f32::from(coords[0]) * 0.001
        + f32::from(coords[1]) * 0.2561
        + f32::from(coords[2]) * 65.5625
        - 100.0;
    round4_even(f64::from(v)) as f32
}

/// `Helpers.ConvertZ(float)` (mixes float and double constants exactly like the C#).
#[deprecated(note = "use encode_position")]
pub fn convert_z_to_bytes(z: f32) -> [u8; 3] {
    let mut temp = z + 100.0;
    let c2 = (temp / 65.5625) as u8;
    temp -= f32::from(c2) * 65.5625;
    let c1 = (f64::from(temp) / 0.2561) as u8;
    temp -= f32::from(c1) * 0.2561;
    let c0 = (f64::from(temp) / 0.001) as u8;
    [c0, c1, c2]
}

/// `Helpers.ConvertLongX/ConvertLongY(long)`: fixed point (`<< 32`, 1/4096 units) to float.
pub fn convert_long_to_coord(v: i64) -> f32 {
    long_to_coord(v)
}

/// `Helpers.ConvertLongX/ConvertLongY(float)`.
pub fn convert_coord_to_long(v: f32) -> i64 {
    coord_to_long(v)
}

/// `Helpers.StringToByteArray`: hex string to bytes; `None` on odd length / invalid digits.
pub fn string_to_byte_array(hex: &str) -> Option<Vec<u8>> {
    if !hex.len().is_multiple_of(2) {
        return None;
    }
    (0..hex.len())
        .step_by(2)
        .map(|i| {
            hex.get(i..i + 2)
                .and_then(|p| u8::from_str_radix(p, 16).ok())
        })
        .collect()
}

/// `Helpers.ConvertIp(string)`: parses an IPv4 or IPv6 address and returns its bytes reversed.
pub fn convert_ip_str(ip: &str) -> Result<Vec<u8>, std::net::AddrParseError> {
    let addr: std::net::IpAddr = ip.parse()?;
    let mut bytes = match addr {
        std::net::IpAddr::V4(a) => a.octets().to_vec(),
        std::net::IpAddr::V6(a) => a.octets().to_vec(),
    };
    bytes.reverse();
    Ok(bytes)
}

/// `Helpers.ConvertRadianToSbyteDirection`: radians to the client's sbyte direction
/// (`Convert.ToSByte`: banker's rounding). `None` where the C# threw `OverflowException`.
#[allow(clippy::excessive_precision)]
pub fn convert_radian_to_sbyte_direction(radian: f32) -> Option<i8> {
    let z = f64::from(radian) * 0.159_154_943_091_895_335_768_883_763_372_51;
    let v = (z * 127.0).round_ties_even();
    if (-128.0..=127.0).contains(&v) {
        Some(v as i8)
    } else {
        None
    }
}

/// `Helpers.SplitList` / `SplitArray`: splits into chunks of `size` (last one may be shorter).
pub fn split_list<T: Clone>(list: &[T], size: usize) -> Vec<Vec<T>> {
    list.chunks(size.max(1)).map(<[T]>::to_vec).collect()
}

#[cfg(test)]
mod more_tests {
    use super::*;
    use chrono::TimeZone;

    #[test]
    fn unix_times() {
        let t = Utc.with_ymd_and_hms(2020, 1, 1, 0, 0, 0).unwrap();
        assert_eq!(unix_time(t), 1_577_836_800);
        assert_eq!(
            unix_time(Utc.with_ymd_and_hms(1960, 1, 1, 0, 0, 0).unwrap()),
            0
        );
        assert_eq!(unix_time_to_date_time(1_577_836_800), t);
        assert_eq!(
            unix_time_to_date_time(i64::MAX).format("%Y").to_string(),
            "9999"
        );
        assert_eq!(
            unix_time_to_date_time(i64::MIN).format("%Y").to_string(),
            "0001"
        );
        assert!(unix_time_now() > 1_577_836_800);
        assert!(unix_time_now_millis() > 1_577_836_800_000);
    }

    #[test]
    #[allow(deprecated)]
    fn legacy_coords() {
        let b = convert_x_to_bytes(1000.25);
        assert_eq!(b, [128, 208, 7]);
        assert!((convert_x_from_bytes(b) - 1000.256).abs() < 0.0001);
        assert_eq!(
            convert_y_from_bytes([1, 2, 3]),
            convert_x_from_bytes([1, 2, 3])
        );
        assert_eq!(convert_y_to_bytes(5.0), convert_x_to_bytes(5.0));
        let z = convert_z_to_bytes(0.0);
        assert_eq!(z[2], 1);
        assert!(convert_z_from_bytes(z).abs() < 0.01);
    }

    #[test]
    fn misc() {
        assert_eq!(convert_coord_to_long(1.0), 4096 << 32);
        assert_eq!(convert_long_to_coord(4096 << 32), 1.0);
        assert_eq!(string_to_byte_array("00ff10"), Some(vec![0, 255, 16]));
        assert_eq!(string_to_byte_array("0"), None);
        assert_eq!(string_to_byte_array("zz"), None);
        assert_eq!(convert_ip_str("127.0.0.1").unwrap(), vec![1, 0, 0, 127]);
        assert_eq!(convert_ip_str("::1").unwrap()[0], 1);
        assert!(convert_ip_str("nope").is_err());
        assert_eq!(
            convert_radian_to_sbyte_direction(std::f32::consts::FRAC_PI_2),
            Some(32)
        );
        assert_eq!(
            convert_radian_to_sbyte_direction(-std::f32::consts::PI),
            Some(-64)
        );
        assert_eq!(convert_radian_to_sbyte_direction(100.0), None);
        assert_eq!(
            split_list(&[1, 2, 3, 4, 5], 2),
            vec![vec![1, 2], vec![3, 4], vec![5]]
        );
        assert!(split_list::<i32>(&[], 3).is_empty());
        assert!(base_directory().is_dir());
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn negative_position_roundtrip() {
        let p = encode_position(-100.5, -2000.0, -50.0);
        let (x, y, z) = decode_position(&p);
        assert!((x + 100.5).abs() < 0.01, "{x}");
        assert!((y + 2000.0).abs() < 0.01, "{y}");
        assert!((z + 50.0).abs() < 0.01, "{z}");
    }
}
