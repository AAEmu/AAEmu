//! `Core/Managers/SusManager`: the suspicious-activity log (`audit_char_sus`) and the movement
//! speed analysis of players and mounts, run every 5 s of `Transform.FinalizeTransform`
//! ([`SusManager::finalize_transform`], called by the unit movement handler).

use std::collections::HashMap;
use std::sync::Mutex;

use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

/// `CategoryBot`.
pub const CATEGORY_BOT: &str = "Bot";
/// `CategoryBotReport`.
pub const CATEGORY_BOT_REPORT: &str = "BotReport";
/// `CategoryCheating`.
pub const CATEGORY_CHEATING: &str = "Cheat";
/// `CategoryRmt`.
pub const CATEGORY_RMT: &str = "RMT";

/// The flat speed above which a player is reported (m/s).
pub const PLAYER_CHECK_SPEED: f64 = 25.0;
/// The flat speed above which a mount is reported (m/s).
pub const MOUNT_CHECK_SPEED: f64 = 27.5;
/// Seconds a report suppresses further reports.
const SKIP_TIME_AFTER_REPORT: f32 = 15.0;

/// `SusManager`.
#[derive(Debug, Default)]
pub struct SusManager {
    /// `LastPlayerPositions` (player id -> (pos, skipTime)).
    last_player_positions: Mutex<HashMap<u32, ([f32; 3], f32)>>,
    /// `LastPetPositions` (mate id -> (pos, skipTime)).
    last_pet_positions: Mutex<HashMap<u32, ([f32; 3], f32)>>,
    /// `Transform._lastFinalizeTime` (unix ms) / `_skipCheckTime` (s) per object.
    finalize_checks: Mutex<HashMap<u32, (i64, f32)>>,
}

impl SusManager {
    /// The `_skipCheckTime` part of `Transform.FinalizeTransform` for an object whose world
    /// position changed: `true` once every 5 s (the first finalize of an object included).
    pub fn finalize_check(&self, obj_id: u32, now_ms: i64) -> bool {
        let mut m = self.finalize_checks.lock().expect("sus poisoned");
        let e = m.entry(obj_id).or_insert((now_ms, 0.0));
        let delta = (now_ms - e.0) as f32 / 1000.0;
        e.0 = now_ms;
        e.1 -= delta;
        if e.1 <= 0.0 {
            e.1 = 5.0;
            true
        } else {
            false
        }
    }

    /// Forgets an object's `FinalizeTransform` timer (the object left the world).
    pub fn forget_object(&self, obj_id: u32) {
        self.finalize_checks
            .lock()
            .expect("sus poisoned")
            .remove(&obj_id);
    }

    /// `Transform.FinalizeTransform` every 5 s: `AnalyzePlayerDeltaMovement(player, 5f)` for a
    /// character, `AnalyzeMountDeltaMovement(mount, 5f)` for a mate (the database log runs on
    /// a task).
    pub fn finalize_transform(&self, server: &GameServer, obj: &WorldObject) {
        if !matches!(obj, WorldObject::Player(_) | WorldObject::Mate(_)) {
            return;
        }
        if !self.finalize_check(obj.obj_id(), crate::world::now_ms()) {
            return;
        }
        let (Some(arc), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
            return;
        };
        let obj = obj.clone();
        rt.spawn(async move {
            let sus = &arc.systems.services.sus;
            match &obj {
                WorldObject::Player(p) => {
                    let riding = p.data().is_riding;
                    sus.analyze_player(&arc, p, riding, 5.0).await;
                }
                WorldObject::Mate(m) => sus.analyze_mount(&arc, m, 5.0).await,
                _ => {}
            }
        });
    }

    /// `AnalyzeMountDeltaMovement(pet, deltaTime)` including the `audit_char_sus` entry of the
    /// pet's owner.
    pub async fn analyze_mount(
        &self,
        server: &GameServer,
        pet: &crate::world::MateRef,
        delta_time: f32,
    ) {
        let obj = WorldObject::from(pet);
        let pos = match server.world.world_of(&obj) {
            Some(w) => {
                let p = w.world_pos_rot(&obj);
                [p.x, p.y, p.z]
            }
            None => {
                let t = obj.transform();
                [t.x, t.y, t.z]
            }
        };
        let (id, name, disabled, owner_obj_id) = {
            let m = pet.data();
            (m.id, m.unit.name.clone(), false, m.owner_obj_id)
        };
        if let Some(speed) = self.analyze_mount_delta_movement(id, pos, disabled, delta_time) {
            let owner = server.world.character_by_obj_id(owner_obj_id);
            let owner_name = owner.as_ref().map(|o| o.data().unit.name.clone());
            let text = Self::mount_report(&name, owner_name.as_deref(), speed);
            self.log_activity_player(server, CATEGORY_CHEATING, owner.as_ref(), &text)
                .await;
        }
    }

