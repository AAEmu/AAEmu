//! Port of `Models/Game/Items/Containers/ItemContainer` and `EquipmentContainer` (slot logic,
//! adding new items, bind rules). Moving items between containers, consuming items and the item
//! task packets (`SCItemTaskSuccessPacket`) are later phases; every caller of this phase uses
//! `ItemTaskType.Invalid`, for which the C# sends nothing.

use std::sync::{Arc, Mutex};

use crate::data::items::{backpack_type, bind_type, ItemData, ItemTemplate, TemplateKind};
use crate::models::item::{Item, ITEM_FLAG_SOUL_BOUND};

/// `SlotType`.
pub mod slot_type {
    pub const NONE: u8 = 0;
    pub const EQUIPMENT: u8 = 1;
    pub const INVENTORY: u8 = 2;
    pub const BANK: u8 = 3;
    pub const TRADE: u8 = 4;
    pub const MAIL: u8 = 5;
    pub const AUCTION: u8 = 6;
    pub const EQUIPMENT_MATE: u8 = 252;
    pub const SYSTEM: u8 = 0xFF;
    /// `Enum.GetValues<SlotType>()` order.
    pub const ALL: [u8; 9] = [
        NONE,
        EQUIPMENT,
        INVENTORY,
        BANK,
        TRADE,
        MAIL,
        AUCTION,
        EQUIPMENT_MATE,
        SYSTEM,
    ];
}

/// `EquipmentItemSlot` indices used by this phase.
pub mod equip_slot {
    pub const HEAD: i32 = 0;
    pub const NECK: i32 = 1;
    pub const CHEST: i32 = 2;
    pub const WAIST: i32 = 3;
    pub const LEGS: i32 = 4;
    pub const HANDS: i32 = 5;
    pub const FEET: i32 = 6;
    pub const ARMS: i32 = 7;
    pub const BACK: i32 = 8;
    pub const EAR1: i32 = 9;
    pub const EAR2: i32 = 10;
    pub const FINGER1: i32 = 11;
    pub const FINGER2: i32 = 12;
    pub const UNDERSHIRT: i32 = 13;
    pub const UNDERPANTS: i32 = 14;
    pub const MAINHAND: i32 = 15;
    pub const OFFHAND: i32 = 16;
    pub const RANGED: i32 = 17;
    pub const MUSICAL: i32 = 18;
    pub const FACE: i32 = 19;
    pub const HAIR: i32 = 20;
    pub const GLASSES: i32 = 21;
    pub const RESERVED: i32 = 22;
    pub const TAIL: i32 = 23;
    pub const BODY: i32 = 24;
    pub const BEARD: i32 = 25;
    pub const BACKPACK: i32 = 26;
    pub const COSPLAY: i32 = 27;
}

/// `EquipmentContainer` size: `(int)Enum.GetValues<EquipmentItemSlot>().Max() + 1`.
pub const EQUIPMENT_CONTAINER_SIZE: i32 = 28;

/// `EquipmentContainer.GetAllowedGearSlots(EquipmentItemSlotType)`.
pub fn allowed_gear_slots(slot_type_id: u32) -> &'static [i32] {
    use equip_slot::*;
    match slot_type_id {
        1 => &[HEAD],
        2 => &[NECK],
        3 => &[CHEST],
        4 => &[WAIST],
        5 => &[LEGS],
        6 => &[HANDS],
        7 => &[FEET],
        8 => &[ARMS],
        9 => &[BACK],
        10 => &[EAR1, EAR2],
        11 => &[FINGER1, FINGER2],
        12 => &[UNDERSHIRT],
        13 => &[UNDERPANTS],
        14 => &[MAINHAND],
        15 => &[OFFHAND],
        16 => &[MAINHAND],
        17 => &[MAINHAND, OFFHAND],
        18 => &[RANGED],
        20 => &[OFFHAND],
        21 => &[MUSICAL],
        23 => &[FACE],
        24 => &[HAIR],
        25 => &[GLASSES],
        26 => &[RESERVED],
        27 => &[TAIL],
        28 => &[BODY],
        29 => &[BEARD],
        30 => &[BACKPACK],
        31 => &[COSPLAY],
        _ => &[],
    }
}

/// The concrete container class (`ItemContainer.CreateByTypeName`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ContainerKind {
    Item,
    Equipment,
    MateEquipment,
    Coffer,
}

