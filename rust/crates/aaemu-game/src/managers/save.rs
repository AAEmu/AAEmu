//! `Core/Managers/SaveManager`: the periodic save (`AutoSaveInterval` minutes) of items and
//! online characters in one transaction, and the final save on shutdown. The subsystems' rows
//! (houses, mails, auction, crimes, slaves) are written by `systems::save_stage` at the C#
//! positions of the transaction.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::Utc;

use crate::db_character::save_character;
use crate::models::character::Character;
use crate::server::GameServer;
use crate::systems::{save_stage, SaveStage};
use crate::world::PlayerRef;

pub(crate) struct FinancialSnapshot {
    pub(crate) houses: crate::systems::housing::manager::HousingSaveBatch,
    pub(crate) dominions: crate::systems::housing::dominion::state::SaveBatch,
    pub(crate) mails: crate::systems::economy::mail::MailSaveBatch,
    pub(crate) auctions: crate::systems::economy::auction::AuctionSaveBatch,
    pub(crate) items: super::item::ItemSaveBatch,
    pub(crate) schedule_items:
        crate::systems::npcs::schedule_item_persistence::PreparedScheduleSave,
    pub(crate) characters: Vec<(PlayerRef, Character)>,
}

/// Capture property transactions before any database await. Claim/capture, tax payments,
/// certificates, receipts and wallets must describe the same logical operation boundary.
pub(crate) fn capture_financial_snapshot(server: &GameServer) -> FinancialSnapshot {
    let _auction = server.systems.economy.auction.operation_lock();
    let _housing = server
        .systems
        .housing
        .operation_lock
        .lock()
        .expect("housing poisoned");
    let houses = crate::systems::housing::manager::prepare_save(server);
    let dominions = server.systems.housing.dominions.prepare_save();
    let _economy = server.systems.economy.ops_lock();
    let mut players = server.world.all_characters();
    for player in server
        .save
        .departing
        .lock()
        .expect("departure saves poisoned")
        .values()
    {
        if !players
            .iter()
            .any(|live| live.character_id == player.character_id)
        {
            players.push(player.clone());
        }
    }
    FinancialSnapshot {
        houses,
        dominions,
        mails: server.systems.economy.mail.prepare_save(),
        auctions: server.systems.economy.auction.prepare_save(server),
        items: server.items.prepare_save(),
        // Captured under the same `ops_lock` as the granted items / mails of a take.
        schedule_items: server.systems.npcs.schedule_items.prepare_save(),
        characters: players
            .into_iter()
            .map(|p| {
                // Resolved before locking the character: `world_pos_rot` locks transforms.
                let world = save_world_position(server, &p);
                let mut character = p.data().clone();
                if let Some(world) = world {
                    let t = &mut character.unit.transform;
                    t.set_position(
                        world.x,
                        world.y,
                        world.z,
                        world.rot_x,
                        world.rot_y,
                        world.rot_z,
                    );
                    t.parent_obj_id = 0;
                }
                (p, character)
            })
            .collect(),
    }
}

/// The world position of a character attached to a parent (ship, mate, transfer): its
/// transform is then relative to the parent, but the row must hold world coordinates
/// (`Character.Save` writes `Transform.World.Position` / `Rotation`). `None` when the
/// character has no parent, or the parent is gone (the local values are kept, with a warning).
fn save_world_position(
    server: &GameServer,
    player: &PlayerRef,
) -> Option<crate::models::transform::PositionAndRotation> {
    let obj = crate::world::WorldObject::from(player);
    let parent_id = obj.transform().parent_obj_id;
    if parent_id == 0 {
        return None;
    }
    let Some(world) = server.world.world_of(&obj) else {
        tracing::warn!(
            "Saving character {}: no world instance for its parent {parent_id}; \
             saving the parent-relative position",
            player.character_id
        );
        return None;
    };
    if world.object(parent_id).is_none() {
        tracing::warn!(
            "Saving character {}: parent object {parent_id} not found; \
             saving the parent-relative position",
            player.character_id
        );
        return None;
    }
    Some(world.world_pos_rot(&obj))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::slave::Slave;
    use crate::test_support::{spawn_player, test_character, test_server, TestClient};
    use crate::world::{Entity, WorldObject};

    #[tokio::test]
    async fn snapshot_saves_world_position_of_attached_character() {
        let s = test_server();
        let client = TestClient::new(&s, 1, 1);
        let player = spawn_player(
            &s,
            &client,
            test_character(1, "Deck", 1003.0, 1002.0),
            0x100,
        );
        let world = s.world.world(0).unwrap();
        let obj = WorldObject::from(&player);
        let mut hull = Slave::new(1, 1);
        hull.unit
            .transform
            .set_position(1000.0, 2000.0, 305.0, 0.0, 0.0, 0.0);
        let slave = Entity::new(0x300, hull);
        world.spawn(&slave, &s.view_ctx());
        world.attach_child(&WorldObject::from(&slave), &obj);
        player.data().unit.transform.set_xyz(2.0, -3.0, 4.0);

        let snapshot = capture_financial_snapshot(&s);
        let (_, saved) = snapshot
            .characters
            .iter()
            .find(|(p, _)| p.character_id == player.character_id)
            .expect("character in snapshot");
        let t = &saved.unit.transform;
        assert_eq!(t.parent_obj_id, 0);
        assert!((t.x - 1002.0).abs() < 1e-3, "x = {}", t.x);
        assert!((t.y - 1997.0).abs() < 1e-3, "y = {}", t.y);
        assert!((t.z - 309.0).abs() < 1e-3, "z = {}", t.z);

        let live = player.data().unit.transform;
        assert_eq!(live.parent_obj_id, slave.obj_id);
        assert_eq!((live.x, live.y, live.z), (2.0, -3.0, 4.0));
    }
}

