//! Port of `Models/Game/Char/Inventory` (item moves, consumes, expands, coffers) and the money /
//! point / labor members of `Character` (`ChangeMoney`, `AddMoney`, `SubtractMoney`,
//! `ChangeGamePoints`, `ChangeLabor`, `SendErrorMessage`, `ItemUse`).
//!
//! Every item operation runs inside an [`ItemTxn`]: it takes the economy's operation lock (so
//! the multi-step C# operations cannot interleave), collects the effects in its [`ItemEnv`]
//! and flushes them through [`super::fx::flush`] when it is dropped, after the lock is
//! released. Lock order inside a transaction: character data (one at a time, short) and
//! container locks (leaf; two at most through [`lock_pair`]). Never start a transaction while
//! holding a character or container lock.

use std::collections::HashMap;
use std::sync::{Arc, MutexGuard};

use chrono::Utc;

use crate::data::items::{TemplateKind, EQUIPMENT_ITEM_SLOT_TYPE_BACKPACK};
use crate::data::ItemData;
use crate::models::container::{
    equip_slot, slot_type, ContainerRef, ItemContainer, ItemEnv, ItemRules,
};
use crate::models::item::{Item, ItemClass};
use crate::models::item_task::{task_type, ItemFxList, ItemTask};
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

use super::error_message;
use super::item_packets::{
    SCCharacterLaborPowerChangedPacket, SCGamePointChangedPacket, SCInvenExpandedPacket,
};

/// `EquipmentItemSlotType` values the two-handed checks use.
mod gear_slot_type {
    pub const TWO_HANDED: u32 = 16;
}

/// `GamePointKind`.
pub mod game_point_kind {
    pub const HONOR: u8 = 0;
    pub const VOCATION: u8 = 1;
}

impl ItemRules for GameServer {
    /// `SummonSlave.CanDestroy()` through the summon hooks; every other class can be destroyed.
    fn can_destroy(&self, item: &Item) -> bool {
        if item.class != ItemClass::SummonSlave {
            return true;
        }
        match self.systems.economy.hooks.summons.get() {
            Some(h) => h.can_destroy_summon_slave(self, item),
            None => true,
        }
    }
}

/// An item operation in progress (see the module docs).
pub struct ItemTxn<'a> {
    pub server: &'a GameServer,
    guard: Option<MutexGuard<'a, ()>>,
    pub env: ItemEnv<'a>,
}

impl<'a> ItemTxn<'a> {
    /// Starts an operation: takes the economy's operation lock.
    pub fn begin(server: &'a GameServer) -> Self {
        let guard = server.systems.economy.ops_lock();
        Self {
            server,
            guard: Some(guard),
            env: server_env(server),
        }
    }

    /// An operation without the operation lock, for callers that already hold a character
    /// lock (the quest "locked hooks"): starting a normal transaction there could deadlock with
    /// an operation that holds the lock and waits for that character. Hand the effects to
    /// [`ItemTxn::into_fx`] and flush them once the character lock is released.
    pub fn unlocked(server: &'a GameServer) -> Self {
        Self {
            server,
            guard: None,
            env: server_env(server),
        }
    }

    /// Ends the operation without flushing: the recorded effects are returned (flush them with
    /// [`super::fx::flush`] once every lock is released).
    pub fn into_fx(mut self) -> ItemFxList {
        self.env.take_fx()
    }

    pub fn data(&self) -> &'a ItemData {
        &self.server.game_data.items
    }

    /// Queue for the character packets sent during this operation.
    pub fn out(&mut self) -> Out<'_> {
        Out::Queue(&mut self.env.fx)
    }

    /// Ends the operation now (same as dropping it).
    pub fn commit(self) {}
}

impl Drop for ItemTxn<'_> {
    fn drop(&mut self) {
        let fx = self.env.take_fx();
        self.guard.take();
        super::fx::flush(self.server, fx);
    }
}

/// An [`ItemEnv`] for `server` (without the operation lock; tests and loaders).
pub fn server_env(server: &GameServer) -> ItemEnv<'_> {
    ItemEnv {
        data: &server.game_data.items,
        items: &server.items,
        item_ids: &server.ids.item,
        world_id: server.config.id,
        default_world_template_id: default_world_template_id(server),
        now: Utc::now().timestamp(),
        rules: server,
        fx: ItemFxList::default(),
    }
}

/// `WorldManager.DefaultWorldTemplateId` (the first loaded world, `main_world` = 0).
pub fn default_world_template_id(server: &GameServer) -> u8 {
    server
        .game_data
        .worlds
        .get_world_template_by_name("main_world")
        .map_or(0, |w| w.id as u8)
}

/// Locks two containers (once when they are the same), in address order.
pub fn lock_pair<'a>(
    a: &'a ContainerRef,
    b: &'a ContainerRef,
) -> (
    MutexGuard<'a, ItemContainer>,
    Option<MutexGuard<'a, ItemContainer>>,
) {
    if Arc::ptr_eq(a, b) {
        return (a.lock().expect("container poisoned"), None);
    }
    if Arc::as_ptr(a) < Arc::as_ptr(b) {
        let ga = a.lock().expect("container poisoned");
        let gb = b.lock().expect("container poisoned");
        (ga, Some(gb))
    } else {
        let gb = b.lock().expect("container poisoned");
        let ga = a.lock().expect("container poisoned");
        (ga, Some(gb))
    }
}

/// The player's `Inventory._itemContainers` (handles copied out of the character lock).
#[derive(Debug, Clone)]
pub struct Inv {
    pub character_id: u32,
    pub containers: Vec<(u8, ContainerRef)>,
}

impl Inv {
    pub fn of(player: &PlayerRef) -> Self {
        let c = player.data();
        Self {
            character_id: c.id,
            containers: c.inventory.containers.clone(),
        }
    }

