//! Port of the wire-relevant parts of `AAEmu.Commons.Utils.Helpers`.

use std::net::Ipv4Addr;
use std::time::{SystemTime, UNIX_EPOCH};

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