impl ContainerKind {
    /// `CreateByTypeName` (suffix match, in the C# order).
    pub fn from_type_name(name: &str) -> Self {
        if name.ends_with("MateEquipmentContainer") {
            Self::MateEquipment
        } else if name.ends_with("EquipmentContainer") {
            Self::Equipment
        } else if name.ends_with("CofferContainer") {
            Self::Coffer
        } else {
            Self::Item
        }
    }

    /// `ContainerTypeName()`.
    pub fn type_name(self) -> &'static str {
        match self {
            Self::Item => "ItemContainer",
            Self::Equipment => "EquipmentContainer",
            Self::MateEquipment => "MateEquipmentContainer",
            Self::Coffer => "CofferContainer",
        }
    }
}

/// Shared container reference (`ItemManager._allPersistentContainers` and the character's
/// `Inventory` hold the same object, as in C#).
pub type ContainerRef = Arc<Mutex<ItemContainer>>;

/// `ItemContainer`.
#[derive(Debug, Clone)]
pub struct ItemContainer {
    pub container_id: u64,
    pub kind: ContainerKind,
    pub slot_type: u8,
    pub owner_id: u32,
    pub mate_id: u32,
    size: i32,
    free_slot_count: i32,
    /// `Items` (most recently added first, like `Items.Insert(0, item)`).
    pub items: Vec<Item>,
    pub dirty: bool,
    /// `Owner != null` (an `Inventory` was attached): enables `EquipmentContainer.CanAccept`.
    pub has_owner: bool,
}

impl ItemContainer {
    /// `new ItemContainer(ownerId, slotType, ...)` / `new EquipmentContainer(...)` with a
    /// container id (0 = none).
    pub fn new(kind: ContainerKind, owner_id: u32, slot_type: u8, container_id: u64) -> Self {
        let mut c = Self {
            container_id,
            kind,
            slot_type,
            owner_id,
            mate_id: 0,
            size: 0,
            free_slot_count: 0,
            items: Vec::new(),
            dirty: true,
            has_owner: false,
        };
        c.set_container_size(-1); // Unlimited
        if kind == ContainerKind::Equipment || kind == ContainerKind::MateEquipment {
            c.set_container_size(EQUIPMENT_CONTAINER_SIZE);
        }
        c
    }

    pub fn into_ref(self) -> ContainerRef {
        Arc::new(Mutex::new(self))
    }

    pub fn container_size(&self) -> i32 {
        self.size
    }

    /// `ContainerSize` setter.
    pub fn set_container_size(&mut self, v: i32) {
        if v != self.size {
            self.size = v;
            self.dirty = true;
        }
        self.update_free_slot_count();
    }

    /// `UpdateFreeSlotCount`.
    pub fn update_free_slot_count(&mut self) {
        self.free_slot_count = if self.size < 0 {
            9999
        } else {
            self.size - self.items.len() as i32
        };
    }

    pub fn free_slot_count(&self) -> i32 {
        self.free_slot_count
    }

    /// `PartOfPlayerInventory`.
    fn part_of_player_inventory(&self) -> bool {
        matches!(
            self.slot_type,
            slot_type::EQUIPMENT | slot_type::INVENTORY | slot_type::BANK | slot_type::TRADE
        )
    }

    /// `GetItemBySlot`.
    pub fn get_item_by_slot(&self, slot: i32) -> Option<&Item> {
        self.items.iter().find(|i| i.slot == slot)
    }

    pub fn get_item_by_id(&self, id: u64) -> Option<&Item> {
        self.items.iter().find(|i| i.id == id)
    }

    /// `GetItemByItemId` for in-place updates (the item's `IsDirty` is the caller's job).
    pub fn get_item_by_id_mut(&mut self, id: u64) -> Option<&mut Item> {
        self.items.iter_mut().find(|i| i.id == id)
    }

    /// `GetSlottedItemsList` (one entry per slot `0..ContainerSize`).
    pub fn slotted_items(&self) -> Vec<Option<Item>> {
        (0..self.size.max(0))
            .map(|i| self.get_item_by_slot(i).cloned())
            .collect()
    }

