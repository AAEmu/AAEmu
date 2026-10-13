//! The user-item half of `Core/Managers/ItemManager`: `LoadUserItems` (every persistent
//! container and item is held in memory), `GetItemContainerForCharacter`, `Create`, `ReleaseId`
//! and `Save` (dirty containers / items, deleted items).

use std::sync::{Arc, Mutex};

use sqlx::mysql::{MySqlPool, MySqlRow};
use sqlx::{MySql, Row, Transaction};

use crate::data::ItemData;
use crate::db::{dt, from_unix, int, unix};
use crate::error::Result;
use crate::managers::id::IdManager;
use crate::models::container::{slot_type, ContainerKind, ContainerRef, ItemContainer};
use crate::models::item::{Item, ItemClass, ItemRow, TreasureMapTarget};
use crate::systems::HookSlot;

/// `ItemManager` (user items).
///
/// Lock order: the lists below are leaf locks that are never held while a container is locked
/// (lookups snapshot the container handles first), so they may be taken while a container lock
/// is held (`GetNewId` / `ReleaseId` during container operations).
#[derive(Debug, Default)]
pub struct ItemManager {
    /// `_allPersistentContainers` in insertion order.
    containers: Mutex<Vec<ContainerRef>>,
    /// Items that are in `_allItems` but in no persistent container (orphans, merged stacks).
    loose: Mutex<Vec<Item>>,
    /// `_removedItems`.
    removed: Mutex<Vec<u64>>,
    /// Item/container snapshots written by the current world transaction, awaiting commit.
    pending_save: Mutex<Option<ItemSaveBatch>>,
    /// Serialises `GetItemContainerForCharacter`'s find-or-create.
    create_lock: Mutex<()>,
    /// `TreasureMap.GenerateTreasureMapData`'s chest pick (installed by the npcs subsystem,
    /// which owns the spawners and doodad templates).
    pub treasure_maps: HookSlot<dyn TreasureMapSource>,
}

#[derive(Debug, Default)]
struct ItemSaveBatch {
    removed: Vec<u64>,
    containers: Vec<(ContainerRef, ContainerSaveRow)>,
    items: Vec<(Option<ContainerRef>, u64, Item)>,
}

#[derive(Debug, PartialEq, Eq)]
struct ContainerSaveRow {
    container_id: u64,
    kind: ContainerKind,
    slot_type: u8,
    size: i32,
    owner_id: u32,
    mate_id: u32,
}

impl ContainerSaveRow {
    fn of(c: &ItemContainer) -> Self {
        Self {
            container_id: c.container_id,
            kind: c.kind,
            slot_type: c.slot_type,
            size: c.container_size(),
            owner_id: c.owner_id,
            mate_id: c.mate_id,
        }
    }
}

/// Picks the treasure chest a new treasure map points at
/// (`SpawnManager.GetTreasureChestDoodadSpawners()` of the default instance + a random pick +
/// `DoodadManager.GetTemplate(...).GroupId`). `None` = no chest spawners.
pub trait TreasureMapSource: Send + Sync {
    fn pick_treasure_chest(&self) -> Option<TreasureMapTarget>;
}

fn lock<T>(m: &Mutex<T>) -> std::sync::MutexGuard<'_, T> {
    m.lock().expect("item manager poisoned")
}

impl ItemManager {
    /// Snapshot of `_allPersistentContainers`.
    pub fn all_containers(&self) -> Vec<ContainerRef> {
        lock(&self.containers).clone()
    }

    /// `GetItemContainerByDbId`.
    pub fn container_by_id(&self, id: u64) -> Option<ContainerRef> {
        self.all_containers()
            .into_iter()
            .find(|c| c.lock().expect("container poisoned").container_id == id)
    }

    /// `GetContainerSlotTypeByContainerId`.
    pub fn container_slot_type(&self, id: u64) -> u8 {
        self.container_by_id(id).map_or(slot_type::NONE, |c| {
            c.lock().expect("container poisoned").slot_type
        })
    }

