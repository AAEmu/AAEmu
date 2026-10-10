//! The aggro / combat members of `Npc` the AI owns (`ClearAllAggro` override,
//! `ClearAllAggroTargetsAndCheckCombatState`, `CheckIfEmptyAggroToReturn`, `OnDamageReceived`,
//! `GetAggroRatingInPercent`) on top of the `Unit` implementations of the skills subsystem
//! (`AddUnitAggro`, `ClearAggroOfUnit`, base `ClearAllAggro`, `CanAttack`, `IsInBattle`).

use crate::server::GameServer;
use crate::systems::skills::combat as skills_combat;
use crate::systems::skills::geometry::Target;
use crate::systems::skills::relation;
use crate::systems::skills::state::AggroKind;
use crate::world::WorldObject;

use super::framework::AiCtx;
use super::npc;

/// `BaseUnit.CanAttack(target)`.
pub fn can_attack(server: &GameServer, me: &WorldObject, target: &WorldObject) -> bool {
    relation::can_attack(
        server,
        &Target::Obj(me.clone()),
        &Target::Obj(target.clone()),
    )
}

/// `Unit.AddUnitAggro(kind, unit, amount)`.
pub fn add_unit_aggro(
    server: &GameServer,
    owner: &WorldObject,
    kind: AggroKind,
    unit: &WorldObject,
    amount: i32,
) -> bool {
    skills_combat::add_unit_aggro(server, owner, kind, unit, amount)
}

/// `Unit.ClearAggroOfUnit(unit)`.
pub fn clear_aggro_of_unit(server: &GameServer, owner: &WorldObject, unit: &WorldObject) {
    skills_combat::clear_aggro_of_unit(server, owner, unit);
}

/// `ClearAggroOfUnit` for an abuser whose object no longer exists (the C# keeps the `Unit`
/// reference alive; here only its obj id is known): the entry and its watch are dropped.
pub fn clear_aggro_of_missing(server: &GameServer, owner: &WorldObject, unit_id: u32) {
    let removed = owner
        .with_unit(|u| {
            u.unit_mut()
                .skill_state
                .aggro_table
                .remove(&unit_id)
                .is_some()
        })
        .unwrap_or(false);
    if removed {
        if let Some(set) = server
            .systems
            .skills
            .aggro_watch
            .lock()
            .expect("aggro watch poisoned")
            .get_mut(&unit_id)
        {
            set.remove(&owner.obj_id());
        }
        if let Some(n) = owner.as_npc() {
            server
                .systems
                .skills
                .ext
                .npcs()
                .npc_aggro_removed(server, n, unit_id);
        }
    }
}

/// `Unit.IsInBattle = value`.
pub fn set_in_battle(server: &GameServer, obj: &WorldObject, value: bool) {
    skills_combat::set_in_battle(server, obj, value);
}

/// `Npc.ClearAllAggroTargetsAndCheckCombatState()`.
pub fn clear_all_aggro_targets_and_check_combat_state(server: &GameServer, npc_obj: &WorldObject) {
    // Generate a list of all players that we had aggro on, then clear the aggro table
    let ids: Vec<u32> = npc_obj
        .with_unit(|u| {
            let t = &mut u.unit_mut().skill_state.aggro_table;
            let ids = t.keys().copied().collect();
            t.clear();
            ids
        })
        .unwrap_or_default();
    let players: Vec<_> = ids
        .iter()
        .filter_map(|id| server.world.character_by_obj_id(*id))
        .collect();
    for p in players {
        let po = WorldObject::from(&p);
        skills_combat::clear_aggro_of_unit(server, npc_obj, &po);
        if p.data().skill_char.is_in_aggro_list_of.is_empty() {
            // Cancel combat
            set_in_battle(server, &po, false);
        }
    }
}

/// `Npc.ClearAllAggro()` (override): base + clear targets + `CheckIfEmptyAggroToReturn()`.
pub fn npc_clear_all_aggro(c: &mut AiCtx<'_>) {
    skills_combat::clear_all_aggro(c.server, &c.obj);
    let last = c.npc.data().unit.skill_state.aggro_table.len();
    clear_all_aggro_targets_and_check_combat_state(c.server, &c.obj);
    if last > 0 {
        check_if_empty_aggro_to_return(c);
    }
}

/// `Npc.CheckIfEmptyAggroToReturn()`: with an empty aggro table, an NPC more than 4 m away
/// from its idle position returns, and leaves combat.
pub fn check_if_empty_aggro_to_return(c: &mut AiCtx<'_>) {
    if !c.npc.data().unit.skill_state.aggro_table.is_empty() {
        return;
    }
    let pos = npc::world_pos(c.server, &c.obj);
    let distance_to_idle =
        crate::systems::skills::controllers::calculate_distance(c.ai.idle_position, pos, true);
    if distance_to_idle > 4.0 {
        c.go_to_return();
    }
    set_in_battle(c.server, &c.obj, false);
}

/// `Npc.OnDamageReceived(attacker, amount)`.
pub fn on_damage_received(c: &mut AiCtx<'_>, attacker: &WorldObject, amount: i32) {
    add_unit_aggro(c.server, &c.obj, AggroKind::Damage, attacker, amount);
    c.on_aggro_target_changed();
}

/// `Npc.GetAggroRatingInPercent(objId)`: 0 = most aggro ... 100 = not in the table.
pub fn aggro_rating_in_percent(npc_obj: &WorldObject, obj_id: u32) -> f32 {
    let mut sorted: Vec<(u32, i32)> = npc_obj
        .with_unit(|u| {
            u.unit()
                .skill_state
                .aggro_table
                .iter()
                .map(|(k, a)| (*k, a.total()))
                .collect()
        })
        .unwrap_or_default();
    // OrderBy (stable, ascending)
    sorted.sort_by_key(|(_, t)| *t);
    let Some(pos) = sorted.iter().position(|(k, _)| *k == obj_id) else {
        return 100.0;
    };
    1.0 / sorted.len() as f32 * pos as f32
}