    /// `_itemContainers.GetValueOrDefault(type)`.
    pub fn get(&self, st: u8) -> Option<ContainerRef> {
        self.containers
            .iter()
            .find(|(t, _)| *t == st)
            .map(|(_, c)| c.clone())
    }

    pub fn equipment(&self) -> ContainerRef {
        self.get(slot_type::EQUIPMENT).expect("equipment container")
    }

    pub fn bag(&self) -> ContainerRef {
        self.get(slot_type::INVENTORY).expect("bag container")
    }

    pub fn warehouse(&self) -> ContainerRef {
        self.get(slot_type::BANK).expect("bank container")
    }

    /// `GetItemById` (Equipment, Inventory and Bank only).
    pub fn item_by_id(&self, id: u64) -> Option<Item> {
        for (st, c) in &self.containers {
            if matches!(
                *st,
                slot_type::EQUIPMENT | slot_type::INVENTORY | slot_type::BANK
            ) {
                if let Some(i) = c.lock().expect("container poisoned").get_item_by_id(id) {
                    return Some(i.clone());
                }
            }
        }
        None
    }

    /// The container (Equipment / Inventory / Bank) that holds `id`.
    pub fn holder_of(&self, id: u64) -> Option<ContainerRef> {
        for (st, c) in &self.containers {
            if matches!(
                *st,
                slot_type::EQUIPMENT | slot_type::INVENTORY | slot_type::BANK
            ) && c
                .lock()
                .expect("container poisoned")
                .get_item_by_id(id)
                .is_some()
            {
                return Some(c.clone());
            }
        }
        None
    }

    /// Any of the player's containers that holds `id` (`item._holdingContainer`).
    pub fn holder_of_any(&self, id: u64) -> Option<ContainerRef> {
        self.containers
            .iter()
            .find(|(_, c)| {
                c.lock()
                    .expect("container poisoned")
                    .get_item_by_id(id)
                    .is_some()
            })
            .map(|(_, c)| c.clone())
    }

    /// `GetEquippedBySlot(slot)`.
    pub fn equipped(&self, slot: i32) -> Option<Item> {
        self.equipment()
            .lock()
            .expect("container poisoned")
            .get_item_by_slot(slot)
            .cloned()
    }

    /// `GetItem(type, slot)` (Equipment / Inventory / Bank; other types are TODO in C#).
    pub fn item_at(&self, st: u8, slot: u8) -> Option<Item> {
        match st {
            slot_type::EQUIPMENT | slot_type::INVENTORY | slot_type::BANK => self
                .get(st)?
                .lock()
                .expect("container poisoned")
                .get_item_by_slot(i32::from(slot))
                .cloned(),
            _ => None,
        }
    }

    /// `FreeSlotCount(type)`.
    pub fn free_slot_count(&self, st: u8) -> i32 {
        self.get(st).map_or(0, |c| {
            c.lock().expect("container poisoned").free_slot_count()
        })
    }

    /// `GetAllItemsByTemplate(inContainerTypes, templateId, gradeToCheck)`: (item ids, units).
    pub fn all_items_by_template(
        &self,
        types: Option<&[u8]>,
        template_id: u32,
        grade: i32,
    ) -> (Vec<u64>, i32) {
        let default = [slot_type::INVENTORY, slot_type::EQUIPMENT, slot_type::BANK];
        let types = match types {
            Some(t) if !t.is_empty() => t,
            _ => &default,
        };
        let mut ids = Vec::new();
        let mut units = 0;
        for st in types {
            if let Some(c) = self.get(*st) {
                let (i, u) = c
                    .lock()
                    .expect("container poisoned")
                    .items_by_template(template_id, grade);
                ids.extend(i);
                units += u;
            }
        }
        (ids, units)
    }

    /// `GetItemsCount(templateId, grade)` (Inventory + Equipment + Bank).
    pub fn items_count(&self, template_id: u32, grade: i32) -> i32 {
        self.all_items_by_template(None, template_id, grade).1
    }

    /// `GetItemsCount(slotType, templateId, grade)`.
    pub fn items_count_in(&self, st: u8, template_id: u32, grade: i32) -> i32 {
        self.all_items_by_template(Some(&[st]), template_id, grade)
            .1
    }

    /// `CheckItems(slotType, templateId, count)`.
    pub fn check_items(&self, st: u8, template_id: u32, count: i32) -> bool {
        let total = self.get(st).map_or(0, |c| {
            c.lock()
                .expect("container poisoned")
                .items_by_template(template_id, -1)
                .1
        });
        total >= count
    }
}

/// `Inventory.ConsumeItem(containersToCheck, taskType, templateId, amount, preferredItem)`.
pub fn consume_item(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    containers: Option<&[u8]>,
    task: u8,
    template_id: u32,
    mut amount: i32,
    preferred: Option<u64>,
) -> i32 {
    let default = [slot_type::INVENTORY, slot_type::EQUIPMENT, slot_type::BANK];
    let list = match containers {
        Some(c) if !c.is_empty() => c,
        _ => &default,
    };
    let mut res = 0;
    for st in list {
        if let Some(c) = inv.get(*st) {
            let used = c.lock().expect("container poisoned").consume_item(
                task,
                template_id,
                amount,
                preferred,
                &mut txn.env,
            );
            res += used;
            amount -= used;
        }
    }
    res
}

/// `ItemManager.IsAutoEquipTradePack`.
pub fn is_auto_equip_trade_pack(data: &ItemData, template_id: u32) -> bool {
    data.template(template_id)
        .is_some_and(|t| matches!(t.kind, TemplateKind::Backpack { .. }) && t.bind_type & 3 != 3)
}

