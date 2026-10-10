//! `Core/Managers/World/FactionManager` relations + `Models/Game/Faction/SystemFaction`
//! (`GetRelationState`, `BaseUnit.GetRelationStateTo`) and the player side of
//! `Unit.SetFaction` / `Character.SetFaction`. The faction list itself (`SendFactions`,
//! `SendRelations`) is loaded by `data::world_data::FactionData`.

use std::collections::HashMap;

use crate::data::world_data::{factions, FactionData};
use crate::data::GameDataError;
use crate::server::GameServer;
use crate::systems::social::packets::faction::SCUnitFactionChangedPacket;
use crate::systems::DataLoadCtx;
use crate::world::{broadcast, PlayerRef};

/// `RelationState`.
pub mod relation_state {
    pub const HOSTILE: u8 = 1;
    pub const NEUTRAL: u8 = 2;
    pub const FRIENDLY: u8 = 3;
}

/// `BuffConstants.Contemptuous`.
pub const BUFF_CONTEMPTUOUS: u32 = 4832;

/// `SystemFaction.Relations` of every faction (`faction -> other -> state`), built like
/// `FactionManager.Load` (each `system_faction_relations` row is added to both factions;
/// `Dictionary.Add` throws on a repeated pair).
#[derive(Debug, Clone, Default)]
pub struct FactionRelations {
    relations: HashMap<u32, HashMap<u32, u8>>,
}

impl FactionRelations {
    /// The relation part of `FactionManager.Load` (`system_faction_relations`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut rows = Vec::new();
        let mut stmt = ctx
            .conn
            .prepare("SELECT faction1_id, faction2_id, state_id FROM system_faction_relations")?;
        let mut q = stmt.query([])?;
        while let Some(r) = q.next()? {
            let a: i64 = r.get(0)?;
            let b: i64 = r.get(1)?;
            let s: i64 = r.get(2)?;
            rows.push((a as u32, b as u32, s as u8));
        }
        Self::from_rows(&rows)
            .map_err(|e| GameDataError::File(format!("system_faction_relations: {e}")))
    }

    /// Builds the table from `(faction1, faction2, state)` rows.
    pub fn from_rows(rows: &[(u32, u32, u8)]) -> Result<Self, String> {
        let mut t = Self::default();
        for &(a, b, s) in rows {
            for (x, y) in [(a, b), (b, a)] {
                if t.relations.entry(x).or_default().insert(y, s).is_some() {
                    return Err(format!("duplicate relation {x} -> {y}"));
                }
            }
        }
        Ok(t)
    }

    /// `faction.Relations.TryGetValue(other)`.
    pub fn get(&self, faction: u32, other: u32) -> Option<u8> {
        self.relations.get(&faction)?.get(&other).copied()
    }

    pub fn len(&self) -> usize {
        self.relations.values().map(HashMap::len).sum::<usize>() / 2
    }

    pub fn is_empty(&self) -> bool {
        self.relations.is_empty()
    }
}

/// `SystemFaction.GetRelationState(otherFaction)` for two faction ids (`None` = unknown faction,
/// i.e. a null `Faction`).
pub fn relation_state(
    factions_data: &FactionData,
    relations: &FactionRelations,
    me: u32,
    other: u32,
) -> u8 {
    let Some(me) = factions_data.get(me) else {
        // `this.Faction?.GetRelationState(..) ?? Neutral`.
        return relation_state::NEUTRAL;
    };
    let Some(other) = factions_data.get(other) else {
        return relation_state::NEUTRAL;
    };
    let faction_id = if me.mother_id != factions::INVALID {
        me.mother_id
    } else {
        me.id
    };
    let other_id = if other.mother_id != 0 {
        other.mother_id
    } else {
        other.id
    };
    for id in [faction_id, other_id] {
        match id {
            factions::NEUTRAL => return relation_state::NEUTRAL,
            factions::FRIENDLY => return relation_state::FRIENDLY,
            factions::HOSTILE => return relation_state::HOSTILE,
            _ => {}
        }
    }
    if faction_id == other_id {
        return relation_state::FRIENDLY;
    }
    if me.mother_id != 0 && factions_data.get(me.mother_id).is_some() {
        // TODO (C#): not found, so enemy (id = [1, 2, 3])?
        return relations
            .get(me.mother_id, other_id)
            .unwrap_or(relation_state::FRIENDLY);
    }
    relations
        .get(me.id, other_id)
        .unwrap_or(relation_state::NEUTRAL)
}

/// `BaseUnit.GetRelationStateTo(unit)` for two players.
pub fn player_relation(server: &GameServer, me: &PlayerRef, other: &PlayerRef) -> u8 {
    let a = me.data().unit.faction_id;
    let b = other.data().unit.faction_id;
    relation_state(
        &server.game_data.factions,
        &server.game_data.systems.social.relations,
        a,
        b,
    )
}

