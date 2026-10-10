//! Port of `Models/Game/Items/Containers/ItemContainer`, `EquipmentContainer`,
//! `MateEquipmentContainer` and `CofferContainer`: slot logic, `AddOrMoveExistingItem`,
//! `RemoveItem`, `ConsumeItem`, `AcquireDefaultItem(Ex)`, `SpaceLeftForItem`, bind rules, `Wipe`.
//!
//! The C# operations send packets to `Owner` and call quest hooks inline; here they record
//! [`ItemFx`] effects that `systems::economy::fx::flush` executes once the container locks are
//! released. Container locks are leaf locks (never lock a character while holding one); two
//! containers are locked together with `systems::economy::inventory::lock_pair`.

use std::sync::{Arc, Mutex};

use crate::data::items::{backpack_type, bind_type, ItemData, ItemTemplate, TemplateKind};
use crate::managers::id::IdManager;
use crate::managers::item::ItemManager;
use crate::models::item::{Item, UnixTime, ITEM_FLAG_SOUL_BOUND};
use crate::models::item_task::{task_type, ItemFx, ItemFxList, ItemTask};

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
    /// `CofferContainer.CofferType` (`ChestType`).
    pub coffer_type: u8,
    /// `CofferContainer.CofferPermission`.
    pub coffer_permission: u8,
}