/// `Inventory.TryAddNewItem(taskType, itemId, itemCount, gradeToAdd, crafterId)`.
pub fn try_add_new_item(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    task: u8,
    template_id: u32,
    count: i32,
    grade: i32,
    crafter_id: u32,
) -> bool {
    if is_auto_equip_trade_pack(txn.data(), template_id) {
        return try_equip_new_backpack(txn, inv, task, template_id, count, grade, crafter_id);
    }
    inv.bag()
        .lock()
        .expect("container poisoned")
        .acquire_default_item(task, template_id, count, grade, crafter_id, &mut txn.env)
}

/// `Inventory.TryEquipNewBackPack`.
pub fn try_equip_new_backpack(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    task: u8,
    template_id: u32,
    count: i32,
    grade: i32,
    crafter_id: u32,
) -> bool {
    // Validate before taking off an existing glider: a backpack cannot be stacked or
    // acquired into multiple equipment slots.
    if count != 1
        || !txn
            .data()
            .template(template_id)
            .is_some_and(|t| matches!(t.kind, TemplateKind::Backpack { .. }) && t.max_count > 0)
    {
        return false;
    }
    // Remove player backpack
    if take_off_backpack(txn, inv, task, true) {
        // Put tradepack in their backpack slot
        return inv
            .equipment()
            .lock()
            .expect("container poisoned")
            .acquire_default_item(task, template_id, count, grade, crafter_id, &mut txn.env);
    }
    false
}

fn backpack_type_of(data: &ItemData, item: &Item) -> Option<u32> {
    match data.template(item.template_id).map(|t| &t.kind) {
        Some(TemplateKind::Backpack { backpack_type }) => Some(*backpack_type),
        _ => None,
    }
}

/// `Inventory.CanReplaceGliderInBackpackSlot`.
pub fn can_replace_glider_in_backpack_slot(data: &ItemData, inv: &Inv) -> bool {
    let Some(backpack) = inv.equipped(equip_slot::BACKPACK) else {
        return true; // Nothing equipped, so we're good
    };
    // Check if a glider is equipped, and if we have at least 1 free space
    backpack_type_of(data, &backpack) == Some(crate::data::items::backpack_type::GLIDER)
        && inv.free_slot_count(slot_type::INVENTORY) > 0
}

/// `Inventory.TakeoffBackpack(taskType, glidersOnly)`. `previous_backpack` receives
/// `PreviousBackPackItemId` when a glider was taken off for a trade pack.
pub fn take_off_backpack(txn: &mut ItemTxn<'_>, inv: &Inv, task: u8, gliders_only: bool) -> bool {
    let Some(backpack) = inv.equipped(equip_slot::BACKPACK) else {
        return true;
    };
    // Check glider if needed
    if gliders_only {
        if let Some(bt) = backpack_type_of(txn.data(), &backpack) {
            if bt != crate::data::items::backpack_type::GLIDER {
                return false;
            }
        }
    }
    // Move to first available slot
    let bag = inv.bag();
    let free_slot = {
        let g = bag.lock().expect("container poisoned");
        if g.free_slot_count() <= 0 {
            return false;
        }
        g.get_unused_slot(-1)
    };
    if !split_or_move_item(
        txn,
        inv,
        task,
        backpack.id,
        backpack.slot_type,
        backpack.slot as u8,
        0,
        slot_type::INVENTORY,
        free_slot as u8,
        0,
    ) {
        return false;
    }
    if gliders_only {
        txn.server
            .systems
            .economy
            .set_previous_backpack(inv.character_id, backpack.id);
    }
    true
}

/// `SplitOrMoveItem`'s `SwapAction`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(clippy::enum_variant_names)]
enum SwapAction {
    DoNothing,
    DoSwap,
    DoSplit,
    DoMerge,
    DoMoveAllToEmpty,
    DoEquipInEmptySlot,
}

/// `Inventory.SplitOrMoveItem(taskType, fromItemId, fromType, fromSlot, toItemId, toType,
/// toSlot, count)`.
#[allow(clippy::too_many_arguments)]
pub fn split_or_move_item(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    task: u8,
    from_item_id: u64,
    from_type: u8,
    from_slot: u8,
    to_item_id: u64,
    to_type: u8,
    to_slot: u8,
    count: i32,
) -> bool {
    let holder = |id: u64| {
        if id == 0 {
            return None;
        }
        inv.holder_of_any(id)
    };
    let from_holder = holder(from_item_id);
    if from_holder.is_none() && from_item_id != 0 {
        tracing::error!(
            "SplitOrMoveItem - ItemId {from_item_id} no longer exists, possibly a phantom item."
        );
        return false;
    }
    let to_holder = holder(to_item_id);
    if to_holder.is_none() && to_item_id != 0 {
        tracing::error!(
            "SplitOrMoveItem - ItemId {to_item_id} no longer exists, possibly a phantom item."
        );
        return false;
    }
    let src = from_holder.or_else(|| inv.get(from_type));
    let dst = to_holder.or_else(|| inv.get(to_type));
    split_or_move_item_ex(
        txn,
        inv,
        task,
        src,
        dst,
        from_item_id,
        from_type,
        from_slot,
        to_item_id,
        to_type,
        to_slot,
        count,
    )
}

/// Finds an item by id in one of two (locked) containers.
fn find_in<'b>(a: &'b ItemContainer, b: Option<&'b ItemContainer>, id: u64) -> Option<&'b Item> {
    if id == 0 {
        return None;
    }
    a.get_item_by_id(id)
        .or_else(|| b.and_then(|b| b.get_item_by_id(id)))
}

fn is_two_handed(data: &ItemData, item: Option<&Item>) -> bool {
    item.and_then(|i| data.template(i.template_id))
        .is_some_and(|t| matches!(&t.kind, TemplateKind::Weapon(e) if e.slot_type_id == gear_slot_type::TWO_HANDED))
}

