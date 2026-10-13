//! Doodad persistence: `Doodad.Save()` (`REPLACE INTO doodads`), the row delete of
//! `Doodad.Delete()` and `DoodadManager.DeleteDoodadById`.
//!
//! On-change snapshots and deletes enter one FIFO writer. Snapshotting and enqueueing share
//! a lock, so old saves cannot overtake newer state or resurrect deleted objects. The normal
//! save lifecycle drains the writer, including on shutdown.
//! `SaveManager.DoSave` does not save doodads in C# (each change saves itself).

use chrono::NaiveDateTime;
use sqlx::MySqlConnection;
use std::sync::Mutex;
use tokio::sync::{mpsc, oneshot};

use crate::db::{from_unix, min_value};
use crate::server::GameServer;
use crate::world::{DoodadRef, WorldObject};

type Completion = oneshot::Sender<sqlx::Result<()>>;

#[derive(Debug)]
enum Write {
    Save(Box<DoodadRow>, Option<Completion>),
    Delete(u32),
    Flush(Completion),
}

/// One writer per server. It owns no server handle and closes when the manager is dropped.
#[derive(Debug, Default)]
pub struct Persistence {
    sender: Mutex<Option<mpsc::UnboundedSender<Write>>>,
}

impl Persistence {
    fn sender<'a>(
        &self,
        server: &GameServer,
        slot: &'a mut Option<mpsc::UnboundedSender<Write>>,
    ) -> Option<&'a mpsc::UnboundedSender<Write>> {
        if slot.is_none() {
            let rt = tokio::runtime::Handle::try_current().ok()?;
            let (tx, mut rx) = mpsc::unbounded_channel();
            let pool = server.db.clone();
            rt.spawn(async move {
                let mut failure = None;
                while let Some(write) = rx.recv().await {
                    let (result, done) = match write {
                        Write::Save(row, done) => {
                            let result = async {
                                let mut conn = pool.acquire().await?;
                                write_row(&mut conn, &row).await
                            }
                            .await;
                            (result, done)
                        }
                        Write::Delete(id) => {
                            let result = sqlx::query("DELETE FROM doodads WHERE id = ?")
                                .bind(id)
                                .execute(&pool)
                                .await
                                .map(|_| ());
                            (result, None)
                        }
                        Write::Flush(done) => {
                            let result = failure
                                .take()
                                .map_or(Ok(()), |e| Err(sqlx::Error::Protocol(e)));
                            let _ = done.send(result);
                            continue;
                        }
                    };
                    if let Err(e) = &result {
                        tracing::error!("Doodad persistence: {e}");
                        failure = Some(e.to_string());
                    }
                    if let Some(done) = done {
                        let _ = done.send(result);
                    }
                }
            });
            *slot = Some(tx);
        }
        slot.as_ref()
    }

    /// Complete all earlier saves and deletes before returning.
    pub async fn flush(&self) -> sqlx::Result<()> {
        let rx = {
            let slot = self.sender.lock().expect("doodad persistence poisoned");
            let Some(tx) = slot.as_ref() else {
                return Ok(());
            };
            let (done, rx) = oneshot::channel();
            tx.send(Write::Flush(done))
                .map_err(|_| sqlx::Error::Protocol("doodad writer stopped".into()))?;
            rx
        };
        rx.await
            .map_err(|_| sqlx::Error::Protocol("doodad writer stopped".into()))?
    }
}

/// One `doodads` row (the `Save()` parameters).
#[derive(Debug, Clone, PartialEq)]
pub struct DoodadRow {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: u8,
    pub attach_point: u8,
    pub template_id: u32,
    pub current_phase_id: u32,
    pub plant_time: NaiveDateTime,
    pub growth_time: NaiveDateTime,
    pub phase_time: NaiveDateTime,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub roll: f32,
    pub pitch: f32,
    pub yaw: f32,
    pub scale: f32,
    pub item_id: u64,
    pub house_id: u32,
    pub parent_doodad: u32,
    pub item_template_id: u32,
    pub item_container_id: u64,
    pub data: i32,
    pub farm_type: u8,
    pub interaction_counts: String,
}

