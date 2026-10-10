//! Port of `Models/Game/Items/Actions` (`ItemTask` and every subclass, `ItemAction`,
//! `ItemTaskType`) plus [`ItemFx`], the ordered list of side effects an item operation
//! produces (the packets C# sends to `Container.Owner` inline, the `Inventory.OnAcquiredItem` /
//! `OnConsumedItem` quest events, `EquipmentContainer.OnEnter/LeaveContainer` and
//! `ItemManager.ReleaseId`). Container operations run under the container locks and only
//! *record* effects; `systems::economy::fx::flush` executes them afterwards in the same order
//! (see `CONTRIBUTING-PORT.md`, locking rules).

use aaemu_commons::{Marshal, PacketStream};

use super::item::{Item, UnixTime};

/// `ItemTaskType` (the reason byte of `SCItemTaskSuccessPacket`).
pub mod task_type {
    pub const INVALID: u8 = 0;
    pub const DESTROY: u8 = 1;
    pub const ABOX_DESTROY: u8 = 2;
    pub const REPAIR: u8 = 3;
    pub const DURABILITY_LOSS: u8 = 4;
    pub const SWAP_ITEMS: u8 = 5;
    pub const SPLIT: u8 = 6;
    pub const SPLIT_COFFER_ITEMS: u8 = 7;
    pub const SWAP_COFFER_ITEMS: u8 = 8;
    pub const LOOT: u8 = 9;
    pub const LOOT_ALL: u8 = 10;
    pub const GM: u8 = 11;
    pub const GAME_RULE_RESET: u8 = 12;
    pub const CONSUME_SKILL_SOURCE: u8 = 13;
    pub const DOODAD_CREATE: u8 = 14;
    pub const DOODAD_REMOVE: u8 = 15;
    pub const DOODAD_ITEM_CHANGER: u8 = 16;
    pub const DOODAD_INTERACTION: u8 = 17;
    pub const DOODAD_CATTLE_FEED: u8 = 18;
    pub const UPGRADE_SKILL: u8 = 19;
    pub const ABILITY_CHANGE: u8 = 20;
    pub const ABILITY_RESET: u8 = 21;
    pub const BUY_PRIEST_BUFF: u8 = 22;
    pub const TELEPORT: u8 = 23;
    pub const CAPTURE_PET: u8 = 24;
    pub const RECOVER_DOODAD_ITEM: u8 = 25;
    pub const MATE_CREATE: u8 = 26;
    pub const CRAFT_ACT_SAVED: u8 = 27;
    pub const CRAFT_PAY_SAVED: u8 = 28;
    pub const CRAFT_PICKUP_PRODUCT: u8 = 29;
    pub const CRAFT_CANCEL: u8 = 30;
    pub const HOUSE_CREATION: u8 = 31;
    pub const HOUSE_DEPOSIT: u8 = 32;
    pub const HOUSE_BUILDING: u8 = 33;
    pub const PICKUP_BLOODSTAIN: u8 = 34;
    pub const AUTO_LOOT_DOODAD_ITEM: u8 = 35;
    pub const QUEST_START: u8 = 36;
    pub const QUEST_COMPLETE: u8 = 37;
    pub const QUEST_SUPPLY_ITEMS: u8 = 38;
    pub const QUEST_REMOVE_SUPPLIES: u8 = 39;
    pub const SKILL_REAGENTS: u8 = 40;
    pub const SKILL_EFFECT_CONSUMPTION: u8 = 41;
    pub const SKILL_EFFECT_GAIN_ITEM: u8 = 42;
    pub const SKILL_EFFECT_GAIN_ITEM_WITH_POS: u8 = 43;
    pub const SKILL_EFFECT_SIEGE_TICKET: u8 = 44;
    pub const SKILL_EFFECT_EXP_TO_ITEM: u8 = 45;
    pub const AUCTION: u8 = 46;
    pub const MAIL: u8 = 47;
    pub const TRADE: u8 = 48;
    pub const ENCHANT_MAGICAL: u8 = 49;
    pub const ENCHANT_PHYSICAL: u8 = 50;
    pub const GET_COIN_BY_ITEM: u8 = 51;
    pub const GET_ITEM_BY_COIN: u8 = 52;
    pub const STORE_SELL: u8 = 53;
    pub const STORE_BUY: u8 = 54;
    pub const TOD_REWARD: u8 = 55;
    pub const GAIN_ITEM_WITH_UCC: u8 = 56;
    pub const IMPRINT_UCC: u8 = 57;
    pub const REPAIR_PETS: u8 = 58;
    pub const MATE_DEATH: u8 = 59;
    pub const SHIPYARD: u8 = 60;
    pub const SKILLS_RESET: u8 = 61;
    pub const DROP_BACKPACK: u8 = 62;
    pub const USE_RELIC: u8 = 63;
    pub const USE_INDEPENDENCE_RELIC: u8 = 64;
    pub const CONVERSION: u8 = 65;
    pub const SEIZE: u8 = 66;
    pub const RETURN_SEIZED: u8 = 67;
    pub const DEMO_DRESS_OFF: u8 = 68;
    pub const DEMO_DRESS_ON: u8 = 69;
    pub const DEMO_CLEAR_BAG: u8 = 70;
    pub const DEMO_FILL_BAG: u8 = 71;
    pub const SLAVE_DEATH: u8 = 72;
    pub const EXPEDITION_CREATION: u8 = 73;
    pub const REPAIR_SLAVES: u8 = 74;
    pub const EXPAND_BAG: u8 = 75;
    pub const EXPAND_BANK: u8 = 76;
    pub const RENEW_EQUIPMENT: u8 = 77;
    pub const LIFESPAN_EXPIRATION: u8 = 78;
    pub const RECOVER_EXP: u8 = 79;
    pub const SPAWNER_UPDATE: u8 = 80;
    pub const UPDATE_SUMMON_SLAVE_ITEM: u8 = 81;
    pub const UPDATE_SUMMON_MATE_ITEM: u8 = 82;
    pub const DEPOSIT_MONEY: u8 = 83;
    pub const WITHDRAW_MONEY: u8 = 84;
    pub const DELIVER_ITEM_TO_OTHERS: u8 = 85;
    pub const SET_SLAVE_POSITION: u8 = 86;
    pub const SET_BOUNTY_MONEY: u8 = 87;
    pub const PAY_BOUNTY_MONEY: u8 = 88;
    pub const CONVERT_FISH: u8 = 89;
    pub const FISHING: u8 = 90;
    pub const SELL_HOUSE: u8 = 91;
    pub const BUY_HOUSE: u8 = 92;
    pub const SAVE_MUSIC_NOTES: u8 = 93;
    pub const ITEM_LOCK: u8 = 94;
    pub const ITEM_UNLOCK: u8 = 95;
    pub const ITEM_UNLOCK_EXCESS: u8 = 96;
    pub const GRADE_ENCHANT: u8 = 97;
    pub const RECHARGE_BUFF: u8 = 98;
    pub const SOCKETING: u8 = 99;
    pub const DYEING: u8 = 100;
    pub const CONSUME_INDUN_TICKET: u8 = 101;
    pub const EXPAND_EXPERT: u8 = 102;
    pub const EXCHANGE: u8 = 103;
    pub const SELL_BACKPACK: u8 = 104;
    pub const SELL_SPECIALTY: u8 = 105;
    pub const ASK_MOULD: u8 = 106;
    pub const TAKE_MOULD: u8 = 107;
    pub const FACTION_DECLARE_HOSTILE: u8 = 108;
    pub const EDIT_COSMETIC: u8 = 109;
    pub const CHANGE_AUTO_USE_AA_POINT: u8 = 110;
    pub const CONVERT_ITEM_LOOK: u8 = 111;
    pub const CHANGE_EXPERT_LIMIT: u8 = 112;
    pub const SKINIZE: u8 = 113;
    pub const ITEM_TASK_THISTIME_UNPACK: u8 = 114;
    pub const BUY_PREMIUM_SERVICE: u8 = 115;
    pub const BUY_AA_POINT: u8 = 116;
}