/// `Inventory.SplitOrMoveItemEx`. Source / target items are looked up in the two containers
/// (the C# `GetItemByItemId` searches every item; an item that is in neither container is
/// treated as a phantom item here).
#[allow(clippy::too_many_arguments)]
pub fn split_or_move_item_ex(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    task: u8,
    src: Option<ContainerRef>,
    dst: Option<ContainerRef>,
    from_item_id: u64,
    from_type: u8,
    from_slot: u8,
    to_item_id: u64,
    to_type: u8,
    to_slot: u8,
    mut count: i32,
) -> bool {
    tracing::trace!(
        "SplitOrMoveItem({from_item_id} {from_type}:{from_slot} => {to_item_id} {to_type}:{to_slot} - {count})"
    );
    let data = txn.data();
    let (Some(src), Some(dst)) = (src, dst) else {
        // C#: NullReferenceException on the missing container.
        tracing::error!("SplitOrMoveItem - missing source or target container");
        return false;
    };

    // ---- Decide (read-only, under both container locks) ----
    let (action, from_item, target_item, src_type, dst_type) = {
        let (s, d) = lock_pair(&src, &dst);
        let dref = d.as_deref().unwrap_or(&s);
        let from_item = find_in(&s, d.as_deref(), from_item_id).cloned();
        if from_item.is_none() && from_item_id != 0 {
            tracing::error!(
                "SplitOrMoveItem - ItemId {from_item_id} no longer exists, possibly a phantom item."
            );
            return false;
        }
        let mut action = SwapAction::DoNothing;
        let mut target_item = find_in(&s, d.as_deref(), to_item_id).cloned();
        // If no count provided, and we have a source item, use that item's total count instead
        if count <= 0 {
            if let Some(f) = &from_item {
                count = f.count;
            }
        }
        if target_item.is_none() {
            target_item = dref.get_item_by_slot(i32::from(to_slot)).cloned();
        }
        // Check if containers can accept the items
        if let Some(f) = &from_item {
            if !dref.can_accept(f, data.template(f.template_id), i32::from(to_slot)) {
                tracing::error!(
                    "SplitOrMoveItem - fromItemId {from_item_id} is not welcome in this container {} ({}).",
                    dref.slot_type,
                    dref.container_id
                );
                return false;
            }
        }
        if let Some(t) = &target_item {
            if !s.can_accept(t, data.template(t.template_id), i32::from(from_slot)) {
                tracing::error!(
                    "SplitOrMoveItem - toItemId {to_item_id} is not welcome in this container {} ({}).",
                    s.slot_type,
                    s.container_id
                );
                return false;
            }
        }
        // Equipping into an empty slot: the client sends FROM empty equipment slot => TO item
        if from_item_id == 0
            && from_type == slot_type::EQUIPMENT
            && to_type != slot_type::EQUIPMENT
            && target_item.is_some()
        {
            action = SwapAction::DoEquipInEmptySlot;
        }
        // Equipping into an empty mount gear slot
        if from_item_id == 0
            && from_type == slot_type::EQUIPMENT_MATE
            && to_type != slot_type::EQUIPMENT_MATE
            && target_item.is_some()
        {
            action = SwapAction::DoEquipInEmptySlot;
        }
        let equip_empty = action == SwapAction::DoEquipInEmptySlot;
        if !equip_empty && from_item.is_none() {
            tracing::error!("SplitOrMoveItem didn't provide a source itemId");
            return false;
        }
        if !equip_empty && s.slot_type != from_type {
            tracing::error!(
                "SplitOrMoveItem Source Item Container did not match what the client asked"
            );
            return false;
        }
        if !equip_empty && from_item.as_ref().map(|f| f.slot) != Some(i32::from(from_slot)) {
            tracing::error!("SplitOrMoveItem Source Item slot did not match what the client asked");
            return false;
        }
        if !equip_empty && from_item.as_ref().is_some_and(|f| count > f.count) {
            tracing::error!(
                "SplitOrMoveItem Source Item has less item count than is requested to be moved"
            );
            return false;
        }
        if let Some(t) = &target_item {
            if t.slot_type != to_type {
                tracing::error!("SplitOrMoveItem Target Item Type does not match");
                return false;
            }
            if t.slot != i32::from(to_slot) {
                tracing::error!("SplitOrMoveItem Target Item Slot does not match");
                return false;
            }
            if let Some(f) = &from_item {
                let max = data.template(f.template_id).map_or(0, |t| t.max_count);
                if !equip_empty
                    && t.template_id == f.template_id
                    && t.count + count > max
                    && max > 1
                {
                    tracing::error!(
                        "SplitOrMoveItem Target Item stack does not have enough room to take source"
                    );
                    return false;
                }
            }
        }
        if !equip_empty {
            let f = from_item.as_ref().expect("checked above");
            action = match &target_item {
                None if f.count > count => SwapAction::DoSplit,
                None if f.count == count => SwapAction::DoMoveAllToEmpty,
                Some(t)
                    if t.template_id == f.template_id
                        && data.template(t.template_id).map_or(0, |x| x.max_count) > 1 =>
                {
                    SwapAction::DoMerge
                }
                _ => SwapAction::DoSwap,
            };
            // `fromItem.Count < count` with no target item falls through to doSwap in C#
            // (impossible after the count check above).
        }
        (action, from_item, target_item, s.slot_type, dref.slot_type)
    };

    // ---- Two-handed weapon handling ----
    let mut un_equip_offhand = false;
    let mut un_equip_mainhand = false;
    let mut main_hand = None;
    let mut off_hand = None;
    if action == SwapAction::DoSwap || action == SwapAction::DoEquipInEmptySlot {
        main_hand = inv.equipped(equip_slot::MAINHAND);
        off_hand = inv.equipped(equip_slot::OFFHAND);
        let is_from_2h = is_two_handed(data, from_item.as_ref());
        let is_to_2h = is_two_handed(data, target_item.as_ref());
        let is_main_2h = is_two_handed(data, main_hand.as_ref());
        if is_to_2h
            && src_type == slot_type::EQUIPMENT
            && i32::from(from_slot) == equip_slot::MAINHAND
        {
            un_equip_offhand = true;
        }
        if is_main_2h
            && src_type == slot_type::EQUIPMENT
            && i32::from(from_slot) == equip_slot::OFFHAND
        {
            un_equip_mainhand = true;
        }
        if is_from_2h
            && dst_type == slot_type::EQUIPMENT
            && i32::from(to_slot) == equip_slot::MAINHAND
        {
            un_equip_offhand = true;
        }
        if is_main_2h
            && dst_type == slot_type::EQUIPMENT
            && i32::from(to_slot) == equip_slot::OFFHAND
        {
            un_equip_mainhand = true;
        }
    }
    let bag = inv.bag();
    let equipment = inv.equipment();
    if un_equip_offhand {
        if let Some(off) = &off_hand {
            if bag.lock().expect("container poisoned").free_slot_count() < 1 {
                return false;
            }
            let (mut b, e) = lock_pair(&bag, &equipment);
            let Some(mut e) = e else { return false };
            if !b.move_item_from(&mut e, task, off.id, -1, data, &mut txn.env.fx) {
                return false;
            }
        }
    }
    if un_equip_mainhand {
        if bag.lock().expect("container poisoned").free_slot_count() < 1 {
            return false;
        }
        // C#: `Bag.AddOrMoveExistingItem(taskType, mainHandWeapon)` (mainHandWeapon is non-null
        // here, isMain2H implies it).
        let Some(main) = &main_hand else { return false };
        let (mut b, e) = lock_pair(&bag, &equipment);
        let Some(mut e) = e else { return false };
        if !b.move_item_from(&mut e, task, main.id, -1, data, &mut txn.env.fx) {
            return false;
        }
    }

    // ---- Execute ----
    let mut item_tasks = Vec::new();
    let same = Arc::ptr_eq(&src, &dst);
    let (from_after, target_after) = {
        let (mut s, mut d) = lock_pair(&src, &dst);
        match action {
            SwapAction::DoEquipInEmptySlot => {
                let t = target_item.as_ref().expect("equip target");
                item_tasks.push(ItemTask::ItemMove {
                    from_slot_type: from_type,
                    from_slot,
                    from_item_id,
                    to_slot_type: to_type,
                    to_slot,
                    to_item_id,
                });
                let src_type = s.slot_type;
                if let Some(d) = d.as_deref_mut() {
                    if let Some(mut it) = d.remove_item(t.id) {
                        it.slot_type = src_type;
                        it.slot = i32::from(from_slot);
                        it.dirty = true;
                        s.items.push(it);
                        s.update_free_slot_count();
                    }
                } else if let Some(it) = s.get_item_by_id_mut(t.id) {
                    it.slot_type = src_type;
                    it.slot = i32::from(from_slot);
                    it.dirty = true;
                }
            }
            SwapAction::DoSplit => {
                let f = from_item.as_ref().expect("split source");
                if let Some(fi) = s.get_item_by_id_mut(f.id) {
                    fi.count -= count;
                    fi.dirty = true;
                    item_tasks.push(ItemTask::count_update(fi, -count));
                }
                match txn.env.create(f.template_id, count, f.grade) {
                    Some(mut ni) => {
                        ni.slot_type = to_type;
                        ni.slot = i32::from(to_slot);
                        let target = d.as_deref_mut().unwrap_or(&mut s);
                        item_tasks.push(ItemTask::add(&ni));
                        target.items.push(ni);
                        target.update_free_slot_count();
                    }
                    None => {
                        // C#: NullReferenceException on `ni.SlotType`.
                        tracing::error!("SplitOrMoveItem - failed to create the split item");
                    }
                }
                s.update_free_slot_count();
            }
            SwapAction::DoMoveAllToEmpty => {
                let f = from_item.as_ref().expect("move source");
                item_tasks.push(ItemTask::ItemMove {
                    from_slot_type: from_type,
                    from_slot,
                    from_item_id,
                    to_slot_type: to_type,
                    to_slot,
                    to_item_id,
                });
                if let Some(d) = d.as_deref_mut() {
                    if let Some(mut it) = s.remove_item(f.id) {
                        it.slot_type = d.slot_type;
                        it.slot = i32::from(to_slot);
                        it.dirty = true;
                        d.items.push(it);
                        d.update_free_slot_count();
                    }
                } else if let Some(it) = s.get_item_by_id_mut(f.id) {
                    it.slot = i32::from(to_slot);
                    it.dirty = true;
                }
            }
            SwapAction::DoMerge => {
                let f = from_item.as_ref().expect("merge source");
                let t = target_item.as_ref().expect("merge target");
                let max = data.template(t.template_id).map_or(0, |x| x.max_count);
                let to_add = count.min(max - t.count);
                if to_add < count {
                    tracing::trace!(
                        "SplitOrMoveItem supplied more than target can take, changed {count} to {to_add}"
                    );
                }
                let target_c = d.as_deref_mut().unwrap_or(&mut s);
                if let Some(ti) = target_c.get_item_by_id_mut(t.id) {
                    ti.count += to_add;
                    ti.dirty = true;
                }
                let mut from_left = 0;
                if let Some(fi) = s.get_item_by_id_mut(f.id) {
                    fi.count -= to_add;
                    fi.dirty = true;
                    from_left = fi.count;
                    if from_left > 0 {
                        item_tasks.push(ItemTask::count_update(fi, -to_add));
                    } else {
                        item_tasks.push(ItemTask::remove(fi));
                    }
                }
                if from_left <= 0 {
                    s.remove_item_task(task_type::INVALID, f.id, true, &mut txn.env);
                }
                let target_c = d.as_deref().unwrap_or(&s);
                if let Some(ti) = target_c.get_item_by_id(t.id) {
                    item_tasks.push(ItemTask::count_update(ti, to_add));
                }
            }
            SwapAction::DoSwap => {
                let f = from_item.as_ref().expect("swap source");
                let t = target_item.as_ref().expect("swap target");
                item_tasks.push(ItemTask::ItemMove {
                    from_slot_type: from_type,
                    from_slot,
                    from_item_id,
                    to_slot_type: to_type,
                    to_slot,
                    to_item_id,
                });
                let s_type = s.slot_type;
                match d.as_deref_mut() {
                    Some(d) => {
                        let d_type = d.slot_type;
                        if let Some(mut fi) = s.remove_item(f.id) {
                            fi.slot_type = d_type;
                            fi.slot = i32::from(to_slot);
                            fi.dirty = true;
                            d.items.push(fi);
                            d.update_free_slot_count();
                        }
                        if let Some(mut ti) = d.remove_item(t.id) {
                            ti.slot_type = s_type;
                            ti.slot = i32::from(from_slot);
                            ti.dirty = true;
                            s.items.push(ti);
                            s.update_free_slot_count();
                        }
                    }
                    None => {
                        if let Some(fi) = s.get_item_by_id_mut(f.id) {
                            fi.slot = i32::from(to_slot);
                            fi.dirty = true;
                        }
                        if let Some(ti) = s.get_item_by_id_mut(t.id) {
                            ti.slot = i32::from(from_slot);
                            ti.dirty = true;
                        }
                    }
                }
            }
            SwapAction::DoNothing => {
                // Should be impossible to get here
                tracing::error!("SplitOrMoveItem swap action not implemented {action:?}");
            }
        }

        // Force-assign item owners for safety (the C# gives fromItem the *source* owner and the
        // target item the *target* owner, wherever they ended up).
        let s_owner = s.owner_id;
        let d_owner = d.as_deref().map_or(s_owner, |d| d.owner_id);
        let mut from_after = None;
        let mut target_after = None;
        if let Some(f) = &from_item {
            let holder = if s.get_item_by_id(f.id).is_some() {
                Some(&mut *s)
            } else {
                d.as_deref_mut()
            };
            if let Some(i) = holder.and_then(|h| h.get_item_by_id_mut(f.id)) {
                i.owner_id = u64::from(s_owner);
                i.dirty = true;
                from_after = Some(i.clone());
            } else {
                // Merged away: the event still carries the (now empty) item.
                from_after = Some(f.clone());
            }
        }
        if let Some(t) = &target_item {
            let holder = if s.get_item_by_id(t.id).is_some() {
                Some(&mut *s)
            } else {
                d.as_deref_mut()
            };
            if let Some(i) = holder.and_then(|h| h.get_item_by_id_mut(t.id)) {
                i.owner_id = u64::from(d_owner);
                i.dirty = true;
                target_after = Some(i.clone());
            }
        }
        (from_after, target_after)
    };

    // Send Item manipulation packet
    if task != task_type::INVALID && !item_tasks.is_empty() {
        txn.env.fx.tasks(inv.character_id, task, item_tasks.clone());
    }

    // Send ItemContainer events
    {
        let (mut s, mut d) = lock_pair(&src, &dst);
        if !same {
            let dd = d.as_deref().expect("different containers");
            if let Some(f) = &from_after {
                s.notify_leave(f, from_slot, &mut txn.env.fx);
                dd.notify_enter(f, &mut txn.env.fx);
            }
            if let Some(t) = &target_after {
                // C# passes fromSlot as the previous slot here too.
                dd.notify_leave(t, from_slot, &mut txn.env.fx);
                s.notify_enter(t, &mut txn.env.fx);
            }
        }
        s.apply_bind_rules_task(task, data, &mut txn.env.fx);
        if let Some(d) = d.as_deref_mut() {
            d.apply_bind_rules_task(task, data, &mut txn.env.fx);
        }
    }
    !item_tasks.is_empty()
}

