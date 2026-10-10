//! Gimmick S->C packets not covered by `packets::world_objects`.

use aaemu_commons::{Marshal, PacketStream};

use crate::models::gimmick::Gimmick;
use crate::models::transform::{convert_long, PositionAndRotation};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `SCGimmickMovementPacket(gimmick)` (level 1).
#[derive(Debug, Clone, PartialEq)]
pub struct SCGimmickMovementPacket {
    pub obj_id: u32,
    /// `Gimmick.Time` (ms since UTC midnight).
    pub time: u32,
    /// `Transform.World`.
    pub world: PositionAndRotation,
    pub vel: [f32; 3],
    pub ang_vel: [f32; 3],
    pub scale: f32,
}

impl SCGimmickMovementPacket {
    /// Snapshot of `gimmick` (its transform is its world transform: gimmicks have no parent).
    pub fn new(obj_id: u32, time: u32, gimmick: &Gimmick) -> Self {
        Self {
            obj_id,
            time,
            world: gimmick.unit.transform.local(),
            vel: gimmick.vel,
            ang_vel: gimmick.ang_vel,
            scale: gimmick.unit.scale,
        }
    }
}

packet_def!(
    SCGimmickMovementPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGimmickMovementPacket
);

impl Marshal for SCGimmickMovementPacket {
    fn write(&self, s: &mut PacketStream) {
        let q = self.world.to_quaternion();
        s.write_u32(self.obj_id)
            .write_u32(self.time)
            .write_i64(convert_long(self.world.x))
            .write_i64(convert_long(self.world.y))
            .write_f32(self.world.z)
            .write_f32(q.x)
            .write_f32(q.y)
            .write_f32(q.z)
            .write_f32(q.w)
            .write_f32(self.vel[0])
            .write_f32(self.vel[1])
            .write_f32(self.vel[2])
            .write_f32(self.ang_vel[0])
            .write_f32(self.ang_vel[1])
            .write_f32(self.ang_vel[2])
            .write_f32(self.scale);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::PacketDef;

    #[test]
    fn movement_packet_bytes() {
        let mut g = Gimmick::new(0);
        g.unit.transform.set_xyz(1.0, 2.0, 3.5);
        g.unit.scale = 1.0;
        g.vel = [0.0, 0.0, 0.25];
        let p = SCGimmickMovementPacket::new(0x0A0B0C0D, 0x01020304, &g);
        let mut s = PacketStream::new();
        p.write(&mut s);
        let mut e: Vec<u8> = Vec::new();
        e.extend_from_slice(&[0x0D, 0x0C, 0x0B, 0x0A]); // objId (uint)
        e.extend_from_slice(&[0x04, 0x03, 0x02, 0x01]); // time
        e.extend_from_slice(&(4096i64 << 32).to_le_bytes()); // x
        e.extend_from_slice(&(8192i64 << 32).to_le_bytes()); // y
        e.extend_from_slice(&3.5f32.to_le_bytes()); // z
        for f in [0.0f32, 0.0, 0.0, 1.0] {
            e.extend_from_slice(&f.to_le_bytes()); // identity quaternion
        }
        for f in [0.0f32, 0.0, 0.25, 0.0, 0.0, 0.0, 1.0] {
            e.extend_from_slice(&f.to_le_bytes()); // vel, angVel, scale
        }
        assert_eq!(s.as_bytes(), &e[..]);
        assert_eq!(s.as_bytes().len(), 4 + 4 + 8 + 8 + 4 + 16 + 12 + 12 + 4);
        assert_eq!(SCGimmickMovementPacket::OPCODE, 0x13e);
        assert_eq!(SCGimmickMovementPacket::LEVEL, 1);
    }
}