    /// `GetItemContainerForCharacter(characterId, slotType, parentUnit, mateId)`: the first
    /// persistent container matching owner/slot/mate, or a new one (new container id unless the
    /// slot type is `None`, persistent unless `None`). Never call it while holding a container
    /// lock.
    pub fn container_for_character(
        &self,
        character_id: u32,
        slot: u8,
        mate_id: u32,
        container_ids: &IdManager,
    ) -> ContainerRef {
        let _create = lock(&self.create_lock);
        for c in self.all_containers() {
            let g = c.lock().expect("container poisoned");
            if g.owner_id == character_id && g.slot_type == slot && g.mate_id == mate_id {
                drop(g);
                return c;
            }
        }
        let kind = match slot {
            slot_type::EQUIPMENT => ContainerKind::Equipment,
            slot_type::EQUIPMENT_MATE => ContainerKind::MateEquipment,
            _ => ContainerKind::Item,
        };
        let id = if slot != slot_type::NONE {
            match container_ids.get_next_id() {
                Ok(id) => u64::from(id),
                Err(e) => {
                    tracing::error!("{e}");
                    0
                }
            }
        } else {
            0
        };
        let mut c = ItemContainer::new(kind, character_id, slot, id);
        if mate_id > 0 {
            c.mate_id = mate_id;
        }
        let c = c.into_ref();
        if slot != slot_type::NONE {
            lock(&self.containers).push(c.clone());
        }
        c
    }

    /// `NewCofferContainer(characterId)`.
    pub fn new_coffer_container(
        &self,
        character_id: u32,
        container_ids: &IdManager,
    ) -> ContainerRef {
        let id = match container_ids.get_next_id() {
            Ok(id) => u64::from(id),
            Err(e) => {
                tracing::error!("{e}");
                0
            }
        };
        let c = ItemContainer::new(ContainerKind::Coffer, character_id, slot_type::TRADE, id)
            .into_ref();
        lock(&self.containers).push(c.clone());
        c
    }

    /// `DeleteItemContainer(container)` (the list part; the C# also deletes the
    /// `item_containers` row, see [`ItemManager::delete_container_row`]).
    pub fn forget_container(&self, container_id: u64) -> bool {
        let list = self.all_containers();
        let Some(idx) = list
            .iter()
            .position(|c| c.lock().expect("container poisoned").container_id == container_id)
        else {
            return false;
        };
        let target = list[idx].clone();
        lock(&self.containers).retain(|c| !Arc::ptr_eq(c, &target));
        true
    }

    /// The SQL half of `DeleteItemContainer`.
    pub async fn delete_container_row(pool: &MySqlPool, container_id: u64) -> Result<bool> {
        let r = sqlx::query("DELETE FROM item_containers WHERE container_id = ?")
            .bind(container_id)
            .execute(pool)
            .await?;
        Ok(r.rows_affected() > 0)
    }

    /// `GetNewId` (+ removal from the deleted-items list).
    pub fn new_id(&self, item_ids: &IdManager) -> Option<u64> {
        let id = match item_ids.get_next_id() {
            Ok(id) => u64::from(id),
            Err(e) => {
                tracing::error!("{e}");
                return None;
            }
        };
        lock(&self.removed).retain(|&r| r != id);
        Some(id)
    }

    /// `ItemManager.Create(templateId, count, grade)`. The item is not placed in a container.
    #[allow(clippy::too_many_arguments)]
    pub fn create(
        &self,
        template_id: u32,
        count: i32,
        grade: u8,
        world_id: u8,
        data: &ItemData,
        item_ids: &IdManager,
        now: i64,
    ) -> Option<Item> {
        let template = data.template(template_id)?;
        let id = self.new_id(item_ids)?;
        let treasure = if template.class_type() == ItemClass::TreasureMap {
            self.treasure_maps
                .get()
                .and_then(|h| h.pick_treasure_chest())
        } else {
            None
        };
        Some(Item::create_ex(
            id, template, count, grade, world_id, data, now, treasure,
        ))
    }

