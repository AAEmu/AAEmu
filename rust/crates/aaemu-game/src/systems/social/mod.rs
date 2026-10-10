//! Phase-3 subsystem: team / expedition / family / friend / duel / crime / trial, chat
//! channels and faction relations.
//!
//! C# scope: `TeamManager` (+ `Models/Game/Team`), `ExpeditionManager` (+ `Models/Game/
//! Expeditions`), `FamilyManager` (+ `Models/Game/Family`), `FriendMananger` (+ the character
//! friend / blocked lists), `DuelManager` (+ `Models/Game/Duels`, duel tasks), `CrimeManager` /
//! `TrialManager` (+ `Models/Game/Crime`, trial tasks), `ChatManager` (+ `Models/Game/Chat`),
//! the relation part of `FactionManager` and `Unit/Character.SetFaction`, and their packets.
//!
//! Module layout (see `CONTRIBUTING-PORT.md`): managers in their own files, packets under
//! `packets/` (one file per area), configuration in `config.rs`, cross-subsystem services in
//! `hooks.rs`, the session call sites in `session.rs`. `managers::{chat, expedition, friend}`
//! re-export the moved phase-2 managers (`server.chat`, `server.expeditions`, `server.friends`).

pub mod achievement;
pub mod bridges;
pub mod chat;
pub mod config;
pub mod crime;
pub mod dominion;
pub mod duel;
pub mod expedition;
pub mod faction;
pub mod family;
pub mod friend;
pub mod hooks;
pub mod packets;
pub mod session;
pub mod team;
pub mod trial;
pub mod util;
pub mod zone_conflict;

#[cfg(test)]
mod achievement_game_data_tests;
#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod manager_tests;
#[cfg(test)]
mod trial_flow_tests;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.social`).
#[derive(Debug, Default)]
pub struct Data {
    /// `SystemFaction.Relations` (`FactionManager.Load`, relation part).
    pub relations: faction::FactionRelations,
    /// `AchievementGameData.Load`.
    pub achievements: achievement::AchievementGameData,
}

impl Data {
    /// Loads this subsystem's tables from `ctx`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self {
            relations: faction::FactionRelations::load(ctx)?,
            achievements: achievement::AchievementGameData::load(ctx.conn)?,
        })
    }
}

/// Managers of this subsystem (`server.systems.social`).
#[derive(Debug, Default)]
pub struct System {
    pub teams: team::TeamManager,
    pub families: family::FamilyManager,
    pub duels: duel::DuelManager,
    pub crimes: crime::CrimeManager,
    pub trials: trial::TrialManager,
    /// `ZoneManager.GetConflicts()` runtime state (`ZoneConflict`).
    pub conflicts: zone_conflict::ZoneConflictManager,
    /// Services other subsystems provide (`hooks::SocialHooks`).
    pub hooks: hooks::SocialHookSlot,
}

impl System {
    /// Startup after the phase-1/2 managers: DB loads, timers, hook installation.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        use anyhow::Context;
        install_hooks(server);
        // ZoneManager.Load: SetState(Conflict) of the open zones schedules their timers.
        zone_conflict::ZoneConflictManager::start(server);
        server
            .systems
            .social
            .families
            .load(&server.db)
            .await
            .context("FamilyManager")?;
        team::TeamManager::start_tick(server);
        // CrimeManager.Load, TrialManager.Load (court rooms + the 5 s TrialUpdateTask).
        server.systems.social.crimes.load(server).await?;
        server.systems.social.trials.load(server);
        trial::TrialManager::start_tick(server);
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (inside its transaction). Returns the
    /// number of rows written.
    pub async fn save(
        &self,
        _server: &GameServer,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        stage: SaveStage,
    ) -> crate::Result<usize> {
        if matches!(stage, SaveStage::AfterItems) {
            // SaveManager.DoSave: `CrimeManager.Instance.Save(connection, transaction)`.
            let (updated, deleted) = self.crimes.save(tx).await?;
            return Ok(updated + deleted);
        }
        Ok(0)
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}

/// Installs the social subsystem's hooks (its implementations of the other subsystems' hooks
/// and its own `SocialHooks`). No I/O; also used by the flow tests.
pub fn install_hooks(server: &GameServer) {
    bridges::install(server);
}
