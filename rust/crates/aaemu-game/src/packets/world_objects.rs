//! S->C visibility packets of the non-player world objects (`AAEmu.Game.Core.Packets.G2C`):
//! doodads, gimmicks, houses, shipyards, mates, slaves, plus `SCTargetChangedPacket` and
//! `SCUnitAttachedPacket`. All level 1; field order and widths follow the 1.2 C# `Write`.
//!
//! Packets whose C# `Write` reads a live object (`SCDoodadCreatedPacket`,
//! `SCDoodadsCreatedPacket`, `SCGimmicksCreatedPacket`, `SCHouseStatePacket`) carry their body
//! pre-encoded by `world::visibility`.

use aaemu_commons::{Marshal, PacketStream};

use super::{packet_def, raw_body_packet};
use crate::models::shipyard::ShipyardData;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;

raw_body_packet!(SCDoodadCreatedPacket, sc::SCDoodadCreatedPacket);
raw_body_packet!(SCHouseStatePacket, sc::SCHouseStatePacket);

/// `SCDoodadsCreatedPacket(doodads)`: `count: u8` + `Doodad.Write` per doodad (`bodies`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDoodadsCreatedPacket {
    pub bodies: Vec<Vec<u8>>,
}
impl SCDoodadsCreatedPacket {
    /// `MaxCountPerPacket`.
    pub const MAX_COUNT_PER_PACKET: usize = 30;
}
packet_def!(
    SCDoodadsCreatedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadsCreatedPacket
);
impl Marshal for SCDoodadsCreatedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.bodies.len() as u8);
        for b in &self.bodies {
            s.write_raw(b);
        }
    }
}

/// `SCDoodadRemovedPacket(id)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDoodadRemovedPacket {
    pub obj_id: u32,
}
packet_def!(
    SCDoodadRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadRemovedPacket
);
impl Marshal for SCDoodadRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bool(false); // e: false = the doodad is deleted
    }
}

/// `SCDoodadsRemovedPacket(last, ids)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDoodadsRemovedPacket {
    pub last: bool,
    pub ids: Vec<u32>,
}
impl SCDoodadsRemovedPacket {
    /// `MaxCountPerPacket`.
    pub const MAX_COUNT_PER_PACKET: usize = 400;
}
packet_def!(
    SCDoodadsRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadsRemovedPacket
);
impl Marshal for SCDoodadsRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.ids.len() as u16).write_bool(self.last);
        for &id in &self.ids {
            s.write_bc(id).write_bool(false);
        }
    }
}

/// `SCGimmicksCreatedPacket(gimmicks)`: `count: u8` + `Gimmick.Write` per gimmick.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCGimmicksCreatedPacket {
    pub bodies: Vec<Vec<u8>>,
}
impl SCGimmicksCreatedPacket {
    /// `MaxCountPerPacket`.
    pub const MAX_COUNT_PER_PACKET: usize = 30;
}
packet_def!(
    SCGimmicksCreatedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGimmicksCreatedPacket
);
impl Marshal for SCGimmicksCreatedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.bodies.len() as u8);
        for b in &self.bodies {
            s.write_raw(b);
        }
    }
}

/// `SCGimmicksRemovedPacket(ids)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCGimmicksRemovedPacket {
    pub ids: Vec<u32>,
}
impl SCGimmicksRemovedPacket {
    /// `MaxCountPerPacket`.
    pub const MAX_COUNT_PER_PACKET: usize = 500;
}
packet_def!(
    SCGimmicksRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGimmicksRemovedPacket
);
impl Marshal for SCGimmicksRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.ids.len() as u16);
        for &id in &self.ids {
            s.write_bc(id);
        }
    }
}

/// `SCGimmickJointsBrokenPacket(gimmicks)` (always sent empty by the C#).
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct SCGimmickJointsBrokenPacket {
    pub gimmick_obj_ids: Vec<u32>,
}
packet_def!(
    SCGimmickJointsBrokenPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGimmickJointsBrokenPacket
);
impl Marshal for SCGimmickJointsBrokenPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.gimmick_obj_ids.len() as u8);
        for &id in &self.gimmick_obj_ids {
            s.write_u32(id).write_i32(0).write_i32(0); // gimmickId, jointId, epicenter
        }
    }
}

/// `SCShipyardStatePacket(shipyardData)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCShipyardStatePacket {
    pub data: ShipyardData,
}
packet_def!(
    SCShipyardStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCShipyardStatePacket
);
impl Marshal for SCShipyardStatePacket {
    fn write(&self, s: &mut PacketStream) {
        self.data.write(s);
        s.write_i32(self.data.step);
    }
}

/// `SCMateStatePacket(objId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCMateStatePacket {
    pub obj_id: u32,
}
packet_def!(
    SCMateStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMateStatePacket
);
impl Marshal for SCMateStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_i32(0).write_i32(0); // skillCount, tagCount
    }
}

/// `SCSlaveStatePacket(objId, tlId, creatorName, ownerId, dbId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCSlaveStatePacket {
    pub obj_id: u32,
    pub tl_id: u16,
    pub creator_name: String,
    pub owner_obj_id: u32,
    pub db_id: u32,
}
packet_def!(
    SCSlaveStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSlaveStatePacket
);
impl Marshal for SCSlaveStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u16(self.tl_id)
            .write_u64(0) // type
            .write_i32(0) // skillCount
            .write_i32(0) // tagCount
            .write_str(&self.creator_name)
            .write_u32(self.owner_obj_id)
            .write_u32(self.db_id);
    }
}