    /// `ItemManager.Create(templateId, count, grade, generateId: false)`: id 0, not tracked.
    pub fn create_detached(
        &self,
        template_id: u32,
        count: i32,
        grade: u8,
        world_id: u8,
        data: &ItemData,
        now: i64,
    ) -> Option<Item> {
        let template = data.template(template_id)?;
        let treasure = if template.class_type() == ItemClass::TreasureMap {
            self.treasure_maps
                .get()
                .and_then(|h| h.pick_treasure_chest())
        } else {
            None
        };
        Some(Item::create_ex(
            0, template, count, grade, world_id, data, now, treasure,
        ))
    }

    /// `MarkItemForDbDeletion` (queue the row for deletion without freeing the id).
    pub fn mark_for_db_deletion(&self, item_id: u64) {
        let mut removed = lock(&self.removed);
        if item_id != 0 && !removed.contains(&item_id) {
            removed.push(item_id);
        }
    }

    /// `ReleaseId`: queue the row for deletion and free the id.
    pub fn release_id(&self, item_id: u64, item_ids: &IdManager) {
        self.mark_for_db_deletion(item_id);
        lock(&self.loose).retain(|i| i.id != item_id);
        item_ids.release_id(item_id as u32);
    }

    /// Keeps an item that is in no persistent container (`_allItems` only).
    pub fn add_loose(&self, item: Item) {
        let mut loose = lock(&self.loose);
        loose.retain(|i| i.id != item.id);
        loose.push(item);
    }

    /// `AddItem(item)`: registers an item that is in no persistent container. `false` (and an
    /// error log) when an item with that id is already known.
    pub fn add_item(&self, item: Item) -> bool {
        if self.item_by_id(item.id).is_some() {
            tracing::error!(
                "Failed to load item with ID {}, possible duplicate entries!",
                item.id
            );
            return false;
        }
        lock(&self.loose).push(item);
        true
    }

    /// Snapshot of `_removedItems` (rows queued for deletion by the next save).
    pub fn removed_items(&self) -> Vec<u64> {
        lock(&self.removed).clone()
    }

    /// Takes a loose item back (it is about to be placed in a container).
    pub fn take_loose(&self, item_id: u64) -> Option<Item> {
        let mut loose = lock(&self.loose);
        let idx = loose.iter().position(|i| i.id == item_id)?;
        Some(loose.remove(idx))
    }

    /// `GetItemByItemId`.
    pub fn item_by_id(&self, item_id: u64) -> Option<Item> {
        for c in self.all_containers() {
            if let Some(i) = c
                .lock()
                .expect("container poisoned")
                .get_item_by_id(item_id)
            {
                return Some(i.clone());
            }
        }
        lock(&self.loose).iter().find(|i| i.id == item_id).cloned()
    }

    /// The persistent container that holds `item_id` (`item._holdingContainer`).
    pub fn holding_container(&self, item_id: u64) -> Option<ContainerRef> {
        self.all_containers().into_iter().find(|c| {
            c.lock()
                .expect("container poisoned")
                .get_item_by_id(item_id)
                .is_some()
        })
    }

    /// `GetItemByItemId(id)` followed by an in-place change of the live item (`None` when the
    /// item is in no persistent container or the loose list).
    pub fn update_item<R>(&self, item_id: u64, f: impl FnOnce(&mut Item) -> R) -> Option<R> {
        for c in self.all_containers() {
            let mut g = c.lock().expect("container poisoned");
            if let Some(i) = g.get_item_by_id_mut(item_id) {
                return Some(f(i));
            }
        }
        lock(&self.loose)
            .iter_mut()
            .find(|i| i.id == item_id)
            .map(f)
    }

    /// Adds an already loaded container (tests / `LoadUserItems`).
    pub fn add_container(&self, c: ContainerRef) {
        lock(&self.containers).push(c);
    }

