//! `ShipyardManager` (runtime: create, build steps, completion ceremony, decay / tax
//! protection tick, removal) and the `Shipyard` build-step logic (`AddBuildAction`,
//! `CurrentStep`). The templates are `data::VehiclesData::shipyard_templates`.

use std::sync::Mutex;

use crate::models::container::slot_type;
use crate::models::shipyard::{Shipyard, ShipyardData};
use crate::server::GameServer;
use crate::systems::skills::types::{KillReason, SkillCaster};
use crate::world::{Entity, PlayerRef, ShipyardRef, WorldObject};

use super::slaves::buff;
use super::stats::{self, VehicleKind};
use super::{broadcast, error};

/// `ItemTaskType` values used by the shipyard.
mod item_task {
    pub const SKILL_REAGENTS: u8 = 40;
    pub const SKILL_EFFECT_GAIN_ITEM: u8 = 42;
    pub const SHIPYARD: u8 = 60;
}

/// `ShipyardManager` runtime state (`_shipyard` by shipyard id, `_removedShipyards`).
#[derive(Debug, Default)]
pub struct ShipyardManager {
    shipyards: Mutex<Vec<(u32, ShipyardRef)>>,
    removed: Mutex<Vec<u32>>,
}

impl ShipyardManager {
    /// The active shipyards (insertion order).
    pub fn all(&self) -> Vec<ShipyardRef> {
        self.shipyards
            .lock()
            .expect("shipyards poisoned")
            .iter()
            .map(|(_, s)| s.clone())
            .collect()
    }

    /// `_removedShipyards`.
    pub fn removed(&self) -> Vec<u32> {
        self.removed.lock().expect("shipyards poisoned").clone()
    }
}

/// `ShipyardManager.Create(owner, shipyardData)` (`CSCreateShipyardPacket`).
#[allow(clippy::too_many_arguments)]
pub fn create(
    server: &GameServer,
    owner: &PlayerRef,
    template_id: u32,
    x: f32,
    y: f32,
    z: f32,
    z_rot: f32,
    _design_item: u64,
    step: i32,
) -> Option<ShipyardRef> {
    let template = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&template_id)?
        .clone();
    let (instance, zone, owner_name, owner_faction, owner_id) = {
        let c = owner.data();
        (
            c.unit.transform.instance_id,
            c.unit.transform.zone_id,
            c.unit.name.clone(),
            c.unit.faction_id,
            c.id,
        )
    };
    let obj_id = server.ids.object.get_next_id().ok()?;
    let ship_id = server.ids.shipyard.get_next_id().ok()?;
    let Some(step_tpl) = template.step(step).copied() else {
        tracing::error!("Shipyard {template_id} has no step {step} (KeyNotFoundException)");
        return None;
    };
    let data = ShipyardData {
        id: u64::from(ship_id),
        template_id: template.id,
        x,
        y,
        z,
        z_rot,
        money_amount: 0,
        actions: step,
        kind: template.origin_item_id,
        kind2: owner_id,
        owner_name: owner_name.clone(),
        kind3: owner_faction,
        spawned: chrono::Utc::now().timestamp(),
        obj_id,
        hp: step_tpl.max_hp * 100,
        step,
    };
    let mut sy = Shipyard::new(data);
    sy.unit.transform.instance_id = instance;
    sy.unit.transform.zone_id = zone;
    sy.unit.transform.set_position(x, y, z, 0.0, 0.0, z_rot);
    sy.unit.faction_id = owner_faction;
    sy.unit.level = 30;
    sy.unit.name = owner_name;
    sy.unit.model_id = step_tpl.model_id;
    // Template setter: AllAction = sum of the steps' NumActions
    sy.all_action = template.all_actions();
    stats::refresh_pools(server, VehicleKind::Shipyard, &mut sy.unit);
    sy.unit.hp = sy.unit.max_hp;
    let shipyard = Entity::new(obj_id, sy);

    // we will make checks for the availability of money and items to create a shipyard
    if !remove_required_items(server, owner, &template) {
        error(owner, error::NOT_ENOUGH_ITEM);
        return None;
    }
    server
        .systems
        .vehicles
        .shipyards
        .shipyards
        .lock()
        .expect("shipyards poisoned")
        .push((ship_id, shipyard.clone()));
    if let Some(world) = server.world.world(instance) {
        let changes = world.spawn(&shipyard, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
    }
    Some(shipyard)
}