    /// `LogActivity(category, accountId, playerId, zoneGroup, position, description)`.
    #[allow(clippy::too_many_arguments)]
    pub async fn log_activity(
        &self,
        server: &GameServer,
        category: &str,
        account_id: u32,
        player_id: u32,
        zone_group: u32,
        position: [f32; 3],
        description: &str,
    ) -> bool {
        let res = sqlx::query(
            "INSERT INTO audit_char_sus (sus_category, sus_account, sus_character, zone_group, x, y, z, description) \
             VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(category)
        .bind(account_id)
        .bind(player_id)
        .bind(zone_group)
        .bind(position[0])
        .bind(position[1])
        .bind(position[2])
        .bind(description)
        .execute(&server.db)
        .await;
        match res {
            Ok(r) if r.rows_affected() > 0 => {
                tracing::warn!("{description}");
                true
            }
            Ok(_) => {
                tracing::error!("Saving suspisious activity failed: {category} - {description}");
                false
            }
            Err(e) => {
                tracing::error!(
                    "Saving suspisious activity failed: {category} - {description} - {e}"
                );
                false
            }
        }
    }

    /// `LogActivity(category, player, description)` (the C# passes the player's `ZoneId` as the
    /// zone group).
    pub async fn log_activity_player(
        &self,
        server: &GameServer,
        category: &str,
        player: Option<&PlayerRef>,
        description: &str,
    ) -> bool {
        let (account, id, zone, pos) = match player {
            Some(p) => {
                let pos = world_position(server, p);
                let c = p.data();
                (c.account_id, c.id, c.unit.transform.zone_id, pos)
            }
            None => (0, 0, 0, [0.0; 3]),
        };
        self.log_activity(server, category, account, id, zone, pos, description)
            .await
    }

    /// `LogActivity(category, description)`.
    pub async fn log_activity_text(
        &self,
        server: &GameServer,
        category: &str,
        description: &str,
    ) -> bool {
        self.log_activity(server, category, 0, 0, 0, [0.0; 3], description)
            .await
    }

    /// The speed check of `AnalyzePlayerDeltaMovement` / `AnalyzeMountDeltaMovement`: records
    /// the position and returns the observed flat speed when it exceeds `max_speed`.
    fn analyze(
        positions: &Mutex<HashMap<u32, ([f32; 3], f32)>>,
        id: u32,
        pos: [f32; 3],
        can_check: bool,
        max_speed: f64,
        delta_time: f32,
    ) -> Option<f64> {
        let mut map = positions.lock().expect("sus poisoned");
        let Some(&(last_pos, mut skip_time)) = map.get(&id) else {
            map.insert(id, (pos, 0.0));
            return None;
        };
        let (dx, dy) = (pos[0] - last_pos[0], pos[1] - last_pos[1]);
        let mut flagged = None;
        if can_check && (dx != 0.0 || dy != 0.0) && skip_time <= 0.0 {
            let observed = f64::from((dx * dx + dy * dy).sqrt() / delta_time);
            if observed > max_speed {
                skip_time = SKIP_TIME_AFTER_REPORT;
                flagged = Some(observed);
            }
        }
        skip_time -= delta_time;
        map.insert(id, (pos, skip_time));
        flagged
    }

    /// `AnalyzePlayerDeltaMovement(player, deltaTime)` (the analysis part): `Some(speed)` when
    /// the player is reported.
    pub fn analyze_player_delta_movement(
        &self,
        player_id: u32,
        world_pos: [f32; 3],
        is_riding: bool,
        disabled_set_position: bool,
        delta_time: f32,
    ) -> Option<f64> {
        Self::analyze(
            &self.last_player_positions,
            player_id,
            world_pos,
            !is_riding && !disabled_set_position,
            PLAYER_CHECK_SPEED,
            delta_time,
        )
    }

    /// `AnalyzePlayerDeltaMovement` including the `audit_char_sus` entry.
    pub async fn analyze_player(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        is_riding: bool,
        delta_time: f32,
    ) {
        let pos = world_position(server, player);
        let (id, name, disabled) = {
            let c = player.data();
            (c.id, c.unit.name.clone(), c.disabled_set_position)
        };
        if let Some(speed) =
            self.analyze_player_delta_movement(id, pos, is_riding, disabled, delta_time)
        {
            self.log_activity_player(
                server,
                CATEGORY_CHEATING,
                Some(player),
                &format!(
                    "Player {name} seems to be moving a bit fast {speed:.1} m/s (max {PLAYER_CHECK_SPEED:.1})"
                ),
            )
            .await;
        }
    }

    /// `ResetAnalyzePlayerDeltaMovement(playerId)`.
    pub fn reset_analyze_player_delta_movement(&self, player_id: u32) {
        self.last_player_positions
            .lock()
            .expect("sus poisoned")
            .remove(&player_id);
    }

    /// `AnalyzeMountDeltaMovement(pet, deltaTime)` (the analysis part).
    pub fn analyze_mount_delta_movement(
        &self,
        pet_id: u32,
        world_pos: [f32; 3],
        disabled_set_position: bool,
        delta_time: f32,
    ) -> Option<f64> {
        Self::analyze(
            &self.last_pet_positions,
            pet_id,
            world_pos,
            !disabled_set_position,
            MOUNT_CHECK_SPEED,
            delta_time,
        )
    }

    /// The report text of a mount (`Pet {name} from {owner} seems to be moving ...`).
    pub fn mount_report(pet_name: &str, owner_name: Option<&str>, speed: f64) -> String {
        format!(
            "Pet {pet_name} from {} seems to be moving a bit fast {speed:.1} m/s (max {MOUNT_CHECK_SPEED:.1})",
            owner_name.unwrap_or("")
        )
    }

    /// `ResetAnalyzeMountDeltaMovement(petId)`.
    pub fn reset_analyze_mount_delta_movement(&self, pet_id: u32) {
        self.last_pet_positions
            .lock()
            .expect("sus poisoned")
            .remove(&pet_id);
    }
}

/// `player.Transform.World.Position`.
fn world_position(server: &GameServer, player: &PlayerRef) -> [f32; 3] {
    let obj = WorldObject::from(player);
    match server.world.world_of(&obj) {
        Some(w) => {
            let p = w.world_pos_rot(&obj);
            [p.x, p.y, p.z]
        }
        None => {
            let c = player.data();
            [c.unit.transform.x, c.unit.transform.y, c.unit.transform.z]
        }
    }
}

#[cfg(test)]
mod tests {

