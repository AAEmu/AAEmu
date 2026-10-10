//! `Configurations/Physics.json` (`"Physics": { … }`): the collision world and the zone-side
//! checks. Every field is optional; the defaults reproduce the zone server's behaviour where
//! it has one (`movement_verify_enable = 0`).

use serde::Deserialize;

use crate::config::{lenient, lenient_enum};

/// What the collision worlds load.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum GeometryLevel {
    /// Terrain heightfield + ocean only.
    Terrain,
    /// + static brushes (`brush.dat`).
    Brushes,
    /// + vegetation (`vegetation.dat`), the zone's full static world.
    #[default]
    Full,
}

impl crate::config::ConfigEnum for GeometryLevel {
    fn parse_config(s: &str) -> Option<Self> {
        match s.to_ascii_lowercase().as_str() {
            "terrain" | "0" => Some(Self::Terrain),
            "brushes" | "1" => Some(Self::Brushes),
            "full" | "2" => Some(Self::Full),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct PhysicsConfig {
    /// The collision query service and the physics worlds (off: every query answers from the
    /// heightmap only, like before).
    #[serde(rename = "enable", deserialize_with = "lenient")]
    pub enable: bool,
    #[serde(rename = "geometry", deserialize_with = "lenient_enum")]
    pub geometry: GeometryLevel,
    /// Cells (1024 m) kept loaded around every player / ship / active NPC: 0 = only the cell
    /// itself, 1 = 3×3 …
    #[serde(rename = "loadradiuscells", deserialize_with = "lenient")]
    pub load_radius_cells: i32,
    /// A query in a cell that is not loaded loads it synchronously (else: terrain only there).
    #[serde(rename = "loadonquery", deserialize_with = "lenient")]
    pub load_on_query: bool,
    /// Loaded cells nobody was near for this long are unloaded (seconds; 0 = never).
    #[serde(rename = "unloadafterseconds", deserialize_with = "lenient")]
    pub unload_after_seconds: u64,
    /// Period of the residency / doodad-collider sync task (milliseconds).
    #[serde(rename = "syncperiodms", deserialize_with = "lenient")]
    pub sync_period_ms: u64,
    /// Doodad / house colliders from their models (`ZdModel*` + `_UpdateCollisionParams`).
    #[serde(rename = "doodadcolliders", deserialize_with = "lenient")]
    pub doodad_colliders: bool,
    /// Load every cell of every world at startup (measurements / tools; slow).
    #[serde(rename = "preloadallcells", deserialize_with = "lenient")]
    pub preload_all_cells: bool,
    /// `movement_verify_enable` (zone CVar, default 0): the ZWReportMoveHack checks.
    #[serde(rename = "movementverifyenable", deserialize_with = "lenient")]
    pub movement_verify_enable: bool,
    /// Zone fall damage cap `min(fallVel, √(40·Δh))` (ZWUnitFell 0x3935C850).
    #[serde(rename = "falldamagezonecap", deserialize_with = "lenient")]
    pub fall_damage_zone_cap: bool,
    /// Ships on the CryPhysics port (`aaemu-physics` rigid bodies) instead of the Jitter2 subset.
    #[serde(rename = "cryphysicsships", deserialize_with = "lenient")]
    pub cryphysics_ships: bool,
}

impl Default for PhysicsConfig {
    fn default() -> Self {
        Self {
            enable: true,
            geometry: GeometryLevel::Full,
            load_radius_cells: 1,
            load_on_query: true,
            unload_after_seconds: 600,
            sync_period_ms: 1000,
            doodad_colliders: true,
            preload_all_cells: false,
            movement_verify_enable: false,
            fall_damage_zone_cap: true,
            cryphysics_ships: true,
        }
    }
}