/// `RemoveRequiredItems(shipyard)`.
fn remove_required_items(
    server: &GameServer,
    character: &PlayerRef,
    template: &super::data::ShipyardTemplate,
) -> bool {
    let items = server.systems.skills.ext.items();
    let design_id = template.origin_item_id;
    let Some(&money_owed) = server
        .game_data
        .systems
        .vehicles
        .taxations
        .get(&(template.taxation_id as u32))
    else {
        tracing::error!(
            "Shipyard {}: taxation {} missing (KeyNotFoundException)",
            template.id,
            template.taxation_id
        );
        return false;
    };
    if items.item_count(server, character, design_id) < 1 {
        error(character, error::NOT_ENOUGH_ITEM);
        tracing::error!("Not enough item Id={design_id}");
        return false;
    }
    if character.data().money < i64::from(money_owed) {
        error(character, error::NOT_ENOUGH_MONEY);
        return false;
    }
    let Some((use_skill_id, _)) = items.item_use_skill(server, design_id) else {
        tracing::error!("Could not find Reagents/Products for item template {design_id}");
        return false;
    };
    let skills = &server.game_data.systems.skills;
    let reagents = skills.reagents_for(use_skill_id);
    let products = skills.products_for(use_skill_id);
    if !reagents.is_empty() {
        let mut enough = true;
        for r in &reagents {
            if items.item_count(server, character, r.item_id) >= i32::from(r.amount) {
                continue;
            }
            enough = false;
            tracing::error!("Not enough reagents Id={}, Amount={}", r.item_id, r.amount);
        }
        if !enough {
            return false;
        }
        for r in &reagents {
            items.consume_bag_item(
                server,
                character,
                item_task::SKILL_REAGENTS,
                r.item_id,
                i32::from(r.amount),
            );
        }
    }
    for p in &products {
        items.acquire_default_item(
            server,
            character,
            item_task::SKILL_EFFECT_GAIN_ITEM,
            p.item_id,
            i32::from(p.amount),
        );
    }
    items.consume_bag_item(server, character, item_task::SHIPYARD, design_id, 1);
    // SubtractMoney(SlotType.Inventory, moneyOwed, ItemTaskType.Shipyard)
    character.data().money -= i64::from(money_owed);
    character.send(
        crate::systems::social::packets::expedition::SCItemTaskSuccessMoneyPacket {
            action: item_task::SHIPYARD,
            money_change: -(money_owed as i32),
        },
    );
    true
}

/// `RemoveShipyard(shipyard)`.
pub fn remove_shipyard(server: &GameServer, shipyard: &ShipyardRef) {
    let ship_id = shipyard.data().data.id as u32;
    let m = &server.systems.vehicles.shipyards;
    m.removed.lock().expect("shipyards poisoned").push(ship_id);
    m.shipyards
        .lock()
        .expect("shipyards poisoned")
        .retain(|(id, _)| *id != ship_id);
    server.ids.shipyard.release_id(ship_id);
    server.ids.object.release_id(shipyard.obj_id);
    if let Some(world) = server.world.world_of(&WorldObject::from(shipyard)) {
        world.delete(shipyard);
    }
}

/// `Shipyard.OnDeath` (`Events.OnDeath` subscription): `RemoveShipyard(this)`.
pub fn on_death(server: &GameServer, shipyard: &ShipyardRef) {
    {
        let d = shipyard.data();
        tracing::debug!(
            "Shipyard died ObjId:{} - TemplateId:{} - {}",
            shipyard.obj_id,
            d.data.template_id,
            d.data.owner_name
        );
    }
    remove_shipyard(server, shipyard);
}

/// `CurrentStep` setter (`ModelId`, `BaseAction`).
fn set_current_step(server: &GameServer, s: &mut Shipyard, step: i32) {
    s.current_step = step;
    s.is_dirty = true;
    let Some(t) = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&s.data.template_id)
    else {
        return;
    };
    s.unit.model_id = if step == -1 {
        t.main_model_id
    } else {
        t.step(step).map_or(s.unit.model_id, |x| x.model_id)
    };
    if step <= 0 {
        return;
    }
    s.base_action = (0..step)
        .map(|i| t.step(i).map_or(0, |x| x.num_actions))
        .sum();
}