/// `Inventory.SwapCofferItems` / `SplitCofferItems` (`count` = 0 for a swap).
#[allow(clippy::too_many_arguments)]
pub fn coffer_items(
    txn: &mut ItemTxn<'_>,
    inv: &Inv,
    task: u8,
    count: i32,
    from_item_id: u64,
    to_item_id: u64,
    from_type: u8,
    from_slot: u8,
    to_type: u8,
    to_slot: u8,
    db_id: u64,
) -> bool {
    let server = txn.server;
    let Some(player) = server.world.character_by_id(inv.character_id) else {
        return false;
    };
    let Some(world) = server.world.world_of(&WorldObject::from(&player)) else {
        return false;
    };
    let Some(doodad) = world.all_doodads().into_iter().find(|d| {
        d.data()
            .coffer
            .as_ref()
            .is_some_and(|c| c.container_id == db_id && c.opened_by == Some(inv.character_id))
    }) else {
        return false;
    };
    if !crate::systems::npcs::doodad::coffer::allowed_to_interact(server, &doodad, &player)
        || world
            .world_pos_rot(&WorldObject::from(&player))
            .distance_to(&world.world_pos_rot(&WorldObject::from(&doodad)), false)
            > 5.0
    {
        return false;
    }
    let coffer = txn.server.items.container_by_id(db_id);
    let pick = |st: u8| -> Option<ContainerRef> {
        if st == slot_type::TRADE {
            coffer.clone()
        } else {
            inv.get(st)
        }
    };
    let (Some(src), Some(dst)) = (pick(from_type), pick(to_type)) else {
        tracing::error!("SwapCofferItems, not all of the targetted containers exist");
        return false;
    };
    split_or_move_item_ex(
        txn,
        inv,
        task,
        Some(src),
        Some(dst),
        from_item_id,
        from_type,
        from_slot,
        to_item_id,
        to_type,
        to_slot,
        count,
    )
}

