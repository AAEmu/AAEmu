//! S->C packets of the item skill effects and of the mate / slave gear moves:
//! `SCGradeEnchantResultPacket`, `SCGradeEnchantBroadcastPacket`,
//! `SCItemSocketingLunagemResultPacket`, `SCItemSocketingLunastoneResultPacket`,
//! `SCItemUccDataChangedPacket`, `SCMateEquipmentChangedPacket`, `SCSlaveEquipmentChangedPacket`
//! and a local `SCSkillEndedPacket` (the effect bodies send it; dedup with `systems::skills`).

use aaemu_commons::{Marshal, PacketStream};

use crate::models::item::Item;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `GradeEnchantResult` (the `result` byte of the grade enchant packets).
pub mod grade_enchant_result {
    pub const BREAK: u8 = 0;
    pub const DOWNGRADE: u8 = 1;
    pub const FAIL: u8 = 2;
    pub const SUCCESS: u8 = 3;
    pub const GREAT_SUCCESS: u8 = 4;
}

/// `stream.Write(item)` of an `Item` reference that may be null (`stream.Write(0)` = an `int` 0).
fn write_opt_item(s: &mut PacketStream, item: Option<&Item>) {
    match item {
        None => {
            s.write_i32(0);
        }
        Some(i) => i.write(s),
    }
}

/// `SCGradeEnchantResultPacket(result, item, type1, type2)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCGradeEnchantResultPacket {
    pub result: u8,
    pub item: Item,
    /// The grade before the enchant.
    pub type1: u8,
    /// The grade after the enchant.
    pub type2: u8,
}
packet_def!(
    SCGradeEnchantResultPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGradeEnchantResultPacket
);
impl Marshal for SCGradeEnchantResultPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.result);
        self.item.write(s);
        s.write_u8(self.type1).write_u8(self.type2);
    }
}

/// `SCGradeEnchantBroadcastPacket(charName, result, item, type1, type2)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCGradeEnchantBroadcastPacket {
    pub char_name: String,
    pub result: u8,
    pub item: Item,
    pub type1: u8,
    pub type2: u8,
}
packet_def!(
    SCGradeEnchantBroadcastPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGradeEnchantBroadcastPacket
);
impl Marshal for SCGradeEnchantBroadcastPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.char_name).write_u8(self.result);
        self.item.write(s);
        s.write_u8(self.type1).write_u8(self.type2);
    }
}

/// `SCItemSocketingLunagemResultPacket(result, itemId, type, install)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCItemSocketingLunagemResultPacket {
    pub result: u8,
    pub item_id: u64,
    pub template_id: u32,
    pub install: bool,
}
packet_def!(
    SCItemSocketingLunagemResultPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCItemSocketingLunagemResultPacket
);
impl Marshal for SCItemSocketingLunagemResultPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.result)
            .write_u64(self.item_id)
            .write_u32(self.template_id)
            .write_bool(self.install);
    }
}

/// `SCItemSocketingLunastoneResultPacket(result, itemId, type)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCItemSocketingLunastoneResultPacket {
    pub result: bool,
    pub item_id: u64,
    pub template_id: u32,
}
packet_def!(
    SCItemSocketingLunastoneResultPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCItemSocketingLunastoneResultPacket
);
impl Marshal for SCItemSocketingLunastoneResultPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.result)
            .write_u64(self.item_id)
            .write_u32(self.template_id);
    }
}

/// `SCItemUccDataChangedPacket(uccId, playerId, targetItemId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCItemUccDataChangedPacket {
    pub ucc_id: u64,
    pub player_id: u32,
    pub target_item_id: u64,
}
packet_def!(
    SCItemUccDataChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCItemUccDataChangedPacket
);
impl Marshal for SCItemUccDataChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(self.ucc_id)
            .write_u32(self.player_id)
            .write_u64(self.target_item_id);
    }
}