/// `ItemAction` (the first byte of every task).
pub mod item_action {
    pub const INVALID: u8 = 0;
    pub const CHANGE_MONEY_AMOUNT: u8 = 1;
    pub const CHANGE_BANK_MONEY_AMOUNT: u8 = 2;
    pub const CHANGE_GAME_POINT: u8 = 3;
    pub const ADD_STACK: u8 = 4;
    pub const CREATE: u8 = 5;
    pub const TAKE: u8 = 6;
    pub const REMOVE: u8 = 7;
    pub const SWAP_SLOT: u8 = 8;
    pub const UPDATE_DETAIL: u8 = 9;
    pub const SET_FLAGS_BITS: u8 = 10;
    pub const UPDATE_FLAGS: u8 = 11;
    pub const REMOVE_CRAFTING: u8 = 12;
    pub const SEIZE: u8 = 13;
    pub const CHANGE_GRADE: u8 = 14;
    pub const CHANGE_OWNER: u8 = 15;
    pub const CHANGE_AA_POINT: u8 = 16;
    pub const CHANGE_BANK_AA_POINT: u8 = 17;
    pub const CHANGE_AUTO_USE_AA_POINT: u8 = 18;
    pub const UPDATE_CHARGE_USE_SKILL_TIME: u8 = 19;
}