/// `SCTargetChangedPacket(id, targetId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTargetChangedPacket {
    pub obj_id: u32,
    pub target_obj_id: u32,
}
packet_def!(
    SCTargetChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTargetChangedPacket
);
impl Marshal for SCTargetChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bc(self.target_obj_id);
    }
}

/// `SCUnitAttachedPacket(childUnitObjId, point, reason, id)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUnitAttachedPacket {
    pub child_obj_id: u32,
    /// `AttachPointKind`.
    pub point: u8,
    /// `AttachUnitReason`.
    pub reason: u8,
    pub obj_id: u32,
}
packet_def!(
    SCUnitAttachedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitAttachedPacket
);
impl Marshal for SCUnitAttachedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.child_obj_id)
            .write_u8(self.point)
            .write_bc(self.obj_id)
            .write_u8(self.reason);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn frame(op: u16, body: &[u8]) -> Vec<u8> {
        let mut v = vec![0xDD, 0x01, 0, 0];
        v.extend_from_slice(&op.to_le_bytes());
        v.extend_from_slice(body);
        v
    }

    #[test]
    fn doodad_removal_packets() {
        assert_eq!(
            encode(&SCDoodadRemovedPacket { obj_id: 0x0A0B0C }),
            frame(0x10d, &[0x0C, 0x0B, 0x0A, 0])
        );
        assert_eq!(
            encode(&SCDoodadsRemovedPacket {
                last: true,
                ids: vec![1, 0x020304]
            }),
            frame(0x113, &[2, 0, 1, 1, 0, 0, 0, 4, 3, 2, 0])
        );
    }

    #[test]
    fn doodads_created_concatenates_bodies() {
        assert_eq!(
            encode(&SCDoodadsCreatedPacket {
                bodies: vec![vec![1, 2], vec![3]]
            }),
            frame(0x112, &[2, 1, 2, 3])
        );
    }

    #[test]
    fn gimmick_packets() {
        assert_eq!(
            encode(&SCGimmicksRemovedPacket { ids: vec![5] }),
            frame(0x13d, &[1, 0, 5, 0, 0])
        );
        assert_eq!(
            encode(&SCGimmickJointsBrokenPacket::default()),
            frame(0x13f, &[0])
        );
        assert_eq!(
            encode(&SCGimmickJointsBrokenPacket {
                gimmick_obj_ids: vec![7]
            }),
            frame(0x13f, &[1, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
        );
    }

    #[test]
    fn state_packets() {
        assert_eq!(
            encode(&SCMateStatePacket { obj_id: 0x112233 }),
            frame(0x1ac, &[0x33, 0x22, 0x11, 0, 0, 0, 0, 0, 0, 0, 0])
        );
        let mut body = vec![0x01, 0, 0, 0x05, 0x00];
        body.extend_from_slice(&[0; 8 + 4 + 4]);
        body.extend_from_slice(&[2, 0, b'A', b'b']);
        body.extend_from_slice(&9u32.to_le_bytes());
        body.extend_from_slice(&10u32.to_le_bytes());
        assert_eq!(
            encode(&SCSlaveStatePacket {
                obj_id: 1,
                tl_id: 5,
                creator_name: "Ab".into(),
                owner_obj_id: 9,
                db_id: 10
            }),
            frame(0x1ab, &body)
        );
        assert_eq!(
            encode(&SCTargetChangedPacket {
                obj_id: 1,
                target_obj_id: 0
            }),
            frame(0x084, &[1, 0, 0, 0, 0, 0])
        );
        assert_eq!(
            encode(&SCUnitAttachedPacket {
                child_obj_id: 2,
                point: 1,
                reason: 7,
                obj_id: 3
            }),
            frame(0x076, &[2, 0, 0, 1, 3, 0, 0, 7])
        );
    }

    #[test]
    fn shipyard_state_layout() {
        let d = ShipyardData {
            id: 0x0102030405060708,
            template_id: 3,
            x: 1.0,
            y: 2.0,
            z: 3.5,
            z_rot: 0.25,
            money_amount: -1,
            actions: 4,
            kind: 5,
            kind2: 6,
            owner_name: "Zed".into(),
            kind3: 101,
            spawned: 1_700_000_000,
            obj_id: 0x0A0B0C,
            hp: 1000,
            step: 2,
        };
        let mut b = Vec::new();
        b.extend_from_slice(&0x0102030405060708u64.to_le_bytes());
        b.extend_from_slice(&3u32.to_le_bytes());
        b.extend_from_slice(&(4096i64 << 32).to_le_bytes());
        b.extend_from_slice(&(8192i64 << 32).to_le_bytes());
        b.extend_from_slice(&3.5f32.to_le_bytes());
        b.extend_from_slice(&0.25f32.to_le_bytes());
        b.extend_from_slice(&(-1i32).to_le_bytes());
        b.extend_from_slice(&4i32.to_le_bytes());
        b.extend_from_slice(&5u32.to_le_bytes());
        b.extend_from_slice(&6u32.to_le_bytes());
        b.extend_from_slice(&[3, 0, b'Z', b'e', b'd']);
        b.extend_from_slice(&101u32.to_le_bytes());
        b.extend_from_slice(&1_700_000_000i64.to_le_bytes());
        b.extend_from_slice(&[0x0C, 0x0B, 0x0A]);
        b.extend_from_slice(&1000i32.to_le_bytes());
        b.extend_from_slice(&2i32.to_le_bytes());
        assert_eq!(encode(&SCShipyardStatePacket { data: d }), frame(0x1aa, &b));
    }
}
