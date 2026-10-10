//! `Models/Game/Expeditions/ExpeditionConfig` and `Models/Game/JusticeConfig`: the `Expedition`
//! and `Justice` configuration sections (`Configurations/Expedition.json`, `Justice.json`),
//! bound into [`crate::config::AppConfig`] (keys are lower-cased by the loader).
//!
//! Note: the C# classes have no defaults for `Expedition` (a missing section makes
//! `ExpeditionManager.Load` throw on `new Regex(null)`); the Rust defaults are the values of the
//! shipped `Expedition.json` so tests and minimal configurations work.

use serde::Deserialize;

use crate::config::lenient;
use crate::systems::social::expedition::ExpeditionRolePolicy;

/// `WorldSpawnPosition` (the members the configuration uses).
#[derive(Debug, Clone, Copy, Default, PartialEq, Deserialize)]
#[serde(default)]
pub struct SpawnPosition {
    #[serde(rename = "worldid", deserialize_with = "lenient")]
    pub world_id: u32,
    #[serde(rename = "zoneid", deserialize_with = "lenient")]
    pub zone_id: u32,
    #[serde(rename = "x", deserialize_with = "lenient")]
    pub x: f32,
    #[serde(rename = "y", deserialize_with = "lenient")]
    pub y: f32,
    #[serde(rename = "z", deserialize_with = "lenient")]
    pub z: f32,
    /// Degrees.
    #[serde(rename = "yaw", deserialize_with = "lenient")]
    pub yaw: f32,
}

/// `ExpeditionConfigCreate`.
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct ExpeditionCreateConfig {
    #[serde(rename = "cost", deserialize_with = "lenient")]
    pub cost: i32,
    #[serde(rename = "level", deserialize_with = "lenient")]
    pub level: u8,
    #[serde(rename = "partymembercount", deserialize_with = "lenient")]
    pub party_member_count: u8,
}

impl Default for ExpeditionCreateConfig {
    fn default() -> Self {
        Self {
            cost: 10000,
            level: 5,
            party_member_count: 5,
        }
    }
}

/// One `RolePolicies` entry (`ExpeditionRolePolicy` without the expedition id).
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct RolePolicyConfig {
    #[serde(rename = "role", deserialize_with = "lenient")]
    pub role: u8,
    #[serde(rename = "name")]
    pub name: String,
    #[serde(rename = "dominiondeclare", deserialize_with = "lenient")]
    pub dominion_declare: bool,
    #[serde(rename = "invite", deserialize_with = "lenient")]
    pub invite: bool,
    #[serde(rename = "expel", deserialize_with = "lenient")]
    pub expel: bool,
    #[serde(rename = "promote", deserialize_with = "lenient")]
    pub promote: bool,
    #[serde(rename = "dismiss", deserialize_with = "lenient")]
    pub dismiss: bool,
    #[serde(rename = "chat", deserialize_with = "lenient")]
    pub chat: bool,
    #[serde(rename = "managerchat", deserialize_with = "lenient")]
    pub manager_chat: bool,
    #[serde(rename = "siegemaster", deserialize_with = "lenient")]
    pub siege_master: bool,
    #[serde(rename = "joinsiege", deserialize_with = "lenient")]
    pub join_siege: bool,
}

impl RolePolicyConfig {
    /// `rolePolicy.Clone()` with `ExpeditionId = expeditionId`.
    pub fn to_policy(&self, expedition_id: u32) -> ExpeditionRolePolicy {
        ExpeditionRolePolicy {
            expedition_id,
            role: self.role,
            name: self.name.clone(),
            dominion_declare: self.dominion_declare,
            invite: self.invite,
            expel: self.expel,
            promote: self.promote,
            dismiss: self.dismiss,
            chat: self.chat,
            manager_chat: self.manager_chat,
            siege_master: self.siege_master,
            join_siege: self.join_siege,
        }
    }
}

/// `ExpeditionConfig`.
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct ExpeditionConfig {
    #[serde(rename = "create")]
    pub create: ExpeditionCreateConfig,
    #[serde(rename = "nameregex")]
    pub name_regex: String,
    #[serde(rename = "rolepolicies")]
    pub role_policies: Vec<RolePolicyConfig>,
}

impl Default for ExpeditionConfig {
    fn default() -> Self {
        let p = |role: u8, name: &str, flags: [bool; 9]| RolePolicyConfig {
            role,
            name: name.into(),
            dominion_declare: flags[0],
            invite: flags[1],
            expel: flags[2],
            promote: flags[3],
            dismiss: flags[4],
            chat: flags[5],
            manager_chat: flags[6],
            siege_master: flags[7],
            join_siege: flags[8],
        };
        let (t, f) = (true, false);
        Self {
            create: ExpeditionCreateConfig::default(),
            name_regex: "^[a-zA-Zа-яА-Я ]{3,32}$".into(),
            role_policies: vec![
                p(0, "Apprentice", [t, f, f, f, f, t, f, f, t]),
                p(1, "Member", [t, f, f, f, f, t, f, f, t]),
                p(2, "Officer", [t, t, f, f, f, t, f, f, t]),
                p(3, "Commander", [t, t, t, f, f, t, f, t, t]),
                p(255, "General", [t, t, t, t, t, t, t, t, t]),
            ],
        }
    }
}

