//! `Models/Game/Configurations.cs` `DungeonsConfig` / `DungeonLoadConfig`: the `Dungeons`
//! configuration section (`Configurations/Dungeons.json`), bound into
//! [`crate::config::AppConfig`] (keys are lower-cased by the loader).

use serde::Deserialize;

use crate::config::lenient;

/// Opt-in schedules for tower defense events. Times and day intervals come from compact.
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct TowerDefenseConfig {
    #[serde(rename = "enabled", deserialize_with = "lenient")]
    pub enabled: bool,
    #[serde(rename = "events")]
    pub events: Vec<TowerDefenseEventConfig>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct TowerDefenseEventConfig {
    #[serde(rename = "id", deserialize_with = "lenient")]
    pub id: u32,
    /// World instance id, not the client world template id.
    #[serde(rename = "worldid", deserialize_with = "lenient")]
    pub world_id: u32,
    #[serde(rename = "zonegroupid", deserialize_with = "lenient")]
    pub zone_group_id: u16,
    /// Zone key used by the 1.2 event packets.
    #[serde(rename = "zoneid", deserialize_with = "lenient")]
    pub zone_id: u32,
}

/// `DungeonLoadConfig` (one `AutoCreate` entry).
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct DungeonLoadConfig {
    #[serde(rename = "name")]
    pub name: String,
    #[serde(rename = "channel", deserialize_with = "lenient")]
    pub channel: u32,
    #[serde(rename = "id", deserialize_with = "lenient")]
    pub id: u32,
}

/// `DungeonsConfig`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct DungeonsConfig {
    #[serde(rename = "towerdefense")]
    pub tower_defense: TowerDefenseConfig,
    /// `AutoCleanupAfterKick`: remove a dungeon nobody is left in after a kick (QoL, not retail).
    #[serde(rename = "autocleanupafterkick", deserialize_with = "lenient")]
    pub auto_cleanup_after_kick: bool,
    /// `AutoTeamDisbandKickTime`: seconds after leaving the party before being kicked out.
    #[serde(rename = "autoteamdisbandkicktime", deserialize_with = "lenient")]
    pub auto_team_disband_kick_time: i32,
    /// `AutoCreate`: dungeon instances created at startup.
    #[serde(rename = "autocreate")]
    pub auto_create: Vec<DungeonLoadConfig>,
    /// `KickOnBattlefieldFactionChange` (Rust-only switch, default `true` = C# behaviour):
    /// entering a battlefield sets the corps faction, and `Character.SetFaction` then kicks a
    /// guild member whose guild's mother faction differs out of the guild and the party.
    /// `false` keeps guild and party membership through battlefield faction changes.
    #[serde(
        rename = "kickonbattlefieldfactionchange",
        deserialize_with = "lenient"
    )]
    pub kick_on_battlefield_faction_change: bool,
}

impl Default for DungeonsConfig {
    fn default() -> Self {
        Self {
            tower_defense: TowerDefenseConfig::default(),
            auto_cleanup_after_kick: true,
            auto_team_disband_kick_time: 30,
            auto_create: Vec::new(),
            kick_on_battlefield_faction_change: true,
        }
    }
}

#[cfg(test)]
mod tests {
    use crate::config::AppConfig;

    #[test]
    fn dungeons_section_binds() {
        let v = serde_json::json!({
            "Dungeons": {
                "AutoCleanupAfterKick": false,
                "AutoTeamDisbandKickTime": 15,
                "KickOnBattlefieldFactionChange": false,
                "AutoCreate": [ { "Id": 1, "Name": "arche_mall_world", "Channel": 0 } ]
            },
            "World": { "MaxInstances": 7 }
        });
        let c = AppConfig::from_value(v).unwrap();
        assert!(!c.dungeons.auto_cleanup_after_kick);
        assert_eq!(c.dungeons.auto_team_disband_kick_time, 15);
        assert!(!c.dungeons.kick_on_battlefield_faction_change);
        assert!(
            AppConfig::default()
                .dungeons
                .kick_on_battlefield_faction_change
        );
        assert_eq!(c.dungeons.auto_create.len(), 1);
        assert_eq!(c.dungeons.auto_create[0].name, "arche_mall_world");
        assert_eq!(c.dungeons.auto_create[0].id, 1);
        assert_eq!(c.world.max_instances, 7);
        let d = AppConfig::default();
        assert!(d.dungeons.auto_cleanup_after_kick);
        assert_eq!(d.dungeons.auto_team_disband_kick_time, 30);
        assert_eq!(d.world.max_instances, 32);
    }

    #[test]
    fn tower_defense_schedules_bind_and_default_to_disabled() {
        let config = AppConfig::from_value(serde_json::json!({"Dungeons": {"TowerDefense": {
            "Enabled": "true", "Events": [{"Id": "12", "WorldId": 0, "ZoneGroupId": 5, "ZoneId": 100}]
        }}})).unwrap();
        assert!(config.dungeons.tower_defense.enabled);
        assert_eq!(config.dungeons.tower_defense.events[0].id, 12);
        assert_eq!(config.dungeons.tower_defense.events[0].zone_group_id, 5);
        assert!(!AppConfig::default().dungeons.tower_defense.enabled);
    }
}