/// `SCSkillEndedPacket(tlId)`. Local copy for the effect bodies that send it themselves
/// (`GradeEnchant`, `ItemSocketing`); the skills subsystem owns the canonical one.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCSkillEndedPacket {
    pub tl_id: u16,
}
packet_def!(
    SCSkillEndedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSkillEndedPacket
);
impl Marshal for SCSkillEndedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.tl_id);
    }
}

/// `ItemAndLocation` (an item reference + where it is / goes).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ItemAndLocation {
    pub item: Option<Item>,
    pub slot_type: u8,
    pub slot: u8,
}

/// `SCMateEquipmentChangedPacket(itemOnPet, itemInBag, mateTlId, characterId, passengerId, bts,
/// success)`. `num` is always 1 and the trailing success byte is always `true` (the C# ignores
/// the `success` argument).
#[derive(Debug, Clone, PartialEq)]
pub struct SCMateEquipmentChangedPacket {
    pub item_on_pet: ItemAndLocation,
    pub item_in_bag: ItemAndLocation,
    pub mate_tl_id: u16,
    pub character_id: u32,
    pub passenger_id: u32,
    pub bts: bool,
}
packet_def!(
    SCMateEquipmentChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMateEquipmentChangedPacket
);
impl Marshal for SCMateEquipmentChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u16(self.mate_tl_id)
            .write_u32(self.passenger_id)
            .write_bool(self.bts)
            .write_u8(1); // num
        write_opt_item(s, self.item_on_pet.item.as_ref());
        write_opt_item(s, self.item_in_bag.item.as_ref());
        s.write_u8(self.item_on_pet.slot_type)
            .write_u8(self.item_on_pet.slot)
            .write_u8(self.item_in_bag.slot_type)
            .write_u8(self.item_in_bag.slot)
            .write_bool(true); // success
    }
}