    /// `LoadUserItems`.
    pub async fn load_user_items(
        &self,
        pool: &MySqlPool,
        data: &ItemData,
        world_id: u8,
        container_ids: &IdManager,
    ) -> Result<()> {
        tracing::info!("Loading user items ...");
        let rows = sqlx::query(
            "SELECT CAST(container_id AS UNSIGNED) AS container_id, container_type, CAST(slot_type AS SIGNED) AS slot_type, \
             CAST(container_size AS SIGNED) AS container_size, CAST(owner_id AS UNSIGNED) AS owner_id, CAST(mate_id AS UNSIGNED) AS mate_id \
             FROM item_containers",
        )
        .fetch_all(pool)
        .await?;
        for r in rows {
            let ty: String = r.try_get("container_type")?;
            let mut c = ItemContainer::new(
                ContainerKind::from_type_name(&ty),
                r.try_get::<u64, _>("owner_id")? as u32,
                int(&r, "slot_type")? as u8,
                r.try_get::<u64, _>("container_id")? as u32 as u64,
            );
            c.set_container_size(int(&r, "container_size")? as i32);
            c.mate_id = r.try_get::<u64, _>("mate_id")? as u32;
            c.dirty = false;
            lock(&self.containers).push(c.into_ref());
        }

        let rows = sqlx::query(ITEM_SELECT).fetch_all(pool).await?;
        for r in rows {
            let row = item_row(&r)?;
            let container_id = row.container_id;
            let this_slot = row.slot;
            let template = data.template(row.template_id);
            if ItemClass::from_type_name(&row.type_name).is_none() {
                tracing::warn!("Item type {} not found for id {}!", row.type_name, row.id);
                if template.is_none() {
                    tracing::error!(
                        "Unable to restore template {} for item {}, item will not be loaded!",
                        row.template_id,
                        row.id
                    );
                    continue;
                }
            }
            // Unknown types fall back to the template's ClassType inside `from_row`.
            let mut item = Item::from_row(row, template, world_id);
            let container = if container_id > 0 {
                self.container_by_id(container_id)
            } else {
                None
            };
            match container {
                Some(c) => {
                    let slot = item.slot;
                    let item_id = item.id;
                    let res = c
                        .lock()
                        .expect("container poisoned")
                        .add_new_item(item, slot, data);
                    match res {
                        Ok(true) => {
                            // `item.IsDirty = false` (a stacked item no longer exists on its own).
                            let mut g = c.lock().expect("container poisoned");
                            if let Some(i) = g.items.iter_mut().find(|i| i.id == item_id) {
                                i.dirty = false;
                            }
                        }
                        Ok(false) => {
                            tracing::error!(
                                "Failed to add item {item_id} to existing container {container_id} !"
                            );
                        }
                        Err(item) => {
                            tracing::error!(
                                "Failed to add item {} to existing container {container_id} !",
                                item.id
                            );
                            lock(&self.loose).push(item);
                        }
                    }
                }
                None => {
                    tracing::trace!(
                        "Can't find a container for Item {}, ContainerId: {container_id}",
                        item.id
                    );
                    if item.owner_id > 0 {
                        let c = self.container_for_character(
                            item.owner_id as u32,
                            item.slot_type,
                            0,
                            container_ids,
                        );
                        let mut g = c.lock().expect("container poisoned");
                        let id = g.container_id;
                        match g.add_new_item(item, this_slot, data) {
                            Ok(_) => {
                                if let Some(i) = g.items.first_mut() {
                                    i.slot = this_slot;
                                    i.dirty = true;
                                }
                            }
                            Err(mut item) => {
                                tracing::error!(
                                    "Failed to add owned item ({}) to new container (Id:{id}) !",
                                    item.id
                                );
                                item.slot = this_slot;
                                item.dirty = false;
                                drop(g);
                                lock(&self.loose).push(item);
                            }
                        }
                    } else {
                        tracing::warn!(
                            "Could not find a new container for Orphaned item {} ({}, ContainerId: {container_id}",
                            item.id,
                            item.template_id
                        );
                        item.slot = this_slot;
                        item.dirty = false;
                        lock(&self.loose).push(item);
                    }
                }
            }
        }
        // "Starting Timed Items Task ...": `systems::economy::item_timers::start` (from
        // `systems::economy::System::start`).
        Ok(())
    }