/// `Shipyard.AddBuildAction()`.
pub fn add_build_action(server: &GameServer, shipyard: &ShipyardRef) {
    let mut s = shipyard.data();
    if s.current_step == -1 {
        return;
    }
    let Some(t) = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&s.data.template_id)
    else {
        return;
    };
    let Some(step) = t.step(s.current_step).copied() else {
        tracing::error!(
            "Shipyard {}: no step {} (KeyNotFoundException)",
            s.data.template_id,
            s.current_step
        );
        return;
    };
    let count = t.steps.len() as i32;
    let next_action = s.num_action + 1;
    if step.num_actions > next_action {
        s.num_action = next_action;
        s.is_dirty = true;
    } else {
        s.num_action = 0;
        s.is_dirty = true;
        let next_step = s.current_step + 1;
        if count > next_step {
            set_current_step(server, &mut s, next_step);
        } else {
            set_current_step(server, &mut s, -1);
        }
    }
}

/// The skill id the current build step requires (`shipStep.SkillId`); `None` when there is no
/// step template (`shipStep == null`).
pub fn current_step_template(server: &GameServer, shipyard: &ShipyardRef) -> Option<u32> {
    let s = shipyard.data();
    let t = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&s.data.template_id)?;
    let current_action = s.base_action + s.num_action;
    if current_action >= 0 && s.current_step < t.steps.len() as i32 {
        if s.current_step == -1 {
            // C#: `ShipyardSteps[-1]` throws KeyNotFoundException.
            tracing::error!(
                "Shipyard {}: ShipyardSteps[-1] (KeyNotFoundException)",
                t.id
            );
        }
        t.step(s.current_step).map(|x| x.skill_id)
    } else {
        None
    }
}

/// The `ShipyardData.Actions` / `Step` update of `CraftEffect` after a build action.
pub fn sync_build_state(server: &GameServer, shipyard: &ShipyardRef) {
    let mut s = shipyard.data();
    let count = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&s.data.template_id)
        .map_or(0, |t| t.steps.len() as i32);
    if s.current_step == -1 {
        s.data.actions = s.all_action;
        s.data.step = count;
        tracing::trace!("[Shipyard] Actions {}, Step {count}", s.all_action);
    } else {
        s.data.actions = s.base_action + s.num_action;
        s.data.step = s.current_step;
    }
}

/// `ShipyardCompletedTask(shipyard)`: the finished ship item, step 1000 (launch ceremony),
/// `ShipyardCompleted` after `CeremonyAnimTime`.
pub fn shipyard_completed_task(server: &GameServer, shipyard: &ShipyardRef) {
    let (owner_name, template_id) = {
        let s = shipyard.data();
        (s.data.owner_name.clone(), s.data.template_id)
    };
    let Some(character) = server.world.character_by_name(&owner_name) else {
        tracing::error!(
            "ShipyardCompletedTask: owner {owner_name} offline (NullReferenceException)"
        );
        return;
    };
    let Some(template) = server
        .game_data
        .systems
        .vehicles
        .shipyard_templates
        .get(&template_id)
        .cloned()
    else {
        return;
    };
    server.systems.skills.ext.items().acquire_default_item(
        server,
        &character,
        item_task::SHIPYARD,
        template.item_id,
        1,
    );
    let data = {
        let mut s = shipyard.data();
        s.data.step = 1000; // last step, the ceremony of launching the ship
        s.data.clone()
    };
    broadcast(
        server,
        &WorldObject::from(&character),
        &crate::packets::world_objects::SCShipyardStatePacket { data },
        true,
    );
    let delay = template.ceremony_anim_time.max(0) as u64;
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return;
    };
    let shipyard = shipyard.clone();
    rt.spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(delay)).await;
        shipyard_completed(&server, &shipyard).await;
    });
}

/// `ShipyardCompleted(shipyard)` (`ShipyardCompleteTask`). Note (C#): the `SkillItem` it builds
/// only has `ItemId` set, so `SlaveManager.Create(owner, skillData, ...)` returns before creating
/// the ship (its `ItemTemplateId == 0` check); the owner's active vehicle is still removed.
pub async fn shipyard_completed(server: &GameServer, shipyard: &ShipyardRef) {
    let (owner_name, template_id) = {
        let s = shipyard.data();
        (s.data.owner_name.clone(), s.data.template_id)
    };
    if let Some(character) = server.world.character_by_name(&owner_name) {
        let item_id = server
            .game_data
            .systems
            .vehicles
            .shipyard_templates
            .get(&template_id)
            .and_then(|t| first_bag_item(&character, t.item_id));
        if let Some(item_id) = item_id {
            let caster = SkillCaster::Item {
                obj_id: 0,
                item_id,
                item_template_id: 0,
                type1: 0,
                type2: 0,
            };
            if let SkillCaster::Item {
                item_id,
                item_template_id,
                ..
            } = caster
            {
                let pos = shipyard.data().unit.transform;
                super::slaves::create_for_owner(
                    server,
                    &character,
                    item_id,
                    item_template_id,
                    true,
                    Some(pos),
                )
                .await;
            }
        }
    } else {
        tracing::error!("ShipyardCompleted: owner {owner_name} offline (NullReferenceException)");
    }
    remove_shipyard(server, shipyard);
}