/// `ChestType.Otherworldly` (private character bound items only).
pub const CHEST_TYPE_OTHERWORLDLY: u8 = 0;
/// `ChestType.Chest` (normal house storage chest with settable permissions).
pub const CHEST_TYPE_CHEST: u8 = 1;

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
            coffer_type: 0,
            coffer_permission: 0,
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
        if self.kind == ContainerKind::Coffer {
            // CofferContainer.CanAccept: not bound, template accepted, then the base rules.
            if item.flags & ITEM_FLAG_SOUL_BOUND != 0 {
                return false;
            }
            if let Some(t) = template {
                // All Chests will not accept timed items
                if t.exp_abs_lifetime > 0
                    || t.exp_online_lifetime > 0
                    || t.exp_date.is_some_and(|d| d > MIN_DATE_TIME)
                {
                    return false;
                }
                if self.coffer_type != CHEST_TYPE_OTHERWORLDLY
                    && (t.bind_type == bind_type::BIND_ON_PICKUP
                        || t.bind_type == bind_type::BIND_ON_PICKUP_PACK)
                {
                    return false;
                }
            }
        }
        if self.is_equipment() {
            // `Owner == null`: not applicable to NPCs (mate gear resolves its owner by id).
            if !self.has_owner && !(self.kind == ContainerKind::MateEquipment && self.owner_id > 0)
            {
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
    /// held by any container yet (new or orphaned items), discarding the side effects (phase-2
    /// callers run without an online owner). Returns the C# result (`itemTasks.Count > 0`); on
    /// `false` the item is handed back.
    #[allow(clippy::result_large_err)] // the item is handed back, like the C# caller keeps it
    pub fn add_new_item(
        &mut self,
        item: Item,
        preferred_slot: i32,
        data: &ItemData,
    ) -> Result<bool, Item> {
        let mut fx = ItemFxList::default();
        self.add_item(task_type::INVALID, item, preferred_slot, data, &mut fx)
    }

    /// `AddOrMoveExistingItem(taskType, item, preferredSlot)` for an item that is in no
    /// container (`_holdingContainer == null`). `Err(item)` hands the item back on failure.
    #[allow(clippy::result_large_err)]
    pub fn add_item(
        &mut self,
        task: u8,
        item: Item,
        preferred_slot: i32,
        data: &ItemData,
        fx: &mut ItemFxList,
    ) -> Result<bool, Item> {
        self.add_or_move_inner(task, item, None, None, preferred_slot, data, fx)
    }

    /// `AddOrMoveExistingItem(taskType, item, preferredSlot)` for an item currently held by
    /// another container `src` (use [`ItemContainer::relocate_item`] when the item is already in
    /// this container). Returns `false` (item left in `src`) when it cannot be placed.
    pub fn move_item_from(
        &mut self,
        src: &mut ItemContainer,
        task: u8,
        item_id: u64,
        preferred_slot: i32,
        data: &ItemData,
        fx: &mut ItemFxList,
    ) -> bool {
        let Some(idx) = src.items.iter().position(|i| i.id == item_id) else {
            return false;
        };
        // The checks run on the item while it is still in the source container; the C# only
        // removes it there once it was placed here.
        if self.place(&src.items[idx], preferred_slot, data).is_none() {
            return false;
        }
        let item = src.items.remove(idx);
        let st = item.slot_type;
        match self.add_or_move_inner(task, item, Some(src), Some(st), preferred_slot, data, fx) {
            Ok(r) => r,
            Err(item) => {
                // Unreachable after the placement check; put it back where it was.
                src.items.insert(idx.min(src.items.len()), item);
                src.update_free_slot_count();
                false
            }
        }
    }

    /// `AddOrMoveExistingItem` with `sourceContainer == this`. The C# re-inserts the item
    /// (`Items.Insert(0, item)`) without removing the old entry, leaving the same object twice
    /// in `Items`; here the entry is moved to the front with its new slot instead (deviation).
    pub fn relocate_item(
        &mut self,
        task: u8,
        item_id: u64,
        preferred_slot: i32,
        data: &ItemData,
        fx: &mut ItemFxList,
    ) -> bool {
        let Some(idx) = self.items.iter().position(|i| i.id == item_id) else {
            return false;
        };
        let item = self.items.remove(idx);
        self.update_free_slot_count();
        let st = item.slot_type;
        match self.add_or_move_inner(task, item, None, Some(st), preferred_slot, data, fx) {
            Ok(r) => r,
            Err(item) => {
                self.items.insert(idx.min(self.items.len()), item);
                self.update_free_slot_count();
                false
            }
        }
    }

    /// The placement decision of `AddOrMoveExistingItem` (slot choice + `CanAccept`):
    /// `(newSlot, canAddToSameSlot)`.
    pub fn place(&self, item: &Item, preferred_slot: i32, data: &ItemData) -> Option<(i32, bool)> {
        let template = data.template(item.template_id);
        let mut new_slot = -1;
        let mut can_add_to_same_slot = false;

        // When adding wearables to equipment container, for the slot numbers if needed
        if self.is_equipment() && item.class.is_equip() && preferred_slot < 0 {
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
                return None; // Inventory Full
            }
        }
        if !self.can_accept(item, template, new_slot) {
            return None;
        }
        Some((new_slot, can_add_to_same_slot))
    }

    /// Body of `AddOrMoveExistingItem`. `src` is the (different) source container the item was
    /// just taken out of; `src_slot_type` is the item's slot type before the call when it came
    /// from a container (`None`: `_holdingContainer == null`).
    #[allow(clippy::too_many_arguments, clippy::result_large_err)]
    fn add_or_move_inner(
        &mut self,
        task: u8,
        mut item: Item,
        src: Option<&mut ItemContainer>,
        src_slot_type: Option<u8>,
        preferred_slot: i32,
        data: &ItemData,
        fx: &mut ItemFxList,
    ) -> Result<bool, Item> {
        let Some((new_slot, can_add_to_same_slot)) = self.place(&item, preferred_slot, data) else {
            return Err(item);
        };
        let source_slot = item.slot as u8;
        let source_slot_type = item.slot_type;
        let from_other = src.is_some();
        let mut item_tasks = Vec::new();
        let mut source_tasks = Vec::new();
        let template_id = item.template_id;
        let item_id = item.id;
        let mut final_count = item.count;

        if can_add_to_same_slot {
            let add = item.count;
            if let Some(target) = self.items.iter_mut().find(|i| i.slot == preferred_slot) {
                target.count += add;
                target.dirty = true;
                if self.slot_type != slot_type::NONE {
                    item_tasks.push(ItemTask::count_update(target, add));
                }
            }
        } else {
            item.slot_type = self.slot_type;
            item.slot = new_slot;
            item.owner_id = u64::from(self.owner_id);
            item.dirty = true;
            final_count = item.count;
            // Note we use SlotType.None for things like the Item BuyBack Container.
            if self.slot_type != slot_type::NONE {
                item_tasks.push(ItemTask::add(&item));
            }
            let entered = item.clone();
            // insert at front for easy buyback handling
            self.items.insert(0, item);
            self.update_free_slot_count();
            if src_slot_type.is_none() || from_other {
                // sourceContainer != this: sourceContainer?.OnLeaveContainer + OnEnterContainer
                if let Some(s) = src.as_deref() {
                    s.on_leave_container(&entered, source_slot, fx);
                }
                self.on_enter_container(&entered, fx);
            }
            item = entered;
        }

        let mut source_owner = 0;
        if let Some(s) = src {
            // `sourceContainer.Items.Remove(item)` already happened in the caller.
            s.update_free_slot_count();
            source_owner = s.owner_id;
            if s.slot_type != slot_type::MAIL {
                source_tasks.push(ItemTask::ItemRemoveSlot {
                    item_id,
                    slot_type: source_slot_type,
                    slot: source_slot,
                });
            }
            if can_add_to_same_slot {
                // The merged item object is gone from every container but stays in the C#
                // `_allItems` (its DB row is never deleted); `ItemManager` keeps it as a loose
                // item for the same effect.
                fx.push(ItemFx::Orphaned(Box::new(item.clone())));
            }
        }

        let has_tasks = !item_tasks.is_empty() || !source_tasks.is_empty();
        // We use Invalid when doing internals, don't send to client
        if task != task_type::INVALID {
            if !item_tasks.is_empty() {
                fx.tasks(self.owner_id, task, item_tasks);
            }
            if !source_tasks.is_empty() {
                fx.tasks(source_owner, task, source_tasks);
            }
        }

        self.apply_bind_rules_task(task, data, fx);

        // Only trigger when moving between containers except for this being move to Mail.
        // `item.SlotType` is read after it was overwritten (unless the item was merged).
        let item_slot_type_now = if can_add_to_same_slot {
            source_slot_type
        } else {
            self.slot_type
        };
        let same_container = src_slot_type.is_some() && !from_other;
        if !same_container && self.slot_type != slot_type::MAIL {
            fx.push(ItemFx::Acquired {
                owner: self.owner_id,
                template_id,
                count: final_count,
            });
        } else if item_slot_type_now == slot_type::MAIL && self.slot_type != slot_type::MAIL {
            // Got attachment from Mail
            fx.push(ItemFx::Acquired {
                owner: self.owner_id,
                template_id,
                count: final_count,
            });
        } else if item_slot_type_now != slot_type::MAIL && self.slot_type == slot_type::MAIL {
            fx.push(ItemFx::Consumed {
                owner: self.owner_id,
                template_id,
                count: final_count,
            });
        }
        Ok(has_tasks)
    }

    /// `EquipmentContainer.OnEnterContainer`.
    fn on_enter_container(&self, item: &Item, fx: &mut ItemFxList) {
        if self.is_equipment() && self.has_parent_unit() {
            fx.push(ItemFx::EquipEnter {
                owner: self.owner_id,
                mate_id: self.mate_id,
                slot: item.slot as u8,
                item: Box::new(item.clone()),
            });
        }
    }

    /// `EquipmentContainer.OnLeaveContainer`.
    fn on_leave_container(&self, item: &Item, previous_slot: u8, fx: &mut ItemFxList) {
        if self.is_equipment() && self.has_parent_unit() {
            fx.push(ItemFx::EquipLeave {
                owner: self.owner_id,
                mate_id: self.mate_id,
                prev_slot: previous_slot,
                item: Box::new(item.clone()),
            });
        }
    }

    /// Public form of the `OnEnterContainer` / `OnLeaveContainer` events for callers that move
    /// item objects between containers by hand (`Inventory.SplitOrMoveItemEx`).
    pub fn notify_enter(&self, item: &Item, fx: &mut ItemFxList) {
        self.on_enter_container(item, fx);
    }

    pub fn notify_leave(&self, item: &Item, previous_slot: u8, fx: &mut ItemFxList) {
        self.on_leave_container(item, previous_slot, fx);
    }

    /// `EquipmentContainer` / `MateEquipmentContainer`.
    pub fn is_equipment(&self) -> bool {
        matches!(
            self.kind,
            ContainerKind::Equipment | ContainerKind::MateEquipment
        )
    }

    /// `ParentUnit != null`: a character's own equipment (attached by its `Inventory`) or a
    /// mate's gear.
    fn has_parent_unit(&self) -> bool {
        self.has_owner || self.mate_id > 0
    }

    /// `ApplyBindRules(taskType)` (the item task packet is sent even for
    /// `ItemTaskType.Invalid`, as in C#).
    pub fn apply_bind_rules_task(&mut self, task: u8, data: &ItemData, fx: &mut ItemFxList) {
        let mut tasks = Vec::new();
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
                tasks.push(ItemTask::update_bits(item));
            }
        }
        if !tasks.is_empty() {
            fx.tasks(self.owner_id, task, tasks);
        }
    }

    /// `ApplyBindRules` without an owner to notify (phase-2 callers).
    pub fn apply_bind_rules(&mut self, data: &ItemData) {
        let mut fx = ItemFxList::default();
        self.apply_bind_rules_task(task_type::INVALID, data, &mut fx);
    }

    /// `Items.Remove(item)` without any side effect (callers that move the object elsewhere).
    pub fn remove_item(&mut self, id: u64) -> Option<Item> {
        let idx = self.items.iter().position(|i| i.id == id)?;
        let item = self.items.remove(idx);
        self.update_free_slot_count();
        Some(item)
    }

    /// `RemoveItem(task, item, releaseIdAsWell)`. Returns the removed item (`None` when
    /// `CanDestroy` refused or the item is not here).
    pub fn remove_item_task(
        &mut self,
        task: u8,
        item_id: u64,
        release_id: bool,
        env: &mut ItemEnv<'_>,
    ) -> Option<Item> {
        let idx = self.items.iter().position(|i| i.id == item_id)?;
        if !env.rules.can_destroy(&self.items[idx]) {
            return None;
        }
        let old_slot = self.items[idx].slot as u8;
        // Handle items that can expire
        {
            let item = &mut self.items[idx];
            if item.expiration_online_minutes_left > 0.0
                || item.expiration_time > env.now
                || item.unpack_time > env.now
            {
                env.fx.push(expire_item(item).with_owner(self.owner_id));
            }
        }
        let item = self.items.remove(idx);
        if task != task_type::INVALID {
            env.fx
                .tasks(self.owner_id, task, vec![ItemTask::remove_slot(&item)]);
        }
        if release_id {
            env.fx.push(ItemFx::ReleaseId(item.id));
        }
        self.update_free_slot_count();
        env.fx.push(ItemFx::Consumed {
            owner: self.owner_id,
            template_id: item.template_id,
            count: item.count,
        });
        self.on_leave_container(&item, old_slot, &mut env.fx);
        Some(item)
    }

    /// `ConsumeItem(taskType, templateId, amountToConsume, preferredItem)`. Returns the number
    /// of units consumed.
    pub fn consume_item(
        &mut self,
        task: u8,
        template_id: u32,
        mut amount: i32,
        preferred_item: Option<u64>,
        env: &mut ItemEnv<'_>,
    ) -> i32 {
        let (mut found, _) = self.items_by_template(template_id, -1);
        if found.is_empty() {
            return 0; // Nothing found
        }
        let mut total = 0;
        let mut tasks = Vec::new();
        if let Some(pid) = preferred_item {
            // Preferred item template did not match / is not in this container
            match self.get_item_by_id(pid) {
                Some(p) if p.template_id == template_id => {}
                _ => return 0,
            }
            if amount > 0 {
                let Some(pos) = found.iter().position(|&i| i == pid) else {
                    return 0;
                };
                found.remove(pos);
                total += self.consume_one(task, pid, &mut amount, &mut tasks, env, true);
            }
        }
        if amount > 0 {
            found.sort_by_key(|id| self.get_item_by_id(*id).map_or(0, |i| i.slot));
            for id in found {
                total += self.consume_one(task, id, &mut amount, &mut tasks, env, false);
                if amount <= 0 {
                    break; // We are done with the list, leave the rest as is
                }
            }
        }
        if task != task_type::INVALID {
            env.fx.tasks(self.owner_id, task, tasks);
        }
        self.update_free_slot_count();
        total
    }

    fn consume_one(
        &mut self,
        task: u8,
        id: u64,
        amount: &mut i32,
        tasks: &mut Vec<ItemTask>,
        env: &mut ItemEnv<'_>,
        preferred: bool,
    ) -> i32 {
        let Some(item) = self.items.iter_mut().find(|i| i.id == id) else {
            return 0;
        };
        let to_remove = item.count.min(*amount);
        item.count -= to_remove;
        item.dirty = true;
        *amount -= to_remove;
        let template_id = item.template_id;
        if item.count > 0 {
            if !preferred {
                env.fx.push(ItemFx::Consumed {
                    owner: self.owner_id,
                    template_id,
                    count: to_remove,
                });
            }
            tasks.push(ItemTask::count_update(item, -to_remove));
        } else {
            self.remove_item_task(task, id, true, env); // Normally, this can never fail
        }
        if preferred {
            env.fx.push(ItemFx::Consumed {
                owner: self.owner_id,
                template_id,
                count: to_remove,
            });
        }
        to_remove
    }

    /// `AcquireDefaultItem(taskType, templateId, amountToAdd, gradeToAdd, crafterId)`.
    pub fn acquire_default_item(
        &mut self,
        task: u8,
        template_id: u32,
        amount: i32,
        grade: i32,
        crafter_id: u32,
        env: &mut ItemEnv<'_>,
    ) -> bool {
        self.acquire_default_item_ex(task, template_id, amount, grade, crafter_id, -1, env)
            .is_some_and(|r| r.has_tasks)
    }

    /// `AcquireDefaultItemEx`. `None` = `false` before anything was added (invalid template,
    /// not enough space, failed creation). The new / updated item ids are returned.
    #[allow(clippy::too_many_arguments)]
    pub fn acquire_default_item_ex(
        &mut self,
        task: u8,
        template_id: u32,
        mut amount: i32,
        mut grade: i32,
        crafter_id: u32,
        preferred_slot: i32,
        env: &mut ItemEnv<'_>,
    ) -> Option<AcquireResult> {
        let mut res = AcquireResult::default();
        if amount <= 0 {
            res.has_tasks = true;
            return Some(res);
        }
        let (current, units) = self.items_by_template(template_id, grade);
        let template = env.data.template(template_id)?; // Invalid item templateId
        let free = current.len() as i32 * template.max_count - units
            + self.free_slot_count * template.max_count;
        // Trying to add too many item units to this container ?
        if amount > free {
            return None;
        }
        if template.fixed_grade >= 0 && !template.gradable {
            grade = template.fixed_grade;
        }
        if grade == -1 {
            grade = template.fixed_grade;
        }
        if grade < 0 {
            grade = 0;
        }

        let mut tasks = Vec::new();
        // Never update in mail or auction containers
        if self.slot_type != slot_type::MAIL && self.slot_type != slot_type::AUCTION {
            let mut ids = current.clone();
            ids.sort_by_key(|id| self.get_item_by_id(*id).map_or(0, |i| i.slot));
            for id in ids {
                let Some(item) = self.items.iter_mut().find(|i| i.id == id) else {
                    continue;
                };
                let max = env
                    .data
                    .template(item.template_id)
                    .map_or(0, |t| t.max_count);
                let space = max - item.count;
                if space > 0 {
                    let add = space.min(amount);
                    item.count += add;
                    item.dirty = true;
                    amount -= add;
                    tasks.push(ItemTask::count_update(item, add));
                    res.updated.push(id);
                    // OnAcquiredItem(i, addAmount, true) -> DoItemsAcquiredEvents(item.Count)
                    env.fx.push(ItemFx::Acquired {
                        owner: self.owner_id,
                        template_id,
                        count: item.count,
                    });
                }
                if amount < 0 {
                    break;
                }
            }
        }

        let mut sync = Vec::new();
        while amount > 0 {
            let add = amount.min(template.max_count);
            let Some(mut item) = env.create(template_id, add, grade as u8) else {
                tracing::error!(
                    "Failed to add item with ID {template_id}, possible duplicate entries!"
                );
                return None;
            };
            // Add name if marked as crafter (single stack items only)
            if crafter_id > 0 && template.max_count == 1 {
                item.made_unit_id = crafter_id;
                item.world_id = env.default_world_template_id;
            }
            amount -= add;
            let pref = if matches!(template.kind, TemplateKind::Backpack { .. })
                && self.slot_type == slot_type::EQUIPMENT
            {
                equip_slot::BACKPACK
            } else {
                preferred_slot
            };
            // Timers
            if template.exp_abs_lifetime > 0 {
                sync.extend(set_item_expiration_time(
                    &mut item,
                    env.now + i64::from(template.exp_abs_lifetime) * 60,
                ));
            }
            if template.exp_online_lifetime > 0 {
                sync.extend(set_item_online_expiration_time(
                    &mut item,
                    f64::from(template.exp_online_lifetime),
                    env.now,
                ));
            }
            if let Some(d) = template.exp_date {
                if d > MIN_DATE_TIME {
                    sync.extend(set_item_expiration_time(&mut item, d));
                }
            }
            if let Some(e) = template.equip() {
                item.charge_count = e.charge_count;
                // `BindType.HasFlag(BindOnUnpack)`: a bit test on the plain enum (4, 5, 6, 7).
                if e.charge_lifetime > 0 && template.bind_type & 4 != 4 {
                    item.charge_start_time = env.now;
                }
            }
            let id = item.id;
            // Task set to invalid as we send our own packets inside this function
            match self.add_item(task_type::INVALID, item, pref, env.data, &mut env.fx) {
                Ok(true) => {
                    if let Some(i) = self.get_item_by_id(id) {
                        tasks.push(ItemTask::add(i));
                    }
                    res.new.push(id);
                }
                _ => {
                    // C#: throw new GameException("AcquireDefaultItem(); Unable to add new items")
                    tracing::error!("AcquireDefaultItem(); Unable to add new items");
                    env.fx.push(ItemFx::ReleaseId(id));
                    return None;
                }
            }
        }
        res.has_tasks = !tasks.is_empty();
        if task != task_type::INVALID {
            env.fx.tasks(self.owner_id, task, tasks);
        }
        self.update_free_slot_count();
        // Send item expire packets if needed
        for p in sync {
            env.fx.push(p.with_owner(self.owner_id));
        }
        Some(res)
    }

    /// `SpaceLeftForItem(templateId)`; `owner_money` is the owner's `Money` (only read for
    /// coins).
    pub fn space_left_for_template(
        &self,
        template_id: u32,
        data: &ItemData,
        owner_money: i64,
    ) -> i32 {
        let Some(t) = data.template(template_id) else {
            return 0; // Invalid item templateId
        };
        if template_id == crate::data::items::ITEM_COINS {
            return space_left_for_money(t.max_count, owner_money);
        }
        let (items, units) = self.items_by_template(template_id, -1);
        items.len() as i32 * t.max_count - units + self.free_slot_count * t.max_count
    }

    /// `SpaceLeftForItem(itemToAdd, out currentItems)` (template + grade).
    pub fn space_left_for_item(&self, item: &Item, data: &ItemData, owner_money: i64) -> i32 {
        let max = data.template(item.template_id).map_or(0, |t| t.max_count);
        if item.template_id == crate::data::items::ITEM_COINS {
            return space_left_for_money(max, owner_money);
        }
        let (items, units) = self.items_by_template(item.template_id, i32::from(item.grade));
        items.len() as i32 * max - units + self.free_slot_count * max
    }

    /// `Wipe()`: removes and releases every item.
    pub fn wipe(&mut self, env: &mut ItemEnv<'_>) {
        while let Some(id) = self.items.first().map(|i| i.id) {
            if self
                .remove_item_task(task_type::INVALID, id, true, env)
                .is_none()
            {
                // CanDestroy refused: the C# loops forever here; drop it from the list instead.
                self.items.remove(0);
            }
        }
        self.update_free_slot_count();
    }

    /// `ReNumberSlots(reverse)`.
    pub fn renumber_slots(&mut self, reverse: bool) {
        let n = self.items.len();
        let st = self.slot_type;
        for c in 0..n {
            let idx = if reverse { n - 1 - c } else { c };
            let i = &mut self.items[idx];
            i.slot_type = st;
            i.slot = c as i32;
            i.dirty = true;
        }
    }

    pub fn get_item_by_id_mut(&mut self, id: u64) -> Option<&mut Item> {
        self.items.iter_mut().find(|i| i.id == id)
    }

    pub fn get_item_by_slot_mut(&mut self, slot: i32) -> Option<&mut Item> {
        self.items.iter_mut().find(|i| i.slot == slot)
    }
}