/// `Inventory.ExpandSlot(slotType)`.
pub fn expand_slot(txn: &mut ItemTxn<'_>, player: &PlayerRef, inv: &Inv, st: u8) {
    let server = txn.server;
    let is_bank = st == slot_type::BANK;
    let (num_bank, num_inv, money) = {
        let c = player.data();
        (c.num_bank_slots, c.num_inventory_slots, c.money)
    };
    let step = (if is_bank {
        i32::from(num_bank)
    } else {
        i32::from(num_inv)
    } - 50)
        / 10;
    let Some(expands) = server.game_data.systems.economy.expands(step) else {
        // C#: `_expands[step]` throws KeyNotFoundException (the packet handler logs it).
        tracing::error!("ExpandSlot: no bag_expands for step {step}");
        return;
    };
    let Some(expand) = expands.iter().find(|e| e.is_bank == is_bank).copied() else {
        return;
    };
    if expand.price != 0 && money < i64::from(expand.price) {
        tracing::warn!("No Money for expand!");
        return;
    }
    if expand.item_id != 0
        && expand.item_count != 0
        && !inv.check_items(slot_type::INVENTORY, expand.item_id, expand.item_count)
    {
        tracing::warn!("Item or Count not fount.");
        return;
    }
    if expand.price != 0 {
        // `tasks.Add(new MoneyChange(-expand.Price))` is never sent in C#.
        player.data().money -= i64::from(expand.price);
    }
    if expand.item_id != 0 && expand.item_count != 0 {
        inv.bag().lock().expect("container poisoned").consume_item(
            if is_bank {
                task_type::EXPAND_BANK
            } else {
                task_type::EXPAND_BAG
            },
            expand.item_id,
            expand.item_count,
            None,
            &mut txn.env,
        );
    }
    let new_size = 50 + 10 * (1 + step);
    {
        let mut c = player.data();
        if is_bank {
            c.num_bank_slots = new_size as i16;
        } else {
            c.num_inventory_slots = new_size as u8;
        }
    }
    let target = if is_bank { inv.warehouse() } else { inv.bag() };
    target
        .lock()
        .expect("container poisoned")
        .set_container_size(new_size);
    txn.env.fx.send(
        player.character_id,
        SCInvenExpandedPacket {
            slot_type: if is_bank {
                slot_type::BANK
            } else {
                slot_type::INVENTORY
            },
            num_slots: new_size as u8,
        },
    );
}