/// One `ItemTask` subclass. Tasks that reference an `Item` keep a snapshot taken when the
/// task is created (the C# packet is encoded by `SendPacket` right after, before the item can
/// change again).
#[derive(Debug, Clone, PartialEq)]
pub enum ItemTask {
    /// `MoneyChange(amount)`.
    MoneyChange(i32),
    /// `MoneyChangeBank(amount)`.
    MoneyChangeBank(i32),
    /// `ChangeGamePoint(kind, amount)`.
    ChangeGamePoint { kind: u8, amount: i32 },
    /// `AAPointUpdate(amount)`.
    AaPointUpdate(i32),
    /// `ChangeBankAAPoint(amount)`.
    ChangeBankAaPoint(i32),
    /// `ChangeAutoUseAAPoint(change)`.
    ChangeAutoUseAaPoint(u8),
    /// `ItemAdd(item)` (`ItemAction.Create`).
    ItemAdd(Box<Item>),
    /// `ItemAddNew(item)` (`ItemAction.ChangeOwner`).
    ItemAddNew(Box<Item>),
    /// `ItemBuyback(item)` (`ItemAction.Take`).
    ItemBuyback(Box<Item>),
    /// `ItemCountUpdate(item, count)`.
    ItemCountUpdate {
        slot_type: u8,
        slot: u8,
        item_id: u64,
        count: i32,
        template_id: u32,
    },
    /// `ItemGradeChange(item, grade)`.
    ItemGradeChange {
        slot_type: u8,
        slot: u8,
        item_id: u64,
        grade: u8,
    },
    /// `ItemMove(fromType, fromSlot, fromId, toType, toSlot, toId)`.
    ItemMove {
        from_slot_type: u8,
        from_slot: u8,
        from_item_id: u64,
        to_slot_type: u8,
        to_slot: u8,
        to_item_id: u64,
    },
    /// `ItemRemove(...)`.
    ItemRemove {
        slot_type: u8,
        slot: u8,
        item_id: u64,
        template_id: u32,
    },
    /// `ItemRemoveCrafting(id)`.
    ItemRemoveCrafting(u64),
    /// `ItemRemoveSlot(...)` (`ItemAction.Seize`).
    ItemRemoveSlot {
        slot_type: u8,
        slot: u8,
        item_id: u64,
    },
    /// `ItemUpdate(item)`.
    ItemUpdate(Box<Item>),
    /// `ItemUpdateBits(item)`.
    ItemUpdateBits {
        slot_type: u8,
        slot: u8,
        item_id: u64,
        bits: u8,
    },
    /// `ItemUpdateSecurity(item, bits, isUnsecureExcess, isUnsecureSet, isUnpack)`.
    ItemUpdateSecurity {
        slot_type: u8,
        slot: u8,
        item_id: u64,
        bits: u8,
        is_unsecure_excess: bool,
        is_unsecure_set: bool,
        is_unpack: bool,
        unsecure_time: UnixTime,
        unpack_time: UnixTime,
    },
}

impl ItemTask {
    pub fn add(item: &Item) -> Self {
        Self::ItemAdd(Box::new(item.clone()))
    }

    pub fn add_new(item: &Item) -> Self {
        Self::ItemAddNew(Box::new(item.clone()))
    }

    pub fn buyback(item: &Item) -> Self {
        Self::ItemBuyback(Box::new(item.clone()))
    }

    pub fn update(item: &Item) -> Self {
        Self::ItemUpdate(Box::new(item.clone()))
    }

    /// `new ItemCountUpdate(item, count)`.
    pub fn count_update(item: &Item, count: i32) -> Self {
        Self::ItemCountUpdate {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            count,
            template_id: item.template_id,
        }
    }