/// `SCSlaveEquipmentChangedPacket(itemOnSlave, itemInBag, slaveTlId, characterId, dbSlaveId,
/// bts, success, expireTime)` (`expireTime` is not written). Nothing sends it in the 1.2 C#.
#[derive(Debug, Clone, PartialEq)]
pub struct SCSlaveEquipmentChangedPacket {
    pub item_on_slave: ItemAndLocation,
    pub item_in_bag: ItemAndLocation,
    pub slave_tl_id: u16,
    pub character_id: u32,
    pub db_slave_id: u32,
    pub bts: bool,
    pub success: bool,
}
packet_def!(
    SCSlaveEquipmentChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSlaveEquipmentChangedPacket
);
impl Marshal for SCSlaveEquipmentChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u16(self.slave_tl_id)
            .write_u32(self.db_slave_id)
            .write_bool(self.bts);
        write_opt_item(s, self.item_on_slave.item.as_ref());
        write_opt_item(s, self.item_in_bag.item.as_ref());
        s.write_u8(self.item_on_slave.slot_type)
            .write_u8(self.item_on_slave.slot)
            .write_u8(self.item_in_bag.slot_type)
            .write_u8(self.item_in_bag.slot)
            .write_bool(self.success);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::items::{EquipTemplate, ItemTemplate, TemplateKind};
    use crate::data::ItemData;

    fn body(p: &impl Marshal) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write(&mut s);
        s.into_bytes()
    }

    fn equip_item() -> Item {
        let d = ItemData::default();
        let t = ItemTemplate::new(
            700,
            TemplateKind::Armor(EquipTemplate {
                slot_type_id: 3,
                ..EquipTemplate::default()
            }),
        );
        let mut i = Item::create(0x55, &t, 1, 4, 1, &d, 1_000);
        i.class = t.class_type(); // no grade data: `create` fell back to a plain item
        i.details.gem_ids[0] = 9;
        i
    }

    fn item_bytes(i: &Item) -> Vec<u8> {
        let mut s = PacketStream::new();
        // Item.Write
        s.write_u32(i.template_id)
            .write_u64(i.id)
            .write_u8(i.grade)
            .write_u8(i.flags)
            .write_i32(i.count)
            .write_u8(1); // DetailType.Equipment
        s.write_u32(i.details.image_item_template_id)
            .write_u8(i.details.durability)
            .write_i16(0)
            .write_u32(i.details.rune_id)
            .write_i64(i.charge_start_time)
            .write_u32(i.details.dye_item_id);
        for g in i.details.gem_ids {
            s.write_u32(g);
        }
        s.write_u16(i.details.temper_physical)
            .write_u16(i.details.temper_magical)
            .write_i64(i.create_time)
            .write_i32(i.lifespan_mins)
            .write_u32(i.made_unit_id)
            .write_u8(i.world_id)
            .write_i64(i.unsecure_time)
            .write_i64(i.unpack_time);
        s.into_bytes()
    }

    #[test]
    fn grade_enchant_result_and_broadcast() {
        let i = equip_item();
        let p = SCGradeEnchantResultPacket {
            result: grade_enchant_result::SUCCESS,
            item: i.clone(),
            type1: 4,
            type2: 5,
        };
        let mut want = vec![3u8];
        want.extend(item_bytes(&i));
        want.extend([4, 5]);
        assert_eq!(body(&p), want);

        let p = SCGradeEnchantBroadcastPacket {
            char_name: "Alice".into(),
            result: grade_enchant_result::GREAT_SUCCESS,
            item: i.clone(),
            type1: 7,
            type2: 9,
        };
        let mut want = vec![5u8, 0, b'A', b'l', b'i', b'c', b'e', 4];
        want.extend(item_bytes(&i));
        want.extend([7, 9]);
        assert_eq!(body(&p), want);
    }

    #[test]
    fn socketing_and_ucc_packets() {
        let p = SCItemSocketingLunagemResultPacket {
            result: 1,
            item_id: 0x0102_0304_0506_0708,
            template_id: 0x1122_3344,
            install: true,
        };
        assert_eq!(
            body(&p),
            [1, 8, 7, 6, 5, 4, 3, 2, 1, 0x44, 0x33, 0x22, 0x11, 1]
        );
        let p = SCItemSocketingLunastoneResultPacket {
            result: true,
            item_id: 2,
            template_id: 3,
        };
        assert_eq!(body(&p), [1, 2, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0]);
        let p = SCItemUccDataChangedPacket {
            ucc_id: 0x10,
            player_id: 0x20,
            target_item_id: 0x30,
        };
        assert_eq!(
            body(&p),
            [0x10, 0, 0, 0, 0, 0, 0, 0, 0x20, 0, 0, 0, 0x30, 0, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(body(&SCSkillEndedPacket { tl_id: 0x1234 }), [0x34, 0x12]);
    }

    #[test]
    fn mate_and_slave_equipment_changed() {
        let i = equip_item();
        let p = SCMateEquipmentChangedPacket {
            item_on_pet: ItemAndLocation {
                item: Some(i.clone()),
                slot_type: 2,
                slot: 7,
            },
            item_in_bag: ItemAndLocation {
                item: None,
                slot_type: 252,
                slot: 3,
            },
            mate_tl_id: 0x0a0b,
            character_id: 9,
            passenger_id: 0,
            bts: false,
        };
        let mut want = vec![9, 0, 0, 0, 0x0b, 0x0a, 0, 0, 0, 0, 0, 1];
        want.extend(item_bytes(&i));
        want.extend([0, 0, 0, 0]); // null item: int 0
        want.extend([2, 7, 252, 3, 1]);
        assert_eq!(body(&p), want);

        let p = SCSlaveEquipmentChangedPacket {
            item_on_slave: ItemAndLocation::default(),
            item_in_bag: ItemAndLocation {
                item: Some(i.clone()),
                slot_type: 2,
                slot: 1,
            },
            slave_tl_id: 5,
            character_id: 6,
            db_slave_id: 7,
            bts: true,
            success: false,
        };
        let mut want = vec![6, 0, 0, 0, 5, 0, 7, 0, 0, 0, 1];
        want.extend([0, 0, 0, 0]);
        want.extend(item_bytes(&i));
        want.extend([0, 0, 2, 1, 0]);
        assert_eq!(body(&p), want);
    }
}