/// Unix ms to a DATETIME (`0` = `DateTime.MinValue`).
pub fn from_unix_ms(ms: i64) -> NaiveDateTime {
    if ms <= 0 {
        return min_value();
    }
    chrono::DateTime::from_timestamp_millis(ms).map_or_else(min_value, |d| d.naive_utc())
}

/// The `Save()` snapshot (allocates `DbId` when 0). `None` when not persistent.
pub fn snapshot(server: &GameServer, doodad: &DoodadRef) -> Option<DoodadRow> {
    // Lookup Parent (Transform.Parent.GameObject is Doodad { DbId: > 0 })
    let parent_obj_id = doodad.data().transform.parent_obj_id;
    let parent_doodad = if parent_obj_id > 0 {
        server
            .world
            .object(parent_obj_id)
            .and_then(|o| match o {
                WorldObject::Doodad(p) => Some(p.data().db_id),
                _ => None,
            })
            .unwrap_or(0)
    } else {
        0
    };
    let mut d = doodad.data();
    if !d.is_persistent || d.phase.deleted {
        return None;
    }
    if d.db_id == 0 {
        match server.ids.doodad.get_next_id() {
            Ok(id) => d.db_id = id,
            Err(e) => {
                tracing::error!("Cannot allocate doodad database id: {e}");
                return None;
            }
        }
    }
    let t = &d.transform;
    Some(DoodadRow {
        id: d.db_id,
        owner_id: d.owner_id,
        owner_type: d.owner_type,
        attach_point: d.attach_point,
        template_id: d.template_id,
        current_phase_id: d.func_group_id,
        plant_time: from_unix(d.plant_time),
        growth_time: from_unix_ms(d.growth_time_ms),
        phase_time: from_unix_ms(d.phase_time_ms),
        // We save the local position; on load things are re-parented from the data
        x: t.x,
        y: t.y,
        z: t.z,
        roll: t.rot_x,
        pitch: t.rot_y,
        yaw: t.rot_z,
        scale: d.scale,
        item_id: d.item_id,
        house_id: d.owner_db_id,
        parent_doodad,
        item_template_id: d.item_template_id,
        item_container_id: d.coffer.as_ref().map_or(0, |c| c.container_id), // GetItemContainerId
        data: d.data,
        farm_type: d.farm_type,
        interaction_counts: serde_json::to_string(&d.phase.interaction_counts)
            .expect("integer phase counters serialize"),
    })
}