/// `CalculateSpaceLeftForMoney`.
fn space_left_for_money(max_count: i32, money: i64) -> i32 {
    let money = money.max(0);
    let count = money.min(i64::from(max_count)) as i32;
    max_count - count
}

/// `DateTime.MinValue` as stored in [`UnixTime`] (`Helpers.UnixTime` clamps it to 0).
pub const MIN_DATE_TIME: UnixTime = 0;

/// `ItemManager.SetItemExpirationTime(item, newTime)` (the packet, owner still unset).
pub fn set_item_expiration_time(item: &mut Item, new_time: UnixTime) -> Option<ItemFx> {
    if item.expiration_time != new_time {
        item.expiration_time = new_time;
        item.dirty = true;
        return Some(ItemFx::Lifespan {
            owner: 0,
            added: new_time > item.create_time,
            item_id: item.id,
            template_id: item.template_id,
            expire_time: new_time,
        });
    }
    None
}

/// `ItemManager.SetItemOnlineExpirationTime(item, newMinutes)`.
pub fn set_item_online_expiration_time(
    item: &mut Item,
    new_minutes: f64,
    now: UnixTime,
) -> Option<ItemFx> {
    // `double.Epsilon` is the smallest subnormal, not machine epsilon.
    if (item.expiration_online_minutes_left - new_minutes).abs() > f64::from_bits(1) {
        let new_time = now + (new_minutes * 60.0) as i64;
        item.expiration_online_minutes_left = new_minutes;
        item.dirty = true;
        return Some(ItemFx::Lifespan {
            owner: 0,
            added: new_minutes >= 0.0,
            item_id: item.id,
            template_id: item.template_id,
            expire_time: new_time,
        });
    }
    None
}

