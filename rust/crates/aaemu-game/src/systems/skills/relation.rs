//! Faction relations and attack permission: `SystemFaction.GetRelationState`,
//! `BaseUnit.GetRelationStateTo`, `BaseUnit.CanAttack`, `GetOwnerCharacter`,
//! `SkillTargetingUtil.FilterWithRelation` / `IsRelationValid`.

use crate::server::GameServer;
use crate::world::{now_ms, PlayerRef, WorldObject};

use super::geometry::Target;
use super::types::{buff_constants, RelationState, SkillTargetRelation};

/// `FactionsEnum` root factions.
const FACTION_FRIENDLY: u32 = 1;
const FACTION_NEUTRAL: u32 = 2;
const FACTION_HOSTILE: u32 = 3;

/// `WorldManager.DefaultCombatTimeout` (seconds).
pub const DEFAULT_COMBAT_TIMEOUT_MS: i64 = 15_000;

fn relation_from_raw(v: u8) -> RelationState {
    RelationState::from_raw(i64::from(v)).unwrap_or(RelationState::Neutral)
}

/// The stored relation between two factions (`faction.Relations[other]`).
fn stored_relation(server: &GameServer, a: u32, b: u32) -> Option<RelationState> {
    server
        .game_data
        .factions
        .relations
        .iter()
        .find(|r| (r.id == a && r.id2 == b) || (r.id == b && r.id2 == a))
        .map(|r| relation_from_raw(r.state))
}

/// `SystemFaction.GetRelationState(other)` for faction ids (0 / unknown ids = null factions).
pub fn faction_relation(server: &GameServer, own: u32, other: u32) -> RelationState {
    let factions = &server.game_data.factions;
    let Some(me) = factions.get(own) else {
        return RelationState::Neutral; // this.Faction == null -> GetRelationStateTo: Neutral
    };
    let Some(them) = factions.get(other) else {
        return RelationState::Neutral;
    };
    let faction_id = if me.mother_id != 0 {
        me.mother_id
    } else {
        me.id
    };
    let other_id = if them.mother_id != 0 {
        them.mother_id
    } else {
        them.id
    };
    match faction_id {
        FACTION_NEUTRAL => return RelationState::Neutral,
        FACTION_FRIENDLY => return RelationState::Friendly,
        FACTION_HOSTILE => return RelationState::Hostile,
        _ => {}
    }
    match other_id {
        FACTION_NEUTRAL => return RelationState::Neutral,
        FACTION_FRIENDLY => return RelationState::Friendly,
        FACTION_HOSTILE => return RelationState::Hostile,
        _ => {}
    }
    if faction_id == other_id {
        return RelationState::Friendly;
    }
    if me.mother_id != 0 && factions.get(me.mother_id).is_some() {
        return stored_relation(server, me.mother_id, other_id).unwrap_or(RelationState::Friendly);
    }
    stored_relation(server, me.id, other_id).unwrap_or(RelationState::Neutral)
}

/// The faction id of a target (0 for position units / no faction).
pub fn faction_of(t: &Target) -> u32 {
    match t {
        Target::Obj(o) => o.with_base_unit(|b| b.faction_id()),
        Target::Pos(_) => 0,
    }
}

/// `BaseUnit.GetRelationStateTo(unit)`.
pub fn relation_to(server: &GameServer, a: &Target, b: &Target) -> RelationState {
    faction_relation(server, faction_of(a), faction_of(b))
}

/// `GetOwnerCharacter()`: the character itself, the owner of a mate / slave, else `None`.
pub fn owner_character(server: &GameServer, o: &WorldObject) -> Option<PlayerRef> {
    match o {
        WorldObject::Player(p) => Some(p.clone()),
        WorldObject::Mate(m) => {
            let owner = m.data().owner_obj_id;
            (owner > 0)
                .then(|| server.world.object(owner))
                .flatten()
                .and_then(|o| owner_character(server, &o))
        }
        WorldObject::Slave(s) => {
            let (summoner, owner) = {
                let d = s.data();
                (d.summoner_obj_id, d.owner_obj_id)
            };
            let id = if summoner != 0 { summoner } else { owner };
            (id > 0)
                .then(|| server.world.object(id))
                .flatten()
                .and_then(|o| owner_character(server, &o))
        }
        _ => None,
    }
}

