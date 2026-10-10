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
use crate::models::item::{Item, ItemClass, ItemRow};

#[derive(Debug, Default)]
struct Store {
    /// `_allPersistentContainers` in insertion order.
    containers: Vec<ContainerRef>,
    /// Items that are in `_allItems` but in no container (orphans).
    loose: Vec<Item>,
    /// `_removedItems`.
    removed: Vec<u64>,
}

/// `ItemManager` (user items).
#[derive(Debug, Default)]
pub struct ItemManager {
    store: Mutex<Store>,
}

impl ItemManager {
    fn store(&self) -> std::sync::MutexGuard<'_, Store> {
        self.store.lock().expect("item store poisoned")
    }

    /// `GetItemContainerByDbId`.
    pub fn container_by_id(&self, id: u64) -> Option<ContainerRef> {
        self.store()
            .containers
            .iter()
            .find(|c| c.lock().expect("container poisoned").container_id == id)
            .cloned()
    }

    /// `GetItemContainerForCharacter(characterId, slotType, parentUnit, mateId)`: the first
    /// persistent container matching owner/slot/mate, or a new one (new container id unless the
    /// slot type is `None`, persistent unless `None`).
    pub fn container_for_character(
        &self,
        character_id: u32,
        slot: u8,
        mate_id: u32,
        container_ids: &IdManager,
    ) -> ContainerRef {
        let mut store = self.store();
        for c in &store.containers {
            let g = c.lock().expect("container poisoned");
            if g.owner_id == character_id && g.slot_type == slot && g.mate_id == mate_id {
                return c.clone();
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
            store.containers.push(c.clone());
        }
        c
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
        let mut store = self.store();
        store.removed.retain(|&r| r != id);
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
        Some(Item::create(
            id, template, count, grade, world_id, data, now,
        ))
    }

    /// `ReleaseId`: queue the row for deletion and free the id.
    pub fn release_id(&self, item_id: u64, item_ids: &IdManager) {
        {
            let mut store = self.store();
            if item_id != 0 && !store.removed.contains(&item_id) {
                store.removed.push(item_id);
            }
            store.loose.retain(|i| i.id != item_id);
        }
        item_ids.release_id(item_id as u32);
    }

    /// `GetItemByItemId`.
    pub fn item_by_id(&self, item_id: u64) -> Option<Item> {
        let store = self.store();
        for c in &store.containers {
            if let Some(i) = c
                .lock()
                .expect("container poisoned")
                .get_item_by_id(item_id)
            {
                return Some(i.clone());
            }
        }
        store.loose.iter().find(|i| i.id == item_id).cloned()
    }

    /// Adds an already loaded container (tests / `LoadUserItems`).
    pub fn add_container(&self, c: ContainerRef) {
        self.store().containers.push(c);
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
            self.store().containers.push(c.into_ref());
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
            let mut item = Item::from_row(row, template, world_id);
            if ItemClass::from_type_name(&item_type_name_of(&r)).is_none() {
                if let Some(t) = template {
                    item.class = t.class_type();
                }
            }
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
                            self.store().loose.push(item);
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
                                self.store().loose.push(item);
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
                        self.store().loose.push(item);
                    }
                }
            }
        }
        // Later phase: ItemTimerTask (expiration timers).
        Ok(())
    }

    /// `ItemManager.Save`: deletes queued rows, writes dirty containers and items. Returns
    /// `(updated items, deleted items, updated containers)`.
    pub async fn save(&self, tx: &mut Transaction<'_, MySql>) -> Result<(usize, usize, usize)> {
        let (removed, containers, loose) = {
            let mut s = self.store();
            (
                std::mem::take(&mut s.removed),
                s.containers.clone(),
                s.loose.clone(),
            )
        };
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
                .await;
                match res {
                    Ok(r) => {
                        container_updates += r.rows_affected() as usize;
                        if r.rows_affected() > 0 {
                            c.lock().expect("container poisoned").dirty = false;
                        }
                    }
                    Err(e) => tracing::error!("{e}"),
                }
            }
            for item in &snapshot.items {
                if item.slot_type == slot_type::NONE || !item.dirty {
                    continue;
                }
                if save_item(tx, item, snapshot.container_id).await {
                    updated += 1;
                    let mut g = c.lock().expect("container poisoned");
                    if let Some(i) = g.items.iter_mut().find(|i| i.id == item.id) {
                        i.dirty = false;
                    }
                }
            }
        }
        for item in &loose {
            if item.slot_type == slot_type::NONE || !item.dirty {
                continue;
            }
            if save_item(tx, item, 0).await {
                updated += 1;
            }
        }
        Ok((updated, deleted, container_updates))
    }
}

async fn save_item(tx: &mut Transaction<'_, MySql>, item: &Item, container_id: u64) -> bool {
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
    .await;
    match res {
        Ok(r) if r.rows_affected() >= 1 => true,
        Ok(_) => {
            tracing::error!("Error updating items {} ({}) !", item.id, item.template_id);
            false
        }
        Err(e) => {
            tracing::error!("Error: {e} (item {})", item.id);
            false
        }
    }
}

const ITEM_SELECT: &str = "SELECT `type`, CAST(id AS UNSIGNED) AS id, CAST(template_id AS SIGNED) AS template_id, \
     CAST(container_id AS UNSIGNED) AS container_id, CAST(slot_type AS SIGNED) AS slot_type, \
     CAST(slot AS SIGNED) AS slot, CAST(`count` AS SIGNED) AS item_count, CAST(lifespan_mins AS SIGNED) AS lifespan_mins, \
     CAST(made_unit_id AS SIGNED) AS made_unit_id, unsecure_time, unpack_time, created_at, CAST(`owner` AS UNSIGNED) AS owner, \
     CAST(flags AS SIGNED) AS flags, CAST(ucc AS SIGNED) AS ucc, details, CAST(grade AS SIGNED) AS grade, charge_time, \
     expire_time, CAST(expire_online_minutes AS DOUBLE) AS expire_online_minutes, CAST(charge_count AS SIGNED) AS charge_count \
     FROM items";

fn item_type_name_of(r: &MySqlRow) -> String {
    r.try_get("type").unwrap_or_default()
}

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