/// `ItemManager.ExpireItemPacket(item)`.
pub fn expire_item(item: &mut Item) -> ItemFx {
    item.expiration_time = MIN_DATE_TIME;
    item.expiration_online_minutes_left = 0.0;
    item.dirty = true;
    ItemFx::Lifespan {
        owner: 0,
        added: false,
        item_id: item.id,
        template_id: item.template_id,
        expire_time: MIN_DATE_TIME,
    }
}

/// What `AcquireDefaultItemEx` reports (`newItemsList`, `updatedItemsList`, the return value).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AcquireResult {
    pub new: Vec<u64>,
    pub updated: Vec<u64>,
    pub has_tasks: bool,
}

/// The virtual item behaviour container operations call (`Item.CanDestroy()`).
pub trait ItemRules: Send + Sync {
    /// `Item.CanDestroy()` (`SummonSlave` refuses while its slave is summoned).
    fn can_destroy(&self, _item: &Item) -> bool {
        true
    }
}

/// The base-class behaviour (every item can be destroyed).
#[derive(Debug, Default, Clone, Copy)]
pub struct DefaultItemRules;

impl ItemRules for DefaultItemRules {}

/// What container operations need besides the containers themselves: templates, item creation
/// (`ItemManager.Create` with a new id), the clock, `Item.CanDestroy` and the effect list.
pub struct ItemEnv<'a> {
    pub data: &'a ItemData,
    pub items: &'a ItemManager,
    pub item_ids: &'a IdManager,
    /// `AppConfiguration.Id` (`Item.WorldId` of new items).
    pub world_id: u8,
    /// `WorldManager.DefaultWorldTemplateId` (crafted items).
    pub default_world_template_id: u8,
    /// `DateTime.UtcNow` (unix seconds).
    pub now: UnixTime,
    /// `Item.CanDestroy()` and friends.
    pub rules: &'a dyn ItemRules,
    pub fx: ItemFxList,
}

impl std::fmt::Debug for ItemEnv<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ItemEnv")
            .field("now", &self.now)
            .field("fx", &self.fx.len())
            .finish_non_exhaustive()
    }
}

impl ItemEnv<'_> {
    /// `ItemManager.Instance.Create(templateId, count, grade)`.
    pub fn create(&self, template_id: u32, count: i32, grade: u8) -> Option<Item> {
        self.items.create(
            template_id,
            count,
            grade,
            self.world_id,
            self.data,
            self.item_ids,
            self.now,
        )
    }

    /// Takes the recorded effects (to be flushed once every lock is released).
    pub fn take_fx(&mut self) -> ItemFxList {
        std::mem::take(&mut self.fx)
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
