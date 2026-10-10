//! `Core/Managers/SusManager`: the suspicious-activity log (`audit_char_sus`) and the movement
//! speed analysis of players and mounts (run every 5 s by `Transform.FinalizeTransform`, whose
//! batching is not ported yet: callers pass the world position and flags).

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
}

impl SusManager {
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