/// `Inventory.Bag.GetAllItemsByTemplate(templateId, -1)` first item id.
fn first_bag_item(character: &PlayerRef, template_id: u32) -> Option<u64> {
    let bag = character
        .data()
        .inventory
        .get(slot_type::INVENTORY)
        .cloned()?;
    let c = bag.lock().expect("container poisoned");
    let (ids, _) = c.items_by_template(template_id, -1);
    ids.first().copied()
}

/// `ShipyardTick()` (every minute): tax protection / deterioration of every shipyard.
pub fn shipyard_tick(server: &GameServer) {
    for s in server.systems.vehicles.shipyards.all() {
        update_shipyard_info(server, &s);
    }
}

fn update_shipyard_info(server: &GameServer, shipyard: &ShipyardRef) {
    let spawned = shipyard.data().data.spawned;
    let now = chrono::Utc::now().timestamp();
    let is_decaying = now >= spawned + 3 * 24 * 3600;
    set_protection_buff(server, shipyard, is_decaying, now);
    set_decay_buff(server, shipyard, is_decaying);
}

fn set_protection_buff(server: &GameServer, shipyard: &ShipyardRef, is_decay: bool, now: i64) {
    let obj = WorldObject::from(shipyard);
    let has = shipyard.data().unit.buffs.check_buff(buff::TAX_PROTECTION);
    if !is_decay {
        let (spawned, template_id) = {
            let s = shipyard.data();
            (s.data.spawned, s.data.template_id)
        };
        let duration_min = (spawned - now) as f64 / 60.0;
        let mins = duration_min.round_ties_even() * 60000.0;
        let tax_duration = server
            .game_data
            .systems
            .vehicles
            .shipyard_templates
            .get(&template_id)
            .map_or(0, |t| t.tax_duration);
        let time_left = f64::from(tax_duration) + mins;
        if has {
            return;
        }
        if server
            .game_data
            .systems
            .skills
            .buff(buff::TAX_PROTECTION)
            .is_some()
        {
            crate::systems::skills::buffs::add_buff_by_id(
                server,
                &obj,
                buff::TAX_PROTECTION,
                &obj,
                time_left as i32,
            );
        } else {
            tracing::error!("Unable to find Protection Buff template");
        }
    } else if has {
        crate::systems::skills::buffs::remove_buff(server, &obj, buff::TAX_PROTECTION);
    }
}

fn set_decay_buff(server: &GameServer, shipyard: &ShipyardRef, is_decay: bool) {
    let obj = WorldObject::from(shipyard);
    let has = shipyard.data().unit.buffs.check_buff(buff::DETERIORATION);
    if is_decay {
        if has {
            crate::systems::skills::combat::reduce_current_hp(
                server,
                &obj,
                Some(&obj),
                7,
                KillReason::Damage,
            );
            let owner_name = shipyard.data().data.owner_name.clone();
            let Some(character) = server.world.character_by_name(&owner_name) else {
                tracing::error!(
                    "Shipyard decay: owner {owner_name} offline (NullReferenceException)"
                );
                return;
            };
            let (state, data) = {
                let mut s = shipyard.data();
                let state = crate::world::unit_state::shipyard_unit_state(
                    shipyard.obj_id,
                    &mut s,
                    &owner_name,
                    crate::world::now_ms(),
                );
                (state, s.data.clone())
            };
            character.send(state);
            character.send(crate::packets::world_objects::SCShipyardStatePacket { data });
            return;
        }
        if server
            .game_data
            .systems
            .skills
            .buff(buff::DETERIORATION)
            .is_some()
        {
            crate::systems::skills::buffs::add_buff_by_id(
                server,
                &obj,
                buff::DETERIORATION,
                &obj,
                0,
            );
        } else {
            tracing::error!("Unable to find Deterioration Debuff template");
        }
    } else if has {
        crate::systems::skills::buffs::remove_buff(server, &obj, buff::DETERIORATION);
    }
}