    /// `new ItemGradeChange(item, grade)`.
    pub fn grade_change(item: &Item, grade: u8) -> Self {
        Self::ItemGradeChange {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            grade,
        }
    }

    /// `new ItemRemove(item)`.
    pub fn remove(item: &Item) -> Self {
        Self::ItemRemove {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            template_id: item.template_id,
        }
    }

    /// `new ItemRemoveSlot(item)`.
    pub fn remove_slot(item: &Item) -> Self {
        Self::ItemRemoveSlot {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
        }
    }

    /// `new ItemUpdateBits(item)`.
    pub fn update_bits(item: &Item) -> Self {
        Self::ItemUpdateBits {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            bits: item.flags,
        }
    }

    /// `new ItemUpdateSecurity(item, bits, ...)`.
    pub fn update_security(
        item: &Item,
        bits: u8,
        is_unsecure_excess: bool,
        is_unsecure_set: bool,
        is_unpack: bool,
    ) -> Self {
        Self::ItemUpdateSecurity {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            bits,
            is_unsecure_excess,
            is_unsecure_set,
            is_unpack,
            unsecure_time: item.unsecure_time,
            unpack_time: item.unpack_time,
        }
    }

    /// The `ItemAction` byte (`_type`).
    pub fn action(&self) -> u8 {
        use item_action::*;
        match self {
            Self::MoneyChange(_) => CHANGE_MONEY_AMOUNT,
            Self::MoneyChangeBank(_) => CHANGE_BANK_MONEY_AMOUNT,
            Self::ChangeGamePoint { .. } => CHANGE_GAME_POINT,
            Self::AaPointUpdate(_) => CHANGE_AA_POINT,
            Self::ChangeBankAaPoint(_) => CHANGE_BANK_AA_POINT,
            Self::ChangeAutoUseAaPoint(_) => CHANGE_AUTO_USE_AA_POINT,
            Self::ItemAdd(_) => CREATE,
            Self::ItemAddNew(_) => CHANGE_OWNER,
            Self::ItemBuyback(_) => TAKE,
            Self::ItemCountUpdate { .. } => ADD_STACK,
            Self::ItemGradeChange { .. } => CHANGE_GRADE,
            Self::ItemMove { .. } => SWAP_SLOT,
            Self::ItemRemove { .. } => REMOVE,
            Self::ItemRemoveCrafting(_) => REMOVE_CRAFTING,
            Self::ItemRemoveSlot { .. } => SEIZE,
            Self::ItemUpdate(_) => UPDATE_DETAIL,
            Self::ItemUpdateBits { .. } => SET_FLAGS_BITS,
            Self::ItemUpdateSecurity { .. } => UPDATE_FLAGS,
        }
    }
}

/// `ItemTask.WriteDetails(stream, item)`: the full item with the details padded to 128 bytes.
pub fn write_task_item_details(s: &mut PacketStream, item: &Item) {
    s.write_u32(item.template_id)
        .write_u64(item.id)
        .write_u8(item.grade)
        .write_u8(item.flags)
        .write_i32(item.count);
    write_padded_details(s, item);
    s.write_i64(item.create_time)
        .write_i32(item.lifespan_mins)
        .write_u32(item.made_unit_id)
        .write_u8(item.world_id)
        .write_i64(item.unsecure_time)
        .write_i64(item.unpack_time);
}

/// `(short)128` + `DetailType` + `WriteDetails` + zero padding up to 128 bytes.
fn write_padded_details(s: &mut PacketStream, item: &Item) {
    let mut details = PacketStream::new();
    details.write_u8(item.class.detail_type());
    item.write_details_to(&mut details);
    s.write_i16(128).write_raw(details.as_bytes());
    // C#: `new byte[128 - details.Count]` (throws when the details exceed 128 bytes; no 1.2
    // item class comes close).
    let pad = 128usize.saturating_sub(details.len());
    s.write_raw(&vec![0u8; pad]);
}

