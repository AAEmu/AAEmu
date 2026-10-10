//! Port of `AAEmu.UnitTests/Commons/Network/PacketStreamTests.cs`.
//!
//! C# members without a dedicated Rust method are expressed with their Rust equivalents:
//! `PushBack` → `write_u8`, `Replace` → `PacketStream::from_bytes(other.as_bytes())`,
//! `Swap` → `std::mem::swap`, `Clone` → `Clone`, `CompareTo` → lexicographic comparison of
//! `as_bytes()` (the C# compares byte by byte, then by length).

use aaemu_commons::PacketStream;

#[test]
fn write_and_read_byte_should_return_same_value() {
    let mut stream = PacketStream::new();
    stream.write_u8(0x7F);
    stream.rollback();
    assert_eq!(stream.read_u8().unwrap(), 0x7F);
}

#[test]
fn write_and_read_int32_should_return_same_value() {
    let mut stream = PacketStream::new();
    stream.write_i32(123456);
    stream.rollback();
    assert_eq!(stream.read_i32().unwrap(), 123456);
}

#[test]
fn write_and_read_string_should_return_same_string() {
    let mut stream = PacketStream::new();
    stream.write_str("Hello xUnit");
    stream.rollback();
    assert_eq!(stream.read_string().unwrap(), "Hello xUnit");
}

#[test]
fn clear_should_reset_count_to_zero() {
    let mut stream = PacketStream::new();
    stream.write_i32(99);
    stream.clear();
    assert_eq!(stream.len(), 0);
}

#[test]
fn replace_with_byte_array_should_copy_data_correctly() {
    let mut original = PacketStream::new();
    original.write_u8(100);
    let mut new_stream = PacketStream::from_bytes(original.as_bytes());
    new_stream.rollback();
    assert_eq!(new_stream.read_u8().unwrap(), 100);
}

#[test]
fn insert_should_insert_bytes_into_the_middle() {
    let mut stream = PacketStream::new();
    stream.write_u8(b'A');
    stream.write_u8(b'C');
    stream.insert(1, b"B");
    stream.rollback();
    assert_eq!(stream.read_bytes(3).unwrap(), b"ABC");
}

#[test]
fn swap_should_exchange_buffers_between_streams() {
    let mut stream1 = PacketStream::new();
    let mut stream2 = PacketStream::new();
    stream1.write_u8(1);
    stream2.write_u8(2);
    std::mem::swap(&mut stream1, &mut stream2);
    stream1.rollback();
    stream2.rollback();
    assert_eq!(stream1.read_u8().unwrap(), 2);
    assert_eq!(stream2.read_u8().unwrap(), 1);
}

#[test]
fn clone_should_create_identical_packet_stream() {
    let mut stream = PacketStream::new();
    stream.write_i32(99);
    let mut clone = stream.clone();
    stream.rollback();
    clone.rollback();
    assert_eq!(clone.read_i32().unwrap(), stream.read_i32().unwrap());
}

#[test]
fn compare_to_should_return_correct_comparison() {
    let mut stream1 = PacketStream::new();
    let mut stream2 = PacketStream::new();
    stream1.write_u8(1);
    stream2.write_u8(2);
    assert!(stream1.as_bytes() < stream2.as_bytes());
}

#[test]
fn write_and_read_position_should_return_same_coordinates() {
    let mut stream = PacketStream::new();
    let (x, y, z) = (1.0f32, 2.0f32, 3.0f32);
    stream.write_position(x, y, z);
    stream.rollback();
    let (rx, ry, rz) = stream.read_position().unwrap();
    assert!((rx - x).abs() <= 0.01);
    assert!((ry - y).abs() <= 0.01);
    assert!((rz - z).abs() <= 0.01);
}

#[test]
fn write_and_read_quaternion_short_should_return_approximately_same_quaternion() {
    let mut stream = PacketStream::new();
    let q = [0.1f32, 0.2, 0.3, 0.4];
    stream.write_quaternion_short(q, false);
    stream.rollback();
    let r = stream.read_quaternion_short().unwrap();
    for i in 0..3 {
        assert!((r[i] - q[i]).abs() <= 0.1, "{i}: {} vs {}", r[i], q[i]);
    }
}