/// `CourtRoomConfig`.
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct CourtRoomConfig {
    #[serde(rename = "id", deserialize_with = "lenient")]
    pub id: u32,
    #[serde(rename = "name")]
    pub name: String,
    /// `FactionsEnum`.
    #[serde(rename = "faction", deserialize_with = "lenient")]
    pub faction: u32,
    #[serde(rename = "defendant")]
    pub defendant: SpawnPosition,
    #[serde(rename = "holdingcell")]
    pub holding_cell: SpawnPosition,
}

impl Default for CourtRoomConfig {
    fn default() -> Self {
        Self {
            id: 0,
            name: "Court Room".into(),
            faction: 0,
            defendant: SpawnPosition::default(),
            holding_cell: SpawnPosition::default(),
        }
    }
}

/// `JailConfig`.
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct JailConfig {
    #[serde(rename = "id", deserialize_with = "lenient")]
    pub id: u32,
    #[serde(rename = "name")]
    pub name: String,
    #[serde(rename = "faction", deserialize_with = "lenient")]
    pub faction: u32,
    #[serde(rename = "pos")]
    pub pos: SpawnPosition,
}

impl Default for JailConfig {
    fn default() -> Self {
        Self {
            id: 0,
            name: "Jail".into(),
            faction: 0,
            pos: SpawnPosition::default(),
        }
    }
}

/// `JusticeConfig` (C# defaults: no court rooms / jails).
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct JusticeConfig {
    #[serde(rename = "keephistory", deserialize_with = "lenient")]
    pub keep_history: bool,
    #[serde(rename = "allowjuryescape", deserialize_with = "lenient")]
    pub allow_jury_escape: bool,
    #[serde(rename = "courtrooms")]
    pub court_rooms: Vec<CourtRoomConfig>,
    #[serde(rename = "jails")]
    pub jails: Vec<JailConfig>,
    #[serde(rename = "botreportprimesuspectcount", deserialize_with = "lenient")]
    pub bot_report_prime_suspect_count: i32,
}

impl Default for JusticeConfig {
    fn default() -> Self {
        Self {
            keep_history: true,
            allow_jury_escape: true,
            court_rooms: Vec::new(),
            jails: Vec::new(),
            bot_report_prime_suspect_count: 5,
        }
    }
}

#[cfg(test)]
mod tests {
    use crate::config::AppConfig;

    #[test]
    fn binds_the_shipped_sections() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
        let mut merged = serde_json::Value::Object(Default::default());
        for f in [
            "Configurations/Expedition.json",
            "Configurations/Justice.json",
        ] {
            let text = std::fs::read_to_string(root.join(f)).expect("config file");
            let v: serde_json::Value = serde_json::from_str(&crate::config::relax_json(
                text.trim_start_matches('\u{feff}'),
            ))
            .expect("json");
            for (k, v) in v.as_object().expect("object") {
                merged
                    .as_object_mut()
                    .expect("object")
                    .insert(k.clone(), v.clone());
            }
        }
        let cfg = AppConfig::from_value(merged).expect("bind");
        let e = &cfg.expedition;
        assert_eq!(
            (e.create.cost, e.create.level, e.create.party_member_count),
            (10000, 5, 5)
        );
        assert_eq!(e.name_regex, "^[a-zA-Zа-яА-Я ]{3,32}$");
        assert_eq!(e.role_policies.len(), 5);
        assert_eq!(e.role_policies[4].role, 255);
        assert!(e.role_policies[3].expel && !e.role_policies[3].promote);
        // The defaults equal the shipped file.
        let d = super::ExpeditionConfig::default();
        for (a, b) in d.role_policies.iter().zip(&e.role_policies) {
            assert_eq!(format!("{a:?}"), format!("{b:?}"));
        }
        let j = &cfg.justice;
        assert_eq!(j.court_rooms.len(), 4);
        assert_eq!(j.court_rooms[2].faction, 149);
        assert_eq!(j.court_rooms[0].defendant.yaw, -28.0);
        assert_eq!(j.court_rooms[3].holding_cell.x, 17020.0);
        assert_eq!(j.jails.len(), 2);
        assert_eq!(j.jails[1].pos.y, 9566.0);
        assert!(j.keep_history && j.allow_jury_escape);
        assert_eq!(j.bot_report_prime_suspect_count, 5);
    }
}