impl Marshal for ItemTask {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.action());
        match self {
            Self::MoneyChange(a)
            | Self::MoneyChangeBank(a)
            | Self::AaPointUpdate(a)
            | Self::ChangeBankAaPoint(a) => {
                s.write_i32(*a);
            }
            Self::ChangeGamePoint { kind, amount } => {
                s.write_u8(*kind).write_i32(*amount);
            }
            Self::ChangeAutoUseAaPoint(c) => {
                s.write_u8(*c);
            }
            Self::ItemAdd(item) | Self::ItemAddNew(item) | Self::ItemBuyback(item) => {
                s.write_u8(item.slot_type).write_u8(item.slot as u8);
                write_task_item_details(s, item);
            }
            Self::ItemCountUpdate {
                slot_type,
                slot,
                item_id,
                count,
                template_id,
            } => {
                s.write_u8(*slot_type)
                    .write_u8(*slot)
                    .write_u64(*item_id)
                    .write_i32(*count)
                    .write_u32(*template_id);
            }
            Self::ItemGradeChange {
                slot_type,
                slot,
                item_id,
                grade,
            } => {
                s.write_u8(*slot_type)
                    .write_u8(*slot)
                    .write_u64(*item_id)
                    .write_u8(*grade);
            }
            Self::ItemMove {
                from_slot_type,
                from_slot,
                from_item_id,
                to_slot_type,
                to_slot,
                to_item_id,
            } => {
                s.write_u8(*from_slot_type)
                    .write_u8(*from_slot)
                    .write_u8(*to_slot_type)
                    .write_u8(*to_slot)
                    .write_u64(*from_item_id)
                    .write_u64(*to_item_id)
                    .write_i32(0); // flags
            }
            Self::ItemRemove {
                slot_type,
                slot,
                item_id,
                template_id,
            } => {
                s.write_u8(*slot_type)
                    .write_u8(*slot)
                    .write_u64(*item_id)
                    .write_u32(*template_id);
            }
            Self::ItemRemoveCrafting(id) => {
                s.write_u64(*id);
            }
            Self::ItemRemoveSlot {
                slot_type,
                slot,
                item_id,
            } => {
                s.write_u8(*slot_type).write_u8(*slot).write_u64(*item_id);
            }
            Self::ItemUpdate(item) => {
                s.write_u8(item.slot_type)
                    .write_u8(item.slot as u8)
                    .write_u64(item.id);
                write_padded_details(s, item);
            }
            Self::ItemUpdateBits {
                slot_type,
                slot,
                item_id,
                bits,
            } => {
                s.write_u8(*slot_type)
                    .write_u8(*slot)
                    .write_u64(*item_id)
                    .write_u8(*bits)
                    .write_u64(0);
            }
            Self::ItemUpdateSecurity {
                slot_type,
                slot,
                item_id,
                bits,
                is_unsecure_excess,
                is_unsecure_set,
                is_unpack,
                unsecure_time,
                unpack_time,
            } => {
                s.write_u8(*slot_type)
                    .write_u8(*slot)
                    .write_u64(*item_id)
                    .write_u8(*bits)
                    .write_bool(*is_unsecure_excess)
                    .write_bool(*is_unsecure_set)
                    .write_bool(*is_unpack)
                    .write_i64(*unsecure_time)
                    .write_i64(*unpack_time);
            }
        }
    }
}

/// One deferred side effect of an item operation (see the module docs).
#[derive(Debug)]
pub enum ItemFx {
    /// `Owner?.SendPacket(new SCItemTaskSuccessPacket(task, tasks, forceRemove))` where `owner`
    /// is a character id (0 = no owner: nothing is sent).
    Tasks {
        owner: u32,
        task: u8,
        tasks: Vec<ItemTask>,
        force_remove: Vec<u64>,
    },
    /// `SCSyncItemLifespanPacket(added, itemId, templateId, expireTime)` to `owner`.
    Lifespan {
        owner: u32,
        added: bool,
        item_id: u64,
        template_id: u32,
        expire_time: UnixTime,
    },
    /// `Owner?.Inventory.OnAcquiredItem(item, count)`; `count` is what the C# hands to
    /// `QuestManager.DoItemsAcquiredEvents` (the item's count, not the added amount).
    Acquired {
        owner: u32,
        template_id: u32,
        count: i32,
    },
    /// `Owner?.Inventory.OnConsumedItem(item, count)`.
    Consumed {
        owner: u32,
        template_id: u32,
        count: i32,
    },
    /// `EquipmentContainer.OnEnterContainer` (`SCUnitEquipmentsChanged` to the neighbours of
    /// the parent unit + `UpdateGearBonuses(item, null)`). `mate_id` > 0 for mate gear.
    EquipEnter {
        owner: u32,
        mate_id: u32,
        slot: u8,
        item: Box<Item>,
    },
    /// `EquipmentContainer.OnLeaveContainer` (`SCUnitEquipmentsChanged(slot, null)` +
    /// `UpdateGearBonuses(null, item)`).
    EquipLeave {
        owner: u32,
        mate_id: u32,
        prev_slot: u8,
        item: Box<Item>,
    },
    /// `ItemManager.Instance.ReleaseId(itemId)`.
    ReleaseId(u64),
    /// Any other packet to the character `owner`, kept in order with the item tasks.
    Packet {
        owner: u32,
        packet: Box<dyn crate::net::packet::ServerPacket>,
    },
    /// An item object that left every container but stays in the C# `_allItems` (kept as a
    /// loose item by the `ItemManager`).
    Orphaned(Box<Item>),
}