    /// `GetUnusedSlot`.
    pub fn get_unused_slot(&self, preferred: i32) -> i32 {
        if self.size < 0 {
            let highest = self.items.iter().map(|i| i.slot).max().unwrap_or(-1) + 1;
            return preferred.max(highest);
        }
        let need_new = preferred < 0 || self.items.iter().any(|i| i.slot == preferred);
        if !need_new {
            return preferred;
        }
        let used: Vec<i32> = self
            .items
            .iter()
            .filter(|i| i.slot != preferred)
            .map(|i| i.slot)
            .collect();
        (0..self.size).find(|s| !used.contains(s)).unwrap_or(-1)
    }

    /// `GetAllItemsByTemplate` -> (items, units).
    pub fn items_by_template(&self, template_id: u32, grade: i32) -> (Vec<u64>, i32) {
        let mut ids = Vec::new();
        let mut units = 0;
        for i in &self.items {
            if i.template_id == template_id && (grade < 0 || grade == i32::from(i.grade)) {
                ids.push(i.id);
                units += i.count;
            }
        }
        (ids, units)
    }

    /// `CanAccept` (base + `EquipmentContainer` override).
    pub fn can_accept(
        &self,
        item: &Item,
        template: Option<&ItemTemplate>,
        target_slot: i32,
    ) -> bool {
        if self.kind == ContainerKind::Equipment {
            if !self.has_owner {
                return true; // Not applicable to NPCs
            }
            if target_slot < 0 || target_slot >= self.size {
                tracing::warn!(
                    "({}) tried to equip a item that is out of range of the valid slots {target_slot}/{}",
                    self.owner_id,
                    self.size
                );
                return false;
            }
            let Some(slot_type_id) = template.and_then(ItemTemplate::gear_slot_type) else {
                tracing::warn!(
                    "({}) tried to equip a non-equipable item ({}), Id:{}",
                    self.owner_id,
                    item.template_id,
                    item.id
                );
                return false;
            };
            if !allowed_gear_slots(slot_type_id).contains(&target_slot) {
                tracing::warn!(
                    "({}) tried to equip a item in the wrong slot ({}), Id:{}, TargetSlot:{target_slot}",
                    self.owner_id,
                    item.template_id,
                    item.id
                );
                return false;
            }
            return true;
        }
        if self.part_of_player_inventory() {
            if let Some(TemplateKind::Backpack { backpack_type: bt }) = template.map(|t| &t.kind) {
                return *bt == backpack_type::GLIDER || *bt == backpack_type::TOY_FLAG;
            }
        }
        true
    }

    /// `AddOrMoveExistingItem(ItemTaskType.Invalid, item, preferredSlot)` for an item that is not
    /// held by any container yet (new or orphaned items). Returns the C# result
    /// (`itemTasks.Count > 0`); on `false` the item is handed back.
    #[allow(clippy::result_large_err)] // the item is handed back, like the C# caller keeps it
    pub fn add_new_item(
        &mut self,
        mut item: Item,
        preferred_slot: i32,
        data: &ItemData,
    ) -> Result<bool, Item> {
        let template = data.template(item.template_id);
        let mut new_slot = -1;
        let mut can_add_to_same_slot = false;

        if self.kind == ContainerKind::Equipment && item.class.is_equip() && preferred_slot < 0 {
            if let Some(st) = template.and_then(ItemTemplate::gear_slot_type) {
                if let Some(&s) = allowed_gear_slots(st)
                    .iter()
                    .find(|&&s| self.get_item_by_slot(s).is_none())
                {
                    new_slot = s;
                }
            }
        }

        let max_count = template.map_or(0, |t| t.max_count);
        let stack_target = self.get_item_by_slot(preferred_slot).map(|c| {
            (
                c.template_id == item.template_id && c.grade == item.grade,
                c.count,
            )
        });
        if self.slot_type == slot_type::INVENTORY
            && max_count > 1
            && matches!(stack_target, Some((true, c)) if item.count + c <= max_count)
        {
            new_slot = preferred_slot;
            can_add_to_same_slot = true;
        } else {
            if new_slot < 0 {
                new_slot = self.get_unused_slot(preferred_slot);
            }
            if new_slot < 0 {
                return Err(item); // Inventory Full
            }
        }

        if !self.can_accept(&item, template, new_slot) {
            return Err(item);
        }

        let has_task = self.slot_type != slot_type::NONE;
        if can_add_to_same_slot {
            let add = item.count;
            if let Some(target) = self.items.iter_mut().find(|i| i.slot == preferred_slot) {
                target.count += add;
                target.dirty = true;
            }
        } else {
            item.slot_type = self.slot_type;
            item.slot = new_slot;
            item.owner_id = u64::from(self.owner_id);
            item.dirty = true;
            self.items.insert(0, item);
            self.update_free_slot_count();
            // OnEnterContainer: EquipmentContainer broadcasts SCUnitEquipmentsChanged and runs
            // UpdateGearBonuses on its ParentUnit (only relevant in the world; later phase).
        }
        self.apply_bind_rules(data);
        Ok(has_task)
    }

