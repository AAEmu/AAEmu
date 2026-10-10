//! Loot packets: `SCLootBagDataPacket`, `SCLootItemTookPacket`, `SCLootItemFailedPacket`,
//! `SCLootDicePacket`, `SCLootDiceNotifyPacket`, `SCLootDiceSummaryPacket`,
//! `SCLootableStatePacket`, `SCUnitLootingStatePacket`, `SCTeamLootingRuleChangedPacket` and the
//! `LootingRule` model (`Models/Game/Team/LootingRule`). The C->S handlers are in
//! [`super::looting`].

use aaemu_commons::{packet_stream::Result as StreamResult, Marshal, PacketStream};

use crate::models::item::Item;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `LootOwnerType`.
pub mod loot_owner_type {
    pub const NONE: u16 = 0;
    pub const NPC: u16 = 1;
    pub const DOODAD: u16 = 2;
}

/// `LootingRuleMethod`.
pub mod looting_rule_method {
    pub const FREE_FOR_ALL: u8 = 0;
    pub const ROTATE_WINNER: u8 = 1;
    pub const LOOT_MASTER: u8 = 2;
    /// Used internally.
    pub const PUBLIC: u8 = 0xFF;
}

/// `LootingRule`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LootingRule {
    /// `LootingRuleMethod`.
    pub loot_method: u8,
    pub minimum_grade: u8,
    pub loot_master: u32,
    pub roll_for_bind_on_pickup: bool,
}

impl Default for LootingRule {
    /// The C# property initialisers (`RotateWinner`, Grand+, roll for BoP).
    fn default() -> Self {
        Self {
            loot_method: looting_rule_method::ROTATE_WINNER,
            minimum_grade: 2,
            loot_master: 0,
            roll_for_bind_on_pickup: true,
        }
    }
}

impl LootingRule {
    /// `LootingRule.Read`.
    pub fn read(s: &mut PacketStream) -> StreamResult<Self> {
        Ok(Self {
            loot_method: s.read_u8()?,
            minimum_grade: s.read_u8()?,
            loot_master: s.read_u32()?,
            roll_for_bind_on_pickup: s.read_bool()?,
        })
    }
}

impl Marshal for LootingRule {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.loot_method)
            .write_u8(self.minimum_grade)
            .write_u32(self.loot_master)
            .write_bool(self.roll_for_bind_on_pickup);
    }
}

/// `SCLootBagDataPacket(items, lootAll)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCLootBagDataPacket {
    pub items: Vec<Item>,
    pub loot_all: bool,
}
packet_def!(
    SCLootBagDataPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootBagDataPacket
);
impl Marshal for SCLootBagDataPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.items.len() as u8);
        for i in &self.items {
            i.write(s);
        }
        s.write_bool(self.loot_all);
    }
}

/// `SCLootItemTookPacket(itemTemplateId, itemIndex, lootOwnerType, lootOwnerId, count)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCLootItemTookPacket {
    pub item_template_id: u32,
    pub item_index: u16,
    pub loot_owner_type: u16,
    pub loot_owner_id: u32,
    pub count: i32,
}
packet_def!(
    SCLootItemTookPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootItemTookPacket
);
impl Marshal for SCLootItemTookPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.item_template_id)
            .write_u16(self.item_index)
            .write_u16(self.loot_owner_type)
            .write_bc(self.loot_owner_id)
            .write_u8(0)
            .write_i32(self.count);
    }
}

/// `SCLootItemFailedPacket(errorMessage, lootOwnerType, lootOwnerObjId, itemIndex,
/// itemTemplateId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCLootItemFailedPacket {
    /// `ErrorMessageType` (written as an `int`).
    pub error_message: i16,
    pub loot_owner_type: u16,
    pub loot_owner_obj_id: u32,
    pub item_index: u16,
    pub item_template_id: u32,
}
packet_def!(
    SCLootItemFailedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootItemFailedPacket
);
impl Marshal for SCLootItemFailedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(i32::from(self.error_message))
            .write_u16(self.item_index)
            .write_u16(self.loot_owner_type)
            .write_bc(self.loot_owner_obj_id)
            .write_u8(0)
            .write_u32(self.item_template_id);
    }
}

/// `SCLootDicePacket(item)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCLootDicePacket {
    pub item: Item,
}
packet_def!(
    SCLootDicePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootDicePacket
);
impl Marshal for SCLootDicePacket {
    fn write(&self, s: &mut PacketStream) {
        self.item.write(s);
    }
}

/// `SCLootDiceNotifyPacket(charName, item, dice)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCLootDiceNotifyPacket {
    pub char_name: String,
    pub item: Item,
    pub dice: i8,
}
packet_def!(
    SCLootDiceNotifyPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootDiceNotifyPacket
);
impl Marshal for SCLootDiceNotifyPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.char_name);
        self.item.write(s);
        s.write_i8(self.dice);
    }
}

/// `SCLootDiceSummaryPacket(lootOwnerType, lootOwner, itemIndex, diceList)` (`diceList` =
/// `(character id, roll)` in dictionary order).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCLootDiceSummaryPacket {
    pub loot_owner_type: u16,
    pub loot_owner: u32,
    pub item_index: u16,
    pub dice_list: Vec<(u32, i8)>,
}
packet_def!(
    SCLootDiceSummaryPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootDiceSummaryPacket
);
impl Marshal for SCLootDiceSummaryPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.item_index)
            .write_u16(self.loot_owner_type)
            .write_bc(self.loot_owner)
            .write_u8(0)
            .write_i32(self.dice_list.len() as i32);
        for (id, dice) in &self.dice_list {
            s.write_u32(*id).write_i8(*dice);
        }
    }
}