struct CharacterSaveAck {
    player: PlayerRef,
    updated: i64,
    removed: [Vec<u32>; 4],
    money: (i64, i64),
}

pub(crate) struct FinancialSaveReceipt {
    counts: (usize, usize, usize, usize, usize),
    schedule_items: crate::systems::npcs::schedule_item_manager::ScheduleSaveToken,
    characters: Vec<CharacterSaveAck>,
}

pub(crate) async fn write_financial_snapshot(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    snapshot: FinancialSnapshot,
) -> crate::Result<FinancialSaveReceipt> {
    let mut systems =
        crate::systems::save_prepared_financial_stage(server, tx, SaveStage::BeforeItems).await?;
    let (updated, deleted) =
        crate::systems::housing::manager::write_prepared(server, tx, snapshot.houses).await?;
    systems += updated + deleted;
    systems += server
        .systems
        .housing
        .dominions
        .save_prepared(tx, snapshot.dominions)
        .await?;
    let (updated, deleted) = server
        .systems
        .economy
        .mail
        .write_prepared(tx, snapshot.mails)
        .await?;
    systems += updated + deleted;
    let (items, deleted, containers) = server.items.write_prepared(tx, snapshot.items).await?;
    let (auction_updated, auction_deleted) = server
        .systems
        .economy
        .auction
        .write_prepared(tx, snapshot.auctions)
        .await?;
    systems += auction_updated + auction_deleted;
    let schedule_items =
        crate::systems::npcs::schedule_item_manager::ScheduleItemManager::write_prepared(
            tx,
            snapshot.schedule_items,
        )
        .await?;
    systems += schedule_items.written();
    systems +=
        crate::systems::save_prepared_financial_stage(server, tx, SaveStage::AfterItems).await?;
    let mut characters = Vec::new();
    for (player, mut character) in snapshot.characters {
        let removed = [
            character.removed_friends.clone(),
            character.removed_blocked.clone(),
            character.skills.removed.clone(),
            character.quests.removed.clone(),
        ];
        if let Err(error) = save_character(tx, &mut character, Utc::now().timestamp()).await {
            tracing::error!(
                "Failed to save character {} - {}: {error}",
                character.id,
                character.unit.name
            );
            return Err(error);
        }
        characters.push(CharacterSaveAck {
            player,
            updated: character.updated,
            removed,
            money: (character.money, character.money2),
        });
    }
    systems += save_stage(server, tx, SaveStage::AfterCharacters).await?;
    Ok(FinancialSaveReceipt {
        counts: (items, deleted, containers, characters.len(), systems),
        schedule_items,
        characters,
    })
}

pub(crate) fn commit_financial_snapshot(server: &GameServer, receipt: FinancialSaveReceipt) {
    crate::systems::housing::manager::commit_save(server);
    server.systems.housing.dominions.commit_save();
    server.systems.economy.mail.commit_save();
    server.systems.economy.auction.commit_save();
    server.items.commit_save();
    server
        .systems
        .npcs
        .schedule_items
        .commit_save(receipt.schedule_items);
    for ack in receipt.characters {
        let mut live = ack.player.data();
        let unchanged_money = (live.money, live.money2) == ack.money;
        live.updated = ack.updated;
        let [friends, blocked, skills, quests] = ack.removed;
        live.removed_friends.retain(|id| !friends.contains(id));
        live.removed_blocked.retain(|id| !blocked.contains(id));
        live.skills.removed.retain(|id| !skills.contains(id));
        live.quests.removed.retain(|id| !quests.contains(id));
        drop(live);
        if unchanged_money {
            let mut departing = server
                .save
                .departing
                .lock()
                .expect("departure saves poisoned");
            if departing
                .get(&ack.player.character_id)
                .is_some_and(|p| Arc::ptr_eq(p, &ack.player))
            {
                departing.remove(&ack.player.character_id);
                drop(departing);
                if server
                    .world
                    .character_by_obj_id(ack.player.obj_id)
                    .is_some_and(|p| Arc::ptr_eq(&p, &ack.player))
                {
                    server.world.try_remove_character(ack.player.obj_id);
                }
            }
        }
    }
}