    /// `ApplyBindRules` (the item task packet is not sent for `ItemTaskType.Invalid`... the C#
    /// does send one if anything got bound; with no connection attached nothing is delivered).
    pub fn apply_bind_rules(&mut self, data: &ItemData) {
        for item in &mut self.items {
            if item.flags & ITEM_FLAG_SOUL_BOUND != 0 {
                continue;
            }
            let bt = data.template(item.template_id).map_or(0, |t| t.bind_type);
            if (self.slot_type == slot_type::INVENTORY && bt == bind_type::BIND_ON_PICKUP)
                || (self.slot_type == slot_type::EQUIPMENT && bt == bind_type::BIND_ON_EQUIP)
            {
                item.flags |= ITEM_FLAG_SOUL_BOUND;
                item.dirty = true;
            }
        }
    }

    /// `Items.Remove(item)` (used by `Wipe` / `RemoveItem`).
    pub fn remove_item(&mut self, id: u64) -> Option<Item> {
        let idx = self.items.iter().position(|i| i.id == id)?;
        let item = self.items.remove(idx);
        self.update_free_slot_count();
        Some(item)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::items::EquipTemplate;

    fn data() -> ItemData {
        let mut d = ItemData::default();
        d.templates.insert(1, ItemTemplate::plain(1, 10));
        d.templates.insert(
            2,
            ItemTemplate {
                kind: TemplateKind::Armor(EquipTemplate {
                    slot_type_id: 3,
                    ..EquipTemplate::default()
                }),
                ..ItemTemplate::plain(2, 1)
            },
        );
        d
    }

    fn item(id: u64, template: u32, count: i32, d: &ItemData) -> Item {
        Item::create(id, d.template(template).unwrap(), count, 0, 1, d, 0)
    }

    #[test]
    fn bag_slots_and_stacking() {
        let d = data();
        let mut bag = ItemContainer::new(ContainerKind::Item, 7, slot_type::INVENTORY, 100);
        bag.set_container_size(2);
        assert_eq!(bag.add_new_item(item(1, 1, 3, &d), -1, &d), Ok(true));
        assert_eq!(bag.get_item_by_slot(0).unwrap().owner_id, 7);
        // Same template into the occupied preferred slot stacks.
        assert_eq!(bag.add_new_item(item(2, 1, 4, &d), 0, &d), Ok(true));
        assert_eq!(bag.get_item_by_slot(0).unwrap().count, 7);
        assert_eq!(bag.items.len(), 1);
        assert_eq!(bag.add_new_item(item(3, 1, 9, &d), -1, &d), Ok(true));
        assert_eq!(bag.get_item_by_slot(1).unwrap().id, 3);
        assert!(bag.add_new_item(item(4, 1, 1, &d), -1, &d).is_err());
        assert_eq!(bag.free_slot_count(), 0);
    }

    #[test]
    fn equipment_slot_rules() {
        let d = data();
        let mut eq = ItemContainer::new(ContainerKind::Equipment, 7, slot_type::EQUIPMENT, 101);
        eq.has_owner = true;
        assert_eq!(eq.container_size(), 28);
        // Wrong slot is refused, auto slot picks Chest.
        assert!(eq.add_new_item(item(1, 2, 1, &d), 5, &d).is_err());
        let armor = Item {
            class: crate::models::item::ItemClass::Armor,
            ..item(2, 2, 1, &d)
        };
        assert_eq!(eq.add_new_item(armor, -1, &d), Ok(true));
        assert_eq!(eq.get_item_by_slot(2).unwrap().id, 2);
        let slotted = eq.slotted_items();
        assert_eq!(slotted.len(), 28);
        assert!(slotted[2].is_some());
    }
}
