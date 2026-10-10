//! The S->C packets of the core item layer: `SCItemTaskSuccessPacket`,
//! `SCSyncItemLifespanPacket`, `SCUnitEquipmentsChangedPacket`, `SCInvenExpandedPacket`,
//! `SCGamePointChangedPacket`, `SCCharacterLaborPowerChangedPacket`.

use aaemu_commons::{Marshal, PacketStream};

use crate::models::item::{Item, UnixTime};
use crate::models::item_task::ItemTask;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `SCItemTaskSuccessPacket(action, tasks, forceRemove)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCItemTaskSuccessPacket {
    pub action: u8,
    pub tasks: Vec<ItemTask>,
    pub force_remove: Vec<u64>,
}
packet_def!(
    SCItemTaskSuccessPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCItemTaskSuccessPacket
);
impl Marshal for SCItemTaskSuccessPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.action);
        s.write_u8(self.tasks.len() as u8); // TODO max count 30
        for t in &self.tasks {
            t.write(s);
        }
        s.write_u8(self.force_remove.len() as u8);
        for r in &self.force_remove {
            s.write_u64(*r);
        }
        s.write_u32(0); // type(id)
    }
}

/// `SCSyncItemLifespanPacket(added, itemId, itemTemplateId, expireTime)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCSyncItemLifespanPacket {
    pub added: bool,
    pub item_id: u64,
    pub template_id: u32,
    pub expire_time: UnixTime,
}
packet_def!(
    SCSyncItemLifespanPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSyncItemLifespanPacket
);
impl Marshal for SCSyncItemLifespanPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.added)
            .write_u64(self.item_id)
            .write_u32(self.template_id)
            .write_i64(self.expire_time);
    }
}

/// `SCUnitEquipmentsChangedPacket(objId, (slot, item)[])`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCUnitEquipmentsChangedPacket {
    pub obj_id: u32,
    pub items: Vec<(u8, Option<Item>)>,
}
packet_def!(
    SCUnitEquipmentsChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitEquipmentsChangedPacket
);
impl Marshal for SCUnitEquipmentsChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
        s.write_u8(self.items.len() as u8); // TODO max 28
        for (slot, item) in &self.items {
            s.write_u8(*slot);
            match item {
                None => {
                    s.write_i32(0);
                }
                Some(i) => i.write(s),
            }
        }
    }
}

/// `SCInvenExpandedPacket(slotType, numSlots)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCInvenExpandedPacket {
    pub slot_type: u8,
    pub num_slots: u8,
}
packet_def!(
    SCInvenExpandedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInvenExpandedPacket
);
impl Marshal for SCInvenExpandedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.slot_type).write_u8(self.num_slots);
    }
}

/// `SCGamePointChangedPacket(kind, amount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCGamePointChangedPacket {
    pub kind: u8,
    pub amount: i32,
}
packet_def!(
    SCGamePointChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGamePointChangedPacket
);
impl Marshal for SCGamePointChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind).write_i32(self.amount);
    }
}

/// `SCCharacterLaborPowerChangedPacket(amount, action, point, step)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCCharacterLaborPowerChangedPacket {
    pub amount: i32,
    pub action: i32,
    pub point: i32,
    pub step: u8,
}
packet_def!(
    SCCharacterLaborPowerChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterLaborPowerChangedPacket
);
impl Marshal for SCCharacterLaborPowerChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.amount)
            .write_i32(self.action)
            .write_i32(self.point)
            .write_u8(self.step);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    #[test]
    fn item_task_success_layout() {
        let p = SCItemTaskSuccessPacket {
            action: 83,
            tasks: vec![ItemTask::MoneyChange(-5), ItemTask::MoneyChangeBank(5)],
            force_remove: vec![9],
        };
        let mut w = PacketStream::new();
        w.write_u8(83)
            .write_u8(2)
            .write_u8(1)
            .write_i32(-5)
            .write_u8(2)
            .write_i32(5)
            .write_u8(1)
            .write_u64(9)
            .write_u32(0);
        let f = encode(&p);
        assert_eq!(&f[6..], w.as_bytes());
        assert_eq!(
            u16::from_le_bytes([f[4], f[5]]),
            sc::SCItemTaskSuccessPacket
        );
    }

    #[test]
    fn small_packets_layout() {
        let f = encode(&SCSyncItemLifespanPacket {
            added: true,
            item_id: 7,
            template_id: 8,
            expire_time: 9,
        });
        let mut w = PacketStream::new();
        w.write_bool(true).write_u64(7).write_u32(8).write_i64(9);
        assert_eq!(&f[6..], w.as_bytes());

        let f = encode(&SCUnitEquipmentsChangedPacket {
            obj_id: 0x0102,
            items: vec![(3, None)],
        });
        let mut w = PacketStream::new();
        w.write_bc(0x0102).write_u8(1).write_u8(3).write_i32(0);
        assert_eq!(&f[6..], w.as_bytes());

        let f = encode(&SCInvenExpandedPacket {
            slot_type: 2,
            num_slots: 60,
        });
        assert_eq!(&f[6..], &[2, 60]);

        let f = encode(&SCGamePointChangedPacket {
            kind: 1,
            amount: -3,
        });
        assert_eq!(&f[6..], &[1, 0xfd, 0xff, 0xff, 0xff]);

        let f = encode(&SCCharacterLaborPowerChangedPacket {
            amount: -10,
            action: 2,
            point: 3,
            step: 4,
        });
        let mut w = PacketStream::new();
        w.write_i32(-10).write_i32(2).write_i32(3).write_u8(4);
        assert_eq!(&f[6..], w.as_bytes());
    }
}