/// `Character.IsActivelyHostile(target)`.
pub fn is_actively_hostile(me: &PlayerRef, target_obj_id: u32) -> bool {
    let now = now_ms();
    me.data()
        .skill_char
        .hostile_players
        .iter()
        .find(|(id, _)| *id == target_obj_id)
        .is_some_and(|(_, t)| t + DEFAULT_COMBAT_TIMEOUT_MS > now)
}

/// `BaseUnit.CanAttack(target)`.
pub fn can_attack(server: &GameServer, me: &Target, target: &Target) -> bool {
    if let Some(allowed) =
        crate::systems::housing::dominion::runtime::can_attack_tower(server, me, target)
    {
        return allowed;
    }
    let my_faction = faction_of(me);
    let target_faction = faction_of(target);
    let factions = &server.game_data.factions;
    if factions.get(my_faction).is_none() || factions.get(target_faction).is_none() {
        return true; // this.Faction == null || target.Faction == null
    }
    if me.obj_id() == target.obj_id() {
        return false;
    }
    let relation = relation_to(server, me, target);
    let me_char = me.as_obj().and_then(|o| o.as_player().cloned());
    let target_owner = target.as_obj().and_then(|o| owner_character(server, o));
    let zone_key = super::geometry::instance_and_zone(target).1;
    let zone_faction_raw = server
        .game_data
        .zones
        .zone_by_key(zone_key)
        .map_or(FACTION_NEUTRAL, |z| z.faction_id);
    let zone_faction_id = if zone_faction_raw == 0 {
        FACTION_NEUTRAL
    } else {
        zone_faction_raw
    };
    let zone_faction = factions
        .get(zone_faction_id)
        .or_else(|| factions.get(FACTION_NEUTRAL));
    let target_mother = factions.get(target_faction).map_or(0, |f| f.mother_id);
    if me_char.is_some() && target_mother != 0 {
        if let Some(z) = zone_faction {
            if target_mother == z.mother_id || target_mother == z.id {
                return false; // protected by the mother zone
            }
        }
    }
    if let (Some(me_c), Some(owner)) = (&me_char, &target_owner) {
        let (owner_faction, owner_id, owner_obj) = {
            let d = owner.data();
            (d.unit.faction_id, d.id, owner.obj_id)
        };
        let trg_flagged = owner
            .data()
            .unit
            .buffs
            .check_buff(buff_constants::RETRIBUTION);
        let owner_mother = factions.get(owner_faction).map_or(0, |f| f.mother_id);
        if owner_mother != 0
            && owner_mother == zone_faction_id
            && !is_actively_hostile(me_c, owner_obj)
            && !trg_flagged
        {
            return false;
        }
        let (me_id, force_attack) = {
            let d = me_c.data();
            (d.id, d.unit.force_attack)
        };
        let is_team = server
            .systems
            .skills
            .ext
            .social()
            .are_team_members(server, me_id, owner_id);
        // flagged (Retribution) target, or Ctrl+F force attack, on a friendly non-team member
        if (trg_flagged || force_attack) && !is_team && relation == RelationState::Friendly {
            return true;
        }
    }
    relation == RelationState::Hostile
}

