//! `Core/Managers/SaveManager`: the periodic save (`AutoSaveInterval` minutes) of items and
//! online characters in one transaction, and the final save on shutdown. The subsystems' rows
//! (houses, mails, auction, crimes, slaves) are written by `systems::save_stage` at the C#
//! positions of the transaction.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use chrono::Utc;

use crate::db_character::save_character;
use crate::server::GameServer;
use crate::systems::{save_stage, SaveStage};

#[derive(Debug, Default)]
pub struct SaveManager {
    /// `_isSaving`.
    saving: AtomicBool,
    /// `_enabled`.
    enabled: AtomicBool,
}

impl SaveManager {
    /// `DoSave()`. Returns `true` when the data was committed (or there was nothing to save).
    pub async fn do_save(&self, server: &GameServer) -> bool {
        if self.saving.swap(true, Ordering::SeqCst) {
            return false;
        }
        let started = std::time::Instant::now();
        tracing::debug!("Saving DB ...");
        let res: crate::Result<bool> = async {
            let mut tx = server.db.begin().await?;
            let mut systems = save_stage(server, &mut tx, SaveStage::BeforeItems).await?;
            let (items, deleted, containers) = server.items.save(&mut tx).await?;
            systems += save_stage(server, &mut tx, SaveStage::AfterItems).await?;
            let mut characters = 0;
            let mut saved_characters = Vec::new();
            for p in server.world.all_characters() {
                let mut c = p.data().clone();
                let removed = (
                    c.removed_friends.clone(),
                    c.removed_blocked.clone(),
                    c.skills.removed.clone(),
                    c.quests.removed.clone(),
                );
                match save_character(&mut tx, &mut c, Utc::now().timestamp()).await {
                    Ok(()) => {
                        characters += 1;
                        saved_characters.push((p, c.updated, removed));
                    }
                    Err(e) => {
                        tracing::error!("{e}");
                        tracing::error!(
                            "Failed to get save data for character {} - {}",
                            c.id,
                            c.unit.name
                        );
                        return Err(e);
                    }
                }
            }
            systems += save_stage(server, &mut tx, SaveStage::AfterCharacters).await?;
            let total = items + deleted + containers + characters + systems;
            if total == 0 {
                tracing::debug!("No data to update ...");
            }
            tx.commit().await?;
            crate::systems::housing::manager::commit_save(server);
            server.systems.economy.mail.commit_save();
            server.items.commit_save();
            for (player, updated, (friends, blocked, skills, quests)) in saved_characters {
                let mut live = player.data();
                live.updated = updated;
                live.removed_friends.retain(|id| !friends.contains(id));
                live.removed_blocked.retain(|id| !blocked.contains(id));
                live.skills.removed.retain(|id| !skills.contains(id));
                live.quests.removed.retain(|id| !quests.contains(id));
            }
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
        self.do_save(server).await;
    }
}