    /// `ItemManager.Save`: deletes queued rows, writes dirty containers and items. Returns
    /// `(updated items, deleted items, updated containers)`.
    pub async fn save(&self, tx: &mut Transaction<'_, MySql>) -> Result<(usize, usize, usize)> {
        *lock(&self.pending_save) = None;
        let removed = lock(&self.removed).clone();
        let mut batch = ItemSaveBatch {
            removed: removed.clone(),
            ..Default::default()
        };
        let containers = self.all_containers();
        let loose = lock(&self.loose).clone();
        let mut deleted = 0;
        if !removed.is_empty() {
            let list = removed
                .iter()
                .map(u64::to_string)
                .collect::<Vec<_>>()
                .join(",");
            deleted = sqlx::query(&format!("DELETE FROM items WHERE `id` IN({list})"))
                .execute(&mut **tx)
                .await?
                .rows_affected() as usize;
            if deleted != removed.len() {
                tracing::error!(
                    "Some items could not be deleted, only {deleted}/{} items removed !",
                    removed.len()
                );
            }
        }

        let mut container_updates = 0;
        let mut updated = 0;
        for c in &containers {
            let snapshot = c.lock().expect("container poisoned").clone();
            if snapshot.slot_type != slot_type::NONE && snapshot.container_id > 0 && snapshot.dirty
            {
                let res = sqlx::query(
                    "REPLACE INTO item_containers (`container_id`,`container_type`,`slot_type`,`container_size`,`owner_id`,`mate_id`) VALUES (?, ?, ?, ?, ?, ?)",
                )
                .bind(snapshot.container_id)
                .bind(snapshot.kind.type_name())
                .bind(i32::from(snapshot.slot_type))
                .bind(snapshot.container_size())
                .bind(snapshot.owner_id)
                .bind(snapshot.mate_id)
                .execute(&mut **tx)
                .await?;
                container_updates += res.rows_affected() as usize;
                batch
                    .containers
                    .push((c.clone(), ContainerSaveRow::of(&snapshot)));
            }
            for item in &snapshot.items {
                if item.slot_type == slot_type::NONE || !item.dirty {
                    continue;
                }
                save_item(tx, item, snapshot.container_id).await?;
                updated += 1;
                batch
                    .items
                    .push((Some(c.clone()), snapshot.container_id, item.clone()));
            }
        }
        for item in &loose {
            if item.slot_type == slot_type::NONE || !item.dirty {
                continue;
            }
            save_item(tx, item, 0).await?;
            updated += 1;
            batch.items.push((None, 0, item.clone()));
        }
        *lock(&self.pending_save) = Some(batch);
        Ok((updated, deleted, container_updates))
    }

    /// Mark only committed snapshots clean, preserving changes made during SQL awaits and
    /// every pending item deletion after a failed/rolled-back transaction.
    pub fn commit_save(&self) {
        let Some(batch) = lock(&self.pending_save).take() else {
            return;
        };
        lock(&self.removed).retain(|id| !batch.removed.contains(id));
        for (container, row) in batch.containers {
            let mut c = container.lock().expect("container poisoned");
            if ContainerSaveRow::of(&c) == row {
                c.dirty = false;
            }
        }
        for (container, container_id, snapshot) in batch.items {
            match container {
                Some(container) => {
                    let mut c = container.lock().expect("container poisoned");
                    if c.container_id != container_id {
                        continue;
                    }
                    if let Some(item) = c.items.iter_mut().find(|i| i.id == snapshot.id) {
                        if *item == snapshot {
                            item.dirty = false;
                        }
                    }
                }
                None => {
                    if let Some(item) = lock(&self.loose).iter_mut().find(|i| i.id == snapshot.id) {
                        if *item == snapshot {
                            item.dirty = false;
                        }
                    }
                }
            }
        }
    }
}