/// `SkillTargetingUtil.FilterWithRelation(relation, caster, units)`.
pub fn filter_with_relation(
    server: &GameServer,
    relation: SkillTargetRelation,
    caster: &Target,
    units: Vec<WorldObject>,
) -> Vec<WorldObject> {
    match relation {
        SkillTargetRelation::Any | SkillTargetRelation::Party => units,
        SkillTargetRelation::Friendly => units
            .into_iter()
            .filter(|o| {
                let t = Target::Obj(o.clone());
                relation_to(server, caster, &t) == RelationState::Friendly
                    && !can_attack(server, caster, &t)
            })
            .collect(),
        SkillTargetRelation::Hostile => units
            .into_iter()
            .filter(|o| can_attack(server, caster, &Target::Obj(o.clone())))
            .collect(),
        SkillTargetRelation::Raid => {
            let social = server.systems.skills.ext.social();
            let team = social.team_of(server, caster.obj_id());
            let mut out: Vec<WorldObject> = if team == 0 {
                // units.Where(o => o.ObjId == mate?.ObjId): the first active mate
                let mate = caster
                    .as_obj()
                    .and_then(WorldObject::as_player)
                    .and_then(|p| crate::systems::vehicles::mates::first_active_mate(server, p))
                    .map(|m| m.obj_id);
                units
                    .into_iter()
                    .filter(|o| Some(o.obj_id()) == mate)
                    .collect()
            } else {
                units
                    .into_iter()
                    .filter(|o| social.is_team_obj_member(server, team, o.obj_id()))
                    .collect()
            };
            if let Target::Obj(c) = caster {
                out.push(c.clone()); // .Append(caster)
            }
            out
        }
        SkillTargetRelation::Others => units
            .into_iter()
            .filter(|o| {
                relation_to(server, caster, &Target::Obj(o.clone())) == RelationState::Neutral
            })
            .collect(),
    }
}

/// `SkillTargetingUtil.IsRelationValid(relation, caster, target)`.
pub fn is_relation_valid(
    server: &GameServer,
    relation: SkillTargetRelation,
    caster: &Target,
    target: &Target,
) -> bool {
    match relation {
        SkillTargetRelation::Any | SkillTargetRelation::Party => true,
        SkillTargetRelation::Friendly => {
            relation_to(server, caster, target) == RelationState::Friendly
                && !can_attack(server, caster, target)
        }
        SkillTargetRelation::Hostile => can_attack(server, caster, target),
        SkillTargetRelation::Raid => {
            if caster.same(target) {
                return true;
            }
            let social = server.systems.skills.ext.social();
            let team = social.team_of(server, caster.obj_id());
            team != 0 && social.is_team_obj_member(server, team, target.obj_id())
        }
        SkillTargetRelation::Others => caster.obj_id() != target.obj_id(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::world_data::{FactionRelation, SystemFaction};
    use crate::data::GameData;

    fn faction(id: u32, mother: u32) -> SystemFaction {
        SystemFaction {
            id,
            mother_id: mother,
            ..SystemFaction::default()
        }
    }

    #[tokio::test]
    async fn relation_rules() {
        let mut data = GameData::empty();
        for f in [
            faction(1, 0),
            faction(2, 0),
            faction(3, 0),
            faction(101, 0),
            faction(109, 0),
            faction(148, 0),
            faction(150, 101),
        ] {
            data.factions.add(f);
        }
        data.factions.relations.push(FactionRelation {
            id: 101,
            id2: 109,
            state: RelationState::Hostile as u8,
            exp_time: 0,
        });
        let server = crate::test_support::test_server_with(data);
        assert_eq!(faction_relation(&server, 101, 101), RelationState::Friendly);
        assert_eq!(faction_relation(&server, 109, 101), RelationState::Hostile);
        assert_eq!(faction_relation(&server, 101, 148), RelationState::Neutral);
        assert_eq!(faction_relation(&server, 101, 3), RelationState::Hostile);
        assert_eq!(faction_relation(&server, 2, 3), RelationState::Neutral);
        // mother faction 101 is known: its relation table decides, default Friendly
        assert_eq!(faction_relation(&server, 150, 109), RelationState::Hostile);
        assert_eq!(faction_relation(&server, 150, 148), RelationState::Friendly);
        assert_eq!(faction_relation(&server, 0, 101), RelationState::Neutral);
    }
}