    #[test]
    fn finalize_check_runs_every_five_seconds() {
        let s = SusManager::default();
        assert!(s.finalize_check(7, 1_000)); // first finalize
        assert!(!s.finalize_check(7, 3_000));
        assert!(!s.finalize_check(7, 5_999));
        assert!(s.finalize_check(7, 6_000));
        assert!(!s.finalize_check(7, 6_100));
        s.forget_object(7);
        assert!(s.finalize_check(7, 6_200));
    }

    use super::*;

    #[test]
    fn speed_analysis() {
        let s = SusManager::default();
        // First call only records.
        assert_eq!(
            s.analyze_player_delta_movement(1, [0.0; 3], false, false, 5.0),
            None
        );
        // 100 m in 5 s = 20 m/s: fine.
        assert_eq!(
            s.analyze_player_delta_movement(1, [100.0, 0.0, 0.0], false, false, 5.0),
            None
        );
        // 200 m in 5 s = 40 m/s (Z ignored): reported once, then 15 s skip.
        let r = s.analyze_player_delta_movement(1, [300.0, 0.0, 500.0], false, false, 5.0);
        assert_eq!(r, Some(40.0));
        assert_eq!(
            s.analyze_player_delta_movement(1, [600.0, 0.0, 0.0], false, false, 5.0),
            None
        );
        // Riding players are not checked.
        s.reset_analyze_player_delta_movement(2);
        s.analyze_player_delta_movement(2, [0.0; 3], true, false, 5.0);
        assert_eq!(
            s.analyze_player_delta_movement(2, [1000.0, 0.0, 0.0], true, false, 5.0),
            None
        );
        // Mounts: 27.5 m/s threshold.
        s.analyze_mount_delta_movement(7, [0.0; 3], false, 5.0);
        assert_eq!(
            s.analyze_mount_delta_movement(7, [137.0, 0.0, 0.0], false, 5.0),
            None
        );
        assert!(s
            .analyze_mount_delta_movement(7, [300.0, 0.0, 0.0], false, 5.0)
            .is_some());
        assert_eq!(
            SusManager::mount_report("Horse", None, 32.6),
            "Pet Horse from  seems to be moving a bit fast 32.6 m/s (max 27.5)"
        );
    }
}