#[derive(Debug, Default)]
pub struct SaveManager {
    /// `_isSaving`.
    saving: AtomicBool,
    /// `_enabled`.
    enabled: AtomicBool,
    /// Prevent a late world snapshot from overwriting a disconnect's newer character save.
    transactions: tokio::sync::Mutex<()>,
    /// Keep departed wallets available for a full transaction retry after database failure.
    departing: Mutex<HashMap<u32, PlayerRef>>,
}

impl SaveManager {
    pub(crate) fn pending_departure(&self, character_id: u32) -> Option<PlayerRef> {
        self.departing
            .lock()
            .expect("departure saves poisoned")
            .get(&character_id)
            .cloned()
    }

    pub(crate) fn remember_departing(&self, player: &PlayerRef) {
        self.departing
            .lock()
            .expect("departure saves poisoned")
            .insert(player.character_id, player.clone());
    }

    pub(crate) async fn transaction_lock(&self) -> tokio::sync::MutexGuard<'_, ()> {
        self.transactions.lock().await
    }

    /// Caller owns transaction_lock. A logout writes the wallet with its property, mail,
    /// certificate and treasury state, rather than committing the wallet alone.
    pub(crate) async fn save_departing_locked(
        &self,
        server: &GameServer,
        player: &PlayerRef,
    ) -> bool {
        self.remember_departing(player);
        let result: crate::Result<()> = async {
            let snapshot = capture_financial_snapshot(server);
            let mut tx = server.db.begin().await?;
            let receipt = write_financial_snapshot(server, &mut tx, snapshot).await?;
            tx.commit().await?;
            commit_financial_snapshot(server, receipt);
            Ok(())
        }
        .await;
        if let Err(error) = result {
            tracing::error!(
                "Departure financial save failed for {}: {error}",
                player.character_id
            );
            return false;
        }
        true
    }

    /// `DoSave()`. Returns `true` when the data was committed (or there was nothing to save).
    pub async fn do_save(&self, server: &GameServer) -> bool {
        if self.saving.swap(true, Ordering::SeqCst) {
            return false;
        }
        let _transaction = self.transaction_lock().await;
        let started = std::time::Instant::now();
        tracing::debug!("Saving DB ...");
        let res: crate::Result<bool> = async {
            let snapshot = capture_financial_snapshot(server);
            let mut tx = server.db.begin().await?;
            let receipt = write_financial_snapshot(server, &mut tx, snapshot).await?;
            let (items, deleted, containers, characters, systems) = receipt.counts;
            let total = items + deleted + containers + characters + systems;
            if total == 0 {
                tracing::debug!("No data to update ...");
            }
            tx.commit().await?;
            commit_financial_snapshot(server, receipt);
            if items + deleted > 0 {
                tracing::debug!(
                    "Updated {items} and deleted {deleted} items in {containers} containers ..."
                );
            }
            if containers > 0 {
                tracing::debug!("Updated {containers} item containers ...");
            }
            if characters > 0 {
                tracing::debug!("Updated {characters} characters ...");
            }
            Ok(true)
        }
        .await;
        let saved = match res {
            Ok(v) => v,
            Err(e) => {
                tracing::error!("DoSave Exception: {e}");
                false
            }
        };
        tracing::debug!("Saving data took {:?}", started.elapsed());
        self.saving.store(false, Ordering::SeqCst);
        saved
    }

    /// `Initialize()` + `SaveTickStart()`.
    pub fn start(server: Arc<GameServer>) {
        server.save.enabled.store(true, Ordering::SeqCst);
        let minutes = server.config.world.auto_save_interval.max(0.01);
        tokio::spawn(async move {
            let period = Duration::from_secs_f64(minutes * 60.0);
            let mut iv = tokio::time::interval_at(tokio::time::Instant::now() + period, period);
            loop {
                iv.tick().await;
                if !server.save.enabled.load(Ordering::SeqCst) {
                    tracing::warn!("Auto-Saving disabled, skipping ...");
                    continue;
                }
                server.save.do_save(&server).await;
            }
        });
    }

    /// `StopAsync()`: disable the tick and do one final save.
    pub async fn stop(&self, server: &GameServer) {
        self.enabled.store(false, Ordering::SeqCst);
        // Wait for the active world/logout transaction, then capture the final state.
        // do_save deliberately returns early when busy and cannot serve as this barrier.
        let _transaction = self.transaction_lock().await;
        let result: crate::Result<()> = async {
            let snapshot = capture_financial_snapshot(server);
            let mut tx = server.db.begin().await?;
            let receipt = write_financial_snapshot(server, &mut tx, snapshot).await?;
            tx.commit().await?;
            commit_financial_snapshot(server, receipt);
            Ok(())
        }
        .await;
        if let Err(error) = result {
            tracing::error!("Final financial save failed: {error}");
        }
    }
}