impl ItemFx {
    /// Sets the owner of a `Lifespan` effect built by the `ItemManager` helpers.
    pub fn with_owner(self, new_owner: u32) -> Self {
        match self {
            Self::Lifespan {
                added,
                item_id,
                template_id,
                expire_time,
                ..
            } => Self::Lifespan {
                owner: new_owner,
                added,
                item_id,
                template_id,
                expire_time,
            },
            other => other,
        }
    }
}

/// An ordered effect list.
#[derive(Debug, Default)]
pub struct ItemFxList(pub Vec<ItemFx>);

impl ItemFxList {
    pub fn push(&mut self, fx: ItemFx) {
        self.0.push(fx);
    }

    /// `SCItemTaskSuccessPacket` to the owner (skipped for `ItemTaskType.Invalid` callers by
    /// the caller itself, as in C#).
    pub fn tasks(&mut self, owner: u32, task: u8, tasks: Vec<ItemTask>) {
        self.0.push(ItemFx::Tasks {
            owner,
            task,
            tasks,
            force_remove: Vec::new(),
        });
    }

    /// Queues `packet` for the character `owner` (in order with the other effects).
    pub fn send(&mut self, owner: u32, packet: impl crate::net::packet::ServerPacket + 'static) {
        self.0.push(ItemFx::Packet {
            owner,
            packet: Box::new(packet),
        });
    }

    pub fn append(&mut self, other: ItemFxList) {
        self.0.extend(other.0);
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::items::ItemTemplate;
    use crate::data::ItemData;

    fn item() -> Item {
        let d = ItemData::default();
        let mut i = Item::create(0x11, &ItemTemplate::plain(500, 10), 3, 0, 1, &d, 77);
        i.slot_type = 2;
        i.slot = 4;
        i
    }

    #[test]
    fn count_update_and_move_layout() {
        let i = item();
        let mut s = PacketStream::new();
        ItemTask::count_update(&i, -2).write(&mut s);
        let mut w = PacketStream::new();
        w.write_u8(4)
            .write_u8(2)
            .write_u8(4)
            .write_u64(0x11)
            .write_i32(-2)
            .write_u32(500);
        assert_eq!(s.as_bytes(), w.as_bytes());

        let mut s = PacketStream::new();
        ItemTask::ItemMove {
            from_slot_type: 2,
            from_slot: 1,
            from_item_id: 5,
            to_slot_type: 1,
            to_slot: 3,
            to_item_id: 0,
        }
        .write(&mut s);
        let mut w = PacketStream::new();
        w.write_u8(8)
            .write_u8(2)
            .write_u8(1)
            .write_u8(1)
            .write_u8(3)
            .write_u64(5)
            .write_u64(0)
            .write_i32(0);
        assert_eq!(s.as_bytes(), w.as_bytes());
    }

    #[test]
    fn item_add_pads_details_to_128() {
        let i = item();
        let mut s = PacketStream::new();
        ItemTask::add(&i).write(&mut s);
        let mut w = PacketStream::new();
        w.write_u8(5).write_u8(2).write_u8(4);
        w.write_u32(500)
            .write_u64(0x11)
            .write_u8(0)
            .write_u8(0)
            .write_i32(3)
            .write_i16(128)
            .write_u8(0); // DetailType.Invalid, no details
        w.write_raw(&[0u8; 127]);
        w.write_i64(77)
            .write_i32(0)
            .write_u32(0)
            .write_u8(1)
            .write_i64(0)
            .write_i64(0);
        assert_eq!(s.as_bytes(), w.as_bytes());
        assert_eq!(
            s.len(),
            3 + 4 + 8 + 1 + 1 + 4 + 2 + 128 + 8 + 4 + 4 + 1 + 8 + 8
        );
    }
}