async fn save_item(tx: &mut Transaction<'_, MySql>, item: &Item, container_id: u64) -> Result<()> {
    let res = sqlx::query(
        "REPLACE INTO items (`id`,`type`,`template_id`,`container_id`,`slot_type`,`slot`,`count`,`details`,`lifespan_mins`,`made_unit_id`,\
         `unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`,`flags`,`ucc`,`expire_time`,`expire_online_minutes`,`charge_time`,`charge_count`) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(item.id)
    .bind(item.class.type_name())
    .bind(item.template_id)
    .bind(container_id)
    .bind(i32::from(item.slot_type))
    .bind(item.slot)
    .bind(item.count)
    .bind(item.details_blob())
    .bind(item.lifespan_mins)
    .bind(item.made_unit_id)
    .bind(from_unix(item.unsecure_time))
    .bind(from_unix(item.unpack_time))
    .bind(item.owner_id)
    .bind(from_unix(item.create_time))
    .bind(item.grade)
    .bind(item.flags)
    .bind(item.ucc_id)
    .bind(from_unix(item.expiration_time))
    .bind(item.expiration_online_minutes_left)
    .bind(from_unix(item.charge_start_time))
    .bind(item.charge_count)
    .execute(&mut **tx)
    .await?;
    if res.rows_affected() == 0 {
        return Err(crate::GameError::Protocol(format!(
            "Item {} was not persisted",
            item.id
        )));
    }
    Ok(())
}

const ITEM_SELECT: &str = "SELECT `type`, CAST(id AS UNSIGNED) AS id, CAST(template_id AS SIGNED) AS template_id, \
     CAST(container_id AS UNSIGNED) AS container_id, CAST(slot_type AS SIGNED) AS slot_type, \
     CAST(slot AS SIGNED) AS slot, CAST(`count` AS SIGNED) AS item_count, CAST(lifespan_mins AS SIGNED) AS lifespan_mins, \
     CAST(made_unit_id AS SIGNED) AS made_unit_id, unsecure_time, unpack_time, created_at, CAST(`owner` AS UNSIGNED) AS owner, \
     CAST(flags AS SIGNED) AS flags, CAST(ucc AS SIGNED) AS ucc, details, CAST(grade AS SIGNED) AS grade, charge_time, \
     expire_time, (expire_online_minutes + 0E0) AS expire_online_minutes, CAST(charge_count AS SIGNED) AS charge_count \
     FROM items";

fn item_row(r: &MySqlRow) -> Result<ItemRow> {
    Ok(ItemRow {
        type_name: r.try_get("type")?,
        id: r.try_get::<u64, _>("id")?,
        template_id: int(r, "template_id")? as u32,
        container_id: r.try_get::<u64, _>("container_id")?,
        slot_type: int(r, "slot_type")? as u8,
        slot: int(r, "slot")? as i32,
        count: int(r, "item_count")? as i32,
        lifespan_mins: int(r, "lifespan_mins")? as i32,
        made_unit_id: int(r, "made_unit_id")? as u32,
        unsecure_time: unix(dt(r, "unsecure_time")),
        unpack_time: unix(dt(r, "unpack_time")),
        created_at: unix(dt(r, "created_at")),
        owner: r.try_get::<u64, _>("owner")?,
        flags: int(r, "flags")? as u8,
        ucc: int(r, "ucc")? as u32,
        details: r
            .try_get::<Option<Vec<u8>>, _>("details")?
            .unwrap_or_default(),
        grade: r.try_get::<Option<i64>, _>("grade")?.unwrap_or(0) as u8,
        charge_time: unix(dt(r, "charge_time")),
        expire_time: unix(dt(r, "expire_time")),
        expire_online_minutes: r
            .try_get::<Option<f64>, _>("expire_online_minutes")?
            .unwrap_or(0.0),
        charge_count: r.try_get::<Option<i64>, _>("charge_count")?.unwrap_or(0) as i32,
    })
}

/// Shared handle (the item manager is used from the world, the stream server and the save task).
pub type ItemManagerRef = Arc<ItemManager>;

#[cfg(test)]
#[path = "item_tests.rs"]
mod item_tests;