/// `SCLootableStatePacket(lootOwnerType, lootOwnerObjId, hasLoot)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCLootableStatePacket {
    pub loot_owner_type: u16,
    pub loot_owner_obj_id: u32,
    pub has_loot: bool,
}
packet_def!(
    SCLootableStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLootableStatePacket
);
impl Marshal for SCLootableStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(0) // unused itemIndex?
            .write_u16(self.loot_owner_type)
            .write_bc(self.loot_owner_obj_id)
            .write_u8(0)
            .write_bool(self.has_loot);
    }
}

/// `SCUnitLootingStatePacket(unitObjId, looting)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitLootingStatePacket {
    pub unit_obj_id: u32,
    pub looting: u8,
}
packet_def!(
    SCUnitLootingStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitLootingStatePacket
);
impl Marshal for SCUnitLootingStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_obj_id).write_u8(self.looting);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::items::{ItemData, ItemTemplate};
    use crate::net::packet::PacketDef;

    fn body(p: &impl Marshal) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write(&mut s);
        s.into_bytes()
    }

    fn item() -> Item {
        Item::create(
            0x0000_1234_0001_0001,
            &ItemTemplate::plain(77, 10),
            3,
            0,
            1,
            &ItemData::default(),
            1000,
        )
    }

    #[test]
    fn opcodes_are_the_1_2_ones() {
        assert_eq!(SCLootableStatePacket::OPCODE, 0x0f6);
        assert_eq!(SCUnitLootingStatePacket::OPCODE, 0x0f7);
        assert_eq!(SCLootBagDataPacket::OPCODE, 0x0f8);
        assert_eq!(SCLootItemTookPacket::OPCODE, 0x0f9);
        assert_eq!(SCLootItemFailedPacket::OPCODE, 0x0fa);
        assert_eq!(SCLootDicePacket::OPCODE, 0x0fb);
        assert_eq!(SCLootDiceNotifyPacket::OPCODE, 0x0fc);
        assert_eq!(SCLootDiceSummaryPacket::OPCODE, 0x0fd);
    }

    #[test]
    fn loot_bag_data_is_byte_exact() {
        let i = item();
        let mut want = PacketStream::new();
        want.write_u8(1);
        i.write(&mut want);
        want.write_bool(true);
        assert_eq!(
            body(&SCLootBagDataPacket {
                items: vec![i],
                loot_all: true
            }),
            want.into_bytes()
        );
    }

    #[test]
    fn took_failed_state_packets_are_byte_exact() {
        let took = SCLootItemTookPacket {
            item_template_id: 0x0102_0304,
            item_index: 2,
            loot_owner_type: 1,
            loot_owner_id: 0x00ab_cdef,
            count: 5,
        };
        assert_eq!(
            body(&took),
            vec![4, 3, 2, 1, 2, 0, 1, 0, 0xef, 0xcd, 0xab, 0, 5, 0, 0, 0]
        );
        let failed = SCLootItemFailedPacket {
            error_message: 25,
            loot_owner_type: 1,
            loot_owner_obj_id: 0x10,
            item_index: 3,
            item_template_id: 9,
        };
        assert_eq!(
            body(&failed),
            vec![25, 0, 0, 0, 3, 0, 1, 0, 0x10, 0, 0, 0, 9, 0, 0, 0]
        );
        let st = SCLootableStatePacket {
            loot_owner_type: 1,
            loot_owner_obj_id: 0x020304,
            has_loot: true,
        };
        assert_eq!(body(&st), vec![0, 0, 1, 0, 4, 3, 2, 0, 1]);
        let ls = SCUnitLootingStatePacket {
            unit_obj_id: 7,
            looting: 2,
        };
        assert_eq!(body(&ls), vec![7, 0, 0, 2]);
    }

    #[test]
    fn dice_packets_are_byte_exact() {
        let i = item();
        let mut want = PacketStream::new();
        i.write(&mut want);
        assert_eq!(
            body(&SCLootDicePacket { item: i.clone() }),
            want.clone().into_bytes()
        );
        let mut n = PacketStream::new();
        n.write_str("Bob");
        i.write(&mut n);
        n.write_i8(-1);
        assert_eq!(
            body(&SCLootDiceNotifyPacket {
                char_name: "Bob".into(),
                item: i,
                dice: -1
            }),
            n.into_bytes()
        );
        let sum = SCLootDiceSummaryPacket {
            loot_owner_type: 1,
            loot_owner: 5,
            item_index: 1,
            dice_list: vec![(1, 50), (2, -1)],
        };
        assert_eq!(
            body(&sum),
            vec![1, 0, 1, 0, 5, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 50, 2, 0, 0, 0, 0xff]
        );
    }

    #[test]
    fn looting_rule_round_trip_and_team_packet() {
        let rule = LootingRule {
            loot_method: looting_rule_method::LOOT_MASTER,
            minimum_grade: 3,
            loot_master: 0x0102_0304,
            roll_for_bind_on_pickup: false,
        };
        let b = body(&rule);
        assert_eq!(b, vec![2, 3, 4, 3, 2, 1, 0]);
        let mut s = PacketStream::from_bytes(b);
        assert_eq!(LootingRule::read(&mut s).unwrap(), rule);
        assert_eq!(LootingRule::default().loot_method, 1);
    }
}
