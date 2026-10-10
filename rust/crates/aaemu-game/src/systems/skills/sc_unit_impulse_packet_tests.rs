//! Port of AAEmu.UnitTests/Game/Core/Packets/G2C/SCUnitImpulsePacketTests.cs

use aaemu_commons::{Marshal, PacketStream};

use super::packets_special::SCUnitImpulsePacket;

#[test]
fn write_writes_obj_id_and_crafted_obj_id_float() {
    const OBJ_ID: u32 = 0x123456;
    let packet = SCUnitImpulsePacket {
        obj_id: OBJ_ID,
        vel_impulse: [1.0, 2.0, 3.0],
        angvel_impulse: [4.0, 5.0, 6.0],
        impulse: [7.0, 8.0, 9.0],
        ang_impulse: [10.0, 11.0, 12.0],
    };
    let mut s = PacketStream::new();
    packet.write(&mut s);
    let mut s = PacketStream::from_bytes(s.into_bytes());

    assert_eq!(s.read_bc().unwrap(), OBJ_ID);
    assert_eq!(s.read_f32().unwrap().to_bits() as i32, (OBJ_ID << 8) as i32);
    assert_eq!(s.read_f32().unwrap(), 1.0);
    assert_eq!(s.read_f32().unwrap(), 2.0);
    assert_eq!(s.read_f32().unwrap(), 3.0);
}

#[test]
fn encode_unit_obj_id_float_packs_obj_id_into_high_three_bytes() {
    let bytes = SCUnitImpulsePacket::encode_obj_id_float(0x123456).to_le_bytes();
    assert_eq!(bytes, [0x00, 0x56, 0x34, 0x12]);
}