/// `REPLACE INTO doodads ...`.
pub async fn write_row(conn: &mut MySqlConnection, r: &DoodadRow) -> sqlx::Result<()> {
    sqlx::query(
        "REPLACE INTO doodads (`id`, `owner_id`, `owner_type`, `attach_point`, `template_id`, \
         `current_phase_id`, `plant_time`, `growth_time`, `phase_time`, `x`, `y`, `z`, `roll`, \
         `pitch`, `yaw`, `scale`, `item_id`, `house_id`, `parent_doodad`, `item_template_id`, \
         `item_container_id`, `data`, `farm_type`, `interaction_counts`) \
         VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(r.id)
    .bind(r.owner_id)
    .bind(r.owner_type)
    .bind(r.attach_point)
    .bind(r.template_id)
    .bind(r.current_phase_id)
    .bind(r.plant_time)
    .bind(r.growth_time)
    .bind(r.phase_time)
    .bind(r.x)
    .bind(r.y)
    .bind(r.z)
    .bind(r.roll)
    .bind(r.pitch)
    .bind(r.yaw)
    .bind(r.scale)
    .bind(r.item_id)
    .bind(r.house_id)
    .bind(r.parent_doodad)
    .bind(r.item_template_id)
    .bind(r.item_container_id)
    .bind(r.data)
    .bind(r.farm_type)
    .bind(&r.interaction_counts)
    .execute(conn)
    .await?;
    Ok(())
}

/// `Doodad.Save()` (fire and forget).
pub fn save(server: &GameServer, doodad: &DoodadRef) {
    let persistence = &server.systems.npcs.doodads.persistence;
    let mut slot = persistence
        .sender
        .lock()
        .expect("doodad persistence poisoned");
    let Some(row) = snapshot(server, doodad) else {
        return;
    };
    if let Some(tx) = persistence.sender(server, &mut slot) {
        if tx.send(Write::Save(Box::new(row), None)).is_err() {
            tracing::error!("Doodad writer stopped");
        }
    }
}

/// `Doodad.Save()` awaited (callers that need the row written, e.g. tests / player doodads).
pub async fn save_now(server: &GameServer, doodad: &DoodadRef) -> sqlx::Result<()> {
    let rx = {
        let persistence = &server.systems.npcs.doodads.persistence;
        let mut slot = persistence
            .sender
            .lock()
            .expect("doodad persistence poisoned");
        let Some(row) = snapshot(server, doodad) else {
            return Ok(());
        };
        let tx = persistence
            .sender(server, &mut slot)
            .ok_or_else(|| sqlx::Error::Protocol("no doodad writer runtime".into()))?;
        let (done, rx) = oneshot::channel();
        tx.send(Write::Save(Box::new(row), Some(done)))
            .map_err(|_| sqlx::Error::Protocol("doodad writer stopped".into()))?;
        rx
    };
    rx.await
        .map_err(|_| sqlx::Error::Protocol("doodad writer stopped".into()))?
}

/// The row delete of `Doodad.Delete()` (`DELETE FROM doodads WHERE id = @id`, fire and forget).
pub fn delete_row(server: &GameServer, db_id: u32) {
    if db_id == 0 {
        return;
    }
    let persistence = &server.systems.npcs.doodads.persistence;
    let mut slot = persistence
        .sender
        .lock()
        .expect("doodad persistence poisoned");
    if let Some(tx) = persistence.sender(server, &mut slot) {
        if tx.send(Write::Delete(db_id)).is_err() {
            tracing::error!("Doodad writer stopped");
        }
    }
}

/// `DoodadManager.DeleteDoodadById(connection, transaction, dbId)`: deletes a persistent doodad
/// row (not for spawned doodads), releases the DB id and hands the attached item / container to
/// the economy hook. Returns false when no row was deleted.
pub async fn delete_doodad_by_id(
    server: &GameServer,
    conn: &mut MySqlConnection,
    db_id: u32,
) -> sqlx::Result<bool> {
    use sqlx::Row;
    server.systems.npcs.doodads.persistence.flush().await?;
    // First grab the item related data
    let row = sqlx::query(
        "SELECT CAST(item_id AS UNSIGNED) AS item_id, \
         CAST(item_container_id AS UNSIGNED) AS item_container_id FROM doodads WHERE id = ? LIMIT 1",
    )
    .bind(db_id)
    .fetch_optional(&mut *conn)
    .await?;
    let (item_id, container_id) = match row {
        Some(r) => (
            r.try_get::<u64, _>("item_id")?,
            r.try_get::<u64, _>("item_container_id")?,
        ),
        None => (0, 0),
    };
    let n = sqlx::query("DELETE FROM doodads WHERE id = ?")
        .bind(db_id)
        .execute(&mut *conn)
        .await?
        .rows_affected();
    if n == 0 {
        tracing::error!("Failed to delete doodad from DB Id: {db_id}");
        return Ok(false);
    }
    server.ids.doodad.release_id(db_id); // Free up the Id
    if item_id > 0 || container_id > 0 {
        if let Some(h) = server.systems.npcs.hooks.economy.get() {
            h.release_deleted_doodad_items(server, item_id, container_id);
        }
    }
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::doodad::Doodad;
    use crate::world::Entity;

    #[tokio::test]
    #[ignore = "requires AAEMU_TEST_MYSQL; uses a connection-local temporary table"]
    async fn doodad_persistence_orders_updates_and_delete_without_resurrection() {
        let url = std::env::var("AAEMU_TEST_MYSQL").expect("private MySQL URL");
        let pool = sqlx::mysql::MySqlPoolOptions::new()
            .max_connections(1)
            .idle_timeout(None)
            .max_lifetime(None)
            .connect(&url)
            .await
            .unwrap();
        sqlx::query("CREATE TEMPORARY TABLE aaemu_doodad_test_template LIKE doodads")
            .execute(&pool)
            .await
            .unwrap();
        sqlx::query("CREATE TEMPORARY TABLE doodads LIKE aaemu_doodad_test_template")
            .execute(&pool)
            .await
            .unwrap();
        let mut data = crate::data::GameData::empty();
        data.systems.npcs.doodad.templates.insert(
            2336,
            super::super::templates::DoodadTemplate {
                id: 2336,
                ..Default::default()
            },
        );
        let mut server = crate::test_support::test_server_with(data);
        server.db = pool.clone();
        let mut d = Doodad::new(2336);
        d.is_persistent = true;
        d.transform.set_xyz(101.0, 102.0, 50.0);
        d.plant_time = 1_700_000_000;
        d.phase_time_ms = 1_700_000_000_000;
        d.growth_time_ms = 1_700_000_000_000;
        d.phase.interaction_counts.insert(123, 2);
        let doodad = Entity::new(0x500, d);
        for value in 0..100 {
            doodad.data().data = value;
            save(&server, &doodad);
        }
        save_now(&server, &doodad).await.unwrap();
        let id = doodad.data().db_id;
        let value: i32 = sqlx::query_scalar("SELECT data FROM doodads WHERE id = ?")
            .bind(id)
            .fetch_one(&pool)
            .await
            .unwrap();
        assert_eq!(value, 99);
        let world = server.world.world(0).unwrap();
        let spawns = server.systems.npcs.spawns.get_or_create(&world);
        assert_eq!(
            spawns
                .spawn_persistent_doodads(
                    &server,
                    crate::models::doodad::doodad_owner_type::SYSTEM,
                    -1,
                    None,
                    false
                )
                .await
                .unwrap(),
            1
        );
        let restored = spawns.get_all_player_doodads().pop().unwrap();
        assert_eq!(restored.data().transform.x, 101.0);
        assert_eq!(restored.data().phase.interaction_counts.get(&123), Some(&2));
        server
            .systems
            .npcs
            .doodads
            .persistence
            .flush()
            .await
            .unwrap();
        let counters: String =
            sqlx::query_scalar("SELECT interaction_counts FROM doodads WHERE id = ?")
                .bind(id)
                .fetch_one(&pool)
                .await
                .unwrap();
        assert_eq!(
            serde_json::from_str::<std::collections::HashMap<u32, u32>>(&counters)
                .unwrap()
                .get(&123),
            Some(&2)
        );
        let saved_x: f32 = sqlx::query_scalar("SELECT x FROM doodads WHERE id = ?")
            .bind(id)
            .fetch_one(&pool)
            .await
            .unwrap();
        assert_eq!(saved_x, 101.0);
        super::super::manager::delete(&server, &world, &restored);
        save(&server, &restored);
        server
            .systems
            .npcs
            .doodads
            .persistence
            .flush()
            .await
            .unwrap();
        let count: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM doodads WHERE id = ?")
            .bind(id)
            .fetch_one(&pool)
            .await
            .unwrap();
        assert_eq!(count, 0);
    }

    #[tokio::test]
    async fn snapshot_mirrors_save_columns() {
        let server = crate::test_support::test_server();
        let mut d = Doodad::new(2336);
        d.transform.set_position(1.0, 2.0, 3.0, 0.1, 0.2, 0.3);
        d.func_group_id = 4651;
        d.plant_time = 1_700_000_000;
        d.growth_time_ms = 1_700_000_001_500;
        d.owner_id = 5;
        d.owner_type = 254;
        d.data = 3;
        let doodad = Entity::new(0x500, d);
        assert!(snapshot(&server, &doodad).is_none()); // not persistent
        doodad.data().is_persistent = true;
        let r = snapshot(&server, &doodad).unwrap();
        assert!(r.id > 0);
        assert_eq!(doodad.data().db_id, r.id);
        assert_eq!(r.template_id, 2336);
        assert_eq!(r.current_phase_id, 4651);
        assert_eq!(
            (r.x, r.y, r.z, r.roll, r.pitch, r.yaw),
            (1.0, 2.0, 3.0, 0.1, 0.2, 0.3)
        );
        assert_eq!(r.plant_time, from_unix(1_700_000_000));
        assert_eq!(
            r.growth_time.and_utc().timestamp_millis(),
            1_700_000_001_500
        );
        assert_eq!(r.phase_time, min_value());
        assert_eq!((r.owner_id, r.owner_type, r.data), (5, 254, 3));
        // The id is kept on the next save.
        assert_eq!(snapshot(&server, &doodad).unwrap().id, r.id);
    }
}