/// `Character.SetFaction(factionId)` (+ `Unit.SetFaction`): broadcasts
/// `SCUnitFactionChangedPacket`, adds / removes the Contemptuous buff, updates owned houses,
/// kicks the character from a guild of another mother faction (`ExpeditionManager.Kick` with
/// its own connection) and from its party.
pub async fn set_faction(server: &GameServer, p: &PlayerRef, faction_id: u32) {
    let (old, name, inst, exp) = {
        let c = p.data();
        (
            c.unit.faction_id,
            c.unit.name.clone(),
            c.unit.transform.instance_id,
            c.unit.expedition_id,
        )
    };
    // Character.SetFaction: `if (Faction.Id == factionId) return;`
    if old == faction_id {
        return;
    }
    // Unit.SetFaction
    {
        let now = crate::systems::social::util::now_ms();
        let mut c = p.data();
        c.unit.origin_faction_id = old;
        c.origin_faction_name = c.faction_name.clone();
        tracing::info!(
            "SetFaction: npc={}:{}, factionId={faction_id}",
            c.unit.template_id,
            p.obj_id
        );
        c.unit.faction_id = faction_id;
        if faction_id == factions::PIRATE {
            if !c.unit.buffs.check_buff(BUFF_CONTEMPTUOUS) {
                crate::db_character::add_buff_by_id(server, &mut c, BUFF_CONTEMPTUOUS, now);
            }
        } else {
            c.unit.buffs.remove_buff(BUFF_CONTEMPTUOUS);
        }
    }
    if let Some(w) = server.world.world(inst) {
        broadcast(
            &w,
            p,
            &SCUnitFactionChangedPacket {
                obj_id: p.obj_id,
                unit_name: name,
                id: old,
                id2: faction_id,
                temp: false,
            },
            true,
        );
    }
    crate::systems::dispatch_buff_events(server, &crate::world::WorldObject::from(p));
    // Character.SetFaction: houses, guild, party.
    server
        .systems
        .social
        .hooks
        .get()
        .update_owned_housing_faction(server, p.character_id, faction_id);
    if exp != 0 {
        if let Some(e) = server.expeditions.get(exp) {
            if e.faction.mother_id != faction_id {
                crate::systems::social::expedition::kick(server, p, p.character_id).await;
            }
        }
    }
    if p.data().in_party {
        server.systems.social.teams.member_remove_from_team(
            server,
            p,
            p,
            crate::systems::social::team::risky_action::KICK,
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::world_data::SystemFaction;

    fn data() -> (FactionData, FactionRelations) {
        let mut d = FactionData::default();
        for (id, mother) in [
            (1, 0),
            (2, 0),
            (3, 0),
            (101, 148),
            (102, 149),
            (148, 0),
            (149, 0),
            (161, 0),
            (200, 2),
            (300, 0),
        ] {
            d.add(SystemFaction {
                id,
                mother_id: mother,
                ..SystemFaction::default()
            });
        }
        let r =
            FactionRelations::from_rows(&[(148, 149, 1), (148, 161, 1), (300, 148, 3)]).unwrap();
        (d, r)
    }

    #[test]
    fn relation_states_follow_the_csharp_rules() {
        let (d, r) = data();
        use relation_state::*;
        // Same mother faction.
        assert_eq!(relation_state(&d, &r, 101, 148), FRIENDLY);
        // Mother relation table (both sides added).
        assert_eq!(relation_state(&d, &r, 101, 102), HOSTILE);
        assert_eq!(relation_state(&d, &r, 102, 101), HOSTILE);
        // Mother faction without an entry -> Friendly.
        assert_eq!(relation_state(&d, &r, 101, 300), FRIENDLY);
        // Own relations without a mother.
        assert_eq!(relation_state(&d, &r, 300, 101), FRIENDLY);
        assert_eq!(relation_state(&d, &r, 300, 161), NEUTRAL);
        // Root factions.
        assert_eq!(relation_state(&d, &r, 200, 148), NEUTRAL);
        assert_eq!(relation_state(&d, &r, 148, 3), HOSTILE);
        assert_eq!(relation_state(&d, &r, 1, 149), FRIENDLY);
        // Unknown factions.
        assert_eq!(relation_state(&d, &r, 999, 148), NEUTRAL);
        assert_eq!(relation_state(&d, &r, 148, 999), NEUTRAL);
    }

    #[test]
    fn duplicate_relations_are_rejected() {
        assert!(FactionRelations::from_rows(&[(1, 2, 1), (2, 1, 1)]).is_err());
        let r = FactionRelations::from_rows(&[(1, 2, 1), (1, 3, 2)]).unwrap();
        assert_eq!(r.len(), 2);
        assert_eq!(r.get(3, 1), Some(2));
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_social_relations() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = rusqlite::Connection::open(path).expect("open compact db");
        let files = crate::data::client_files::ClientFileManager::from_sources(
            &[],
            std::path::Path::new("."),
        );
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let r = FactionRelations::load(&ctx).expect("relations");
        assert!(!r.is_empty());
        // Nuia and Haranya are hostile in 1.2.
        assert_eq!(r.get(148, 149), Some(relation_state::HOSTILE));
    }
}