/// `Inventory.OnItemManuallyDestroyed(item, count)`.
pub fn on_item_manually_destroyed(server: &GameServer, player: &PlayerRef, item: &Item) {
    // item?.OnManuallyDestroyingItem()
    if item.class == ItemClass::SummonSlave {
        if let Some(h) = server.systems.economy.hooks.summons.get() {
            h.on_slave_item_destroyed(server, item);
        }
    }
    let loot_quest_id = server
        .game_data
        .items
        .template(item.template_id)
        .map_or(0, |t| t.loot_quest_id);
    if loot_quest_id > 0 {
        if let Some(h) = server.systems.economy.hooks.quests.get() {
            h.on_quest_item_manually_destroyed(server, player, item, loot_quest_id);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Character members
// ---------------------------------------------------------------------------------------------

/// `Character.SendErrorMessage(errorMsgType, type, isNotify)`.
pub fn send_error_message(player: &PlayerRef, error: i16, kind: u32, is_notify: bool) {
    player.send(SCErrorMsgPacket::new(error, kind, is_notify));
}

/// `SendErrorMessage` through `out`.
pub fn send_error_message_to(out: &mut Out<'_>, player: &PlayerRef, error: i16) {
    out.send(player, SCErrorMsgPacket::new(error, 0, true));
}

/// Where a character's own packets go: straight to its connection, or queued with the item
/// effects of the running [`ItemTxn`] (the C# sends everything inline, so the order of item
/// task packets and these packets must be kept).
pub enum Out<'a> {
    Direct,
    Queue(&'a mut ItemFxList),
}

impl Out<'_> {
    pub fn send(
        &mut self,
        player: &PlayerRef,
        packet: impl crate::net::packet::ServerPacket + 'static,
    ) {
        match self {
            Out::Direct => player.send(packet),
            Out::Queue(fx) => fx.push(crate::models::item_task::ItemFx::Packet {
                owner: player.character_id,
                packet: Box::new(packet),
            }),
        }
    }
}

/// `Character.ChangeMoney(typeFrom, typeTo, amount, itemTaskType)`. The packet is sent even
/// when nothing changed (`SlotType.None` -> `None`), as in C#.
pub fn change_money(
    out: &mut Out<'_>,
    player: &PlayerRef,
    from: u8,
    to: u8,
    amount: i32,
    task: u8,
) -> bool {
    let mut tasks = Vec::new();
    {
        let mut c = player.data();
        match from {
            slot_type::INVENTORY => {
                if i64::from(amount) > c.money {
                    drop(c);
                    send_error_message_to(out, player, error_message::NOT_ENOUGH_MONEY);
                    return false;
                }
                c.money -= i64::from(amount);
                tasks.push(ItemTask::MoneyChange(-amount));
            }
            slot_type::BANK => {
                if i64::from(amount) > c.money2 {
                    drop(c);
                    send_error_message_to(out, player, error_message::NOT_ENOUGH_MONEY);
                    return false;
                }
                c.money2 -= i64::from(amount);
                tasks.push(ItemTask::MoneyChangeBank(-amount));
            }
            _ => {}
        }
        match to {
            slot_type::INVENTORY => {
                c.money += i64::from(amount);
                tasks.push(ItemTask::MoneyChange(amount));
            }
            slot_type::BANK => {
                c.money2 += i64::from(amount);
                tasks.push(ItemTask::MoneyChangeBank(amount));
            }
            _ => {}
        }
    }
    out.send(
        player,
        super::item_packets::SCItemTaskSuccessPacket {
            action: task,
            tasks,
            force_remove: Vec::new(),
        },
    );
    true
}

/// `Character.ChangeMoney(moneyLocation, amount, itemTaskType)`.
pub fn change_money_at(
    out: &mut Out<'_>,
    player: &PlayerRef,
    location: u8,
    amount: i32,
    task: u8,
) -> bool {
    change_money(out, player, slot_type::NONE, location, amount, task)
}

/// `Character.AddMoney`.
pub fn add_money(
    out: &mut Out<'_>,
    player: &PlayerRef,
    location: u8,
    amount: i32,
    task: u8,
) -> bool {
    if amount < 0 {
        return false;
    }
    change_money(out, player, slot_type::NONE, location, amount, task)
}

/// `Character.SubtractMoney`.
pub fn subtract_money(
    out: &mut Out<'_>,
    player: &PlayerRef,
    location: u8,
    amount: i32,
    task: u8,
) -> bool {
    if amount < 0 {
        return false;
    }
    change_money(out, player, slot_type::NONE, location, -amount, task)
}

/// `Character.ChangeGamePoints(kind, change)`.
pub fn change_game_points(out: &mut Out<'_>, player: &PlayerRef, kind: u8, mut change: i32) {
    {
        let mut c = player.data();
        match kind {
            game_point_kind::HONOR => c.honor_point += change,
            game_point_kind::VOCATION => {
                use crate::models::unit::UnitAttribute;
                let now = crate::world::now_ms();
                let voc_add =
                    c.unit
                        .calculate_with_bonuses(0.0, UnitAttribute::LivingPointGain, now)
                        as f32;
                change = f64::from(change as f32 + voc_add).round_ties_even() as i32;
                let voc_mul =
                    c.unit
                        .calculate_with_bonuses(0.0, UnitAttribute::LivingPointGainMul, now)
                        as f32
                        + 100.0;
                change = f64::from(change as f32 * (voc_mul / 100.0)).round_ties_even() as i32;
                c.vocation_point += change;
            }
            _ => {
                tracing::error!("ChangeGamePoints - Unknown Game Point Type {kind}");
                return;
            }
        }
    }
    out.send(
        player,
        SCGamePointChangedPacket {
            kind,
            amount: change,
        },
    );
}

/// `Character.ChangeLabor(change, actabilityId)`.
pub fn change_labor(
    out: &mut Out<'_>,
    server: &GameServer,
    player: &PlayerRef,
    change: i16,
    actability_id: i32,
) {
    let mut actability_change = 0;
    let mut actability_step = 0u8;
    let mut exp_multiplier = 1f32;
    let level;
    {
        let mut c = player.data();
        level = c.unit.level;
        if actability_id > 0 {
            if let Some(a) = c
                .actability
                .actabilities
                .iter_mut()
                .find(|a| a.id == actability_id as u32)
            {
                // Get multiplier before adding points
                exp_multiplier = actability_exp_multiplier(a.step);
                actability_change =
                    (f64::from(change).abs() * server.config.world.actability_rate) as i32;
                actability_step = a.step;
                // CharacterActability.AddPoint
                let previous = a.point;
                a.point += actability_change;
                if let Some(t) = server.game_data.characters.expert_limit(a.step as usize) {
                    if a.point > t.up_limit {
                        a.point = t.up_limit;
                    }
                }
                actability_change = a.point - previous;
            } else {
                // C#: `Actabilities[(uint)actabilityId]` throws KeyNotFoundException.
                tracing::error!("ChangeLabor: unknown actability {actability_id}");
                return;
            }
        }
    }
    // Only grant xp if consuming labor
    if change < 0 {
        let mut params = HashMap::new();
        params.insert("labor_power", f64::from(-change));
        params.insert("pc_level", f64::from(level));
        let xp = server
            .game_data
            .formulas
            .get_formula_by_kind(crate::data::formulas::FormulaKind::ExpByLaborPower)
            .map_or(0.0, |f| f.evaluate(&params));
        let xp_to_add = (xp * f64::from(exp_multiplier)) as i32;
        if let Some(h) = server.systems.economy.hooks.progression.get() {
            h.add_exp(server, player, xp_to_add, true);
        }
    }
    player.data().labor_power += change;
    out.send(
        player,
        SCCharacterLaborPowerChangedPacket {
            amount: i32::from(change),
            action: actability_id,
            point: actability_change,
            step: actability_step,
        },
    );
}

/// `Actability.GetExpMultiplier()` (1.2 table: all 1.0).
pub fn actability_exp_multiplier(_step: u8) -> f32 {
    1.0
}

/// `Actability.GetLaborCostMultiplier()`.
pub fn actability_labor_cost_multiplier(step: u8) -> f32 {
    const T: [f32; 8] = [1.00, 1.00, 0.95, 0.90, 0.85, 0.80, 0.77, 0.77];
    T[usize::from(step).min(T.len() - 1)]
}

/// `Actability.GetProductionTimeMultiplier()`.
pub fn actability_time_multiplier(step: u8) -> f32 {
    const T: [f32; 8] = [1.00, 1.00, 0.95, 0.90, 0.85, 0.80, 0.77, 0.77];
    T[usize::from(step).min(T.len() - 1)]
}

/// `Character.ItemUse(item)` (`Events.OnItemUse` -> quests).
pub fn item_use(server: &GameServer, player: &PlayerRef, template_id: u32) {
    if template_id == 0 {
        return;
    }
    if let Some(h) = server.systems.economy.hooks.quests.get() {
        h.on_item_use(server, player, template_id);
    }
}

/// `EquipmentItemSlotType.Backpack` re-export for callers.
pub const BACKPACK_SLOT_TYPE: u32 = EQUIPMENT_ITEM_SLOT_TYPE_BACKPACK;

#[cfg(test)]
#[path = "backpack_acquire_tests.rs"]
mod backpack_acquire_tests;
