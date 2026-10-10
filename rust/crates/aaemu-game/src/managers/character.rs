//! The lobby half of `Core/Managers/UnitManagers/CharacterManager`: `Create`,
//! `SetDeleteCharacter`, `SetRestoreCharacter`, `CheckForDeletedCharacters` (+ the
//! `CharacterDeleteTask` scheduling) and `ItemContainer.AcquireDefaultItem`, which character
//! creation uses for the starting supplies.

use std::sync::Arc;
use std::time::Duration;

use chrono::Utc;

use crate::data::character_templates::EquipItemsTemplate;
use crate::data::items::{bind_type, ItemTemplate};
use crate::db::get_account_details;
use crate::db_character::{
    check_for_deleted_characters, is_character_pending_deletion, learn_skill,
    save_directly_to_database, set_delete_times,
};
use crate::error::{GameError, Result};
use crate::managers::name::{normalize_name, CharacterCreateError};
use crate::models::char_data::{ability, ActionSlotType, CharacterAbilities, CharacterActability};
use crate::models::character::{Character, Inventory, Transform};
use crate::models::container::{equip_slot, slot_type, ContainerRef};
use crate::models::unit_model::UnitCustomModelParams;
use crate::net::connection::GameConnection;
use crate::packets::world::{
    SCCancelCharacterDeleteResponsePacket, SCCharacterCreationFailedPacket,
    SCDeleteCharacterResponsePacket, SCErrorMsgPacket,
};
use crate::server::GameServer;
use crate::world::unit_state::create_character_response;

/// `ErrorMessageType.CannotDeleteCharWhileBotSuspected`.
pub const ERROR_CANNOT_DELETE_CHAR_WHILE_BOT_SUSPECTED: i16 = 624;

/// The data `CSCreateCharacterPacket` carries.
#[derive(Debug, Clone, PartialEq)]
pub struct CreateCharacterRequest {
    pub name: String,
    pub race: u8,
    pub gender: u8,
    pub body_items: [u32; 7],
    pub model_params: UnitCustomModelParams,
    pub ability1: u8,
    pub ability2: u8,
    pub ability3: u8,
    pub level: u8,
}

/// `ItemContainer.AcquireDefaultItem(ItemTaskType.Invalid, templateId, amount, grade)`.
pub fn acquire_default_item(
    server: &GameServer,
    container: &ContainerRef,
    template_id: u32,
    mut amount: i32,
    grade: i32,
) -> Result<bool> {
    if amount <= 0 {
        return Ok(true);
    }
    let data = &server.game_data.items;
    let mut c = container.lock().expect("container poisoned");
    let (current, units) = c.items_by_template(template_id, grade);
    let Some(template) = data.template(template_id) else {
        return Ok(false);
    };
    let free = current.len() as i32 * template.max_count - units
        + c.free_slot_count() * template.max_count;
    if amount > free {
        return Ok(false);
    }
    let mut grade = grade;
    if template.fixed_grade >= 0 && !template.gradable {
        grade = template.fixed_grade;
    }
    if grade == -1 {
        grade = template.fixed_grade;
    }
    if grade < 0 {
        grade = 0;
    }
    let mut added = false;
    if c.slot_type != slot_type::MAIL && c.slot_type != slot_type::AUCTION {
        let mut ids = current.clone();
        ids.sort_by_key(|id| c.get_item_by_id(*id).map_or(0, |i| i.slot));
        for id in ids {
            let Some(item) = c.items.iter_mut().find(|i| i.id == id) else {
                continue;
            };
            let max = data.template(item.template_id).map_or(0, |t| t.max_count);
            let space = max - item.count;
            if space > 0 {
                let add = space.min(amount);
                item.count += add;
                item.dirty = true;
                amount -= add;
                added = true;
            }
            if amount < 0 {
                break;
            }
        }
    }
    let now = Utc::now().timestamp();
    let container_slot_type = c.slot_type;
    // Lock order: the item store before a container (see ItemManager::container_for_character).
    drop(c);
    while amount > 0 {
        let add = amount.min(template.max_count);
        let Some(mut item) = server.items.create(
            template_id,
            add,
            grade as u8,
            server.config.id,
            data,
            &server.ids.item,
            now,
        ) else {
            tracing::error!(
                "Failed to add item with ID {template_id}, possible duplicate entries!"
            );
            return Ok(false);
        };
        amount -= add;
        let pref = if matches!(
            template.kind,
            crate::data::items::TemplateKind::Backpack { .. }
        ) && container_slot_type == slot_type::EQUIPMENT
        {
            equip_slot::BACKPACK
        } else {
            -1
        };
        apply_new_item_timers(&mut item, template, now);
        let res = container
            .lock()
            .expect("container poisoned")
            .add_new_item(item, pref, data);
        match res {
            Ok(true) => added = true,
            _ => {
                return Err(GameError::Protocol(
                    "AcquireDefaultItem(); Unable to add new items".into(),
                ))
            }
        }
    }
    container
        .lock()
        .expect("container poisoned")
        .update_free_slot_count();
    Ok(added)
}

/// The lifetime / charge setup `AcquireDefaultItemEx` does for each new item
/// (`SetItemExpirationTime`, `SetItemOnlineExpirationTime`, `ChargeCount` / `ChargeStartTime`).
fn apply_new_item_timers(item: &mut crate::models::item::Item, template: &ItemTemplate, now: i64) {
    if template.exp_abs_lifetime > 0 {
        item.expiration_time = now + i64::from(template.exp_abs_lifetime) * 60;
    }
    if template.exp_online_lifetime > 0 {
        item.expiration_online_minutes_left = f64::from(template.exp_online_lifetime);
    }
    if let Some(t) = template.exp_date {
        item.expiration_time = t;
    }
    if let Some(e) = template.equip() {
        if item.class.is_equip() {
            item.charge_count = e.charge_count;
            if e.charge_lifetime > 0 && template.bind_type & bind_type::BIND_ON_UNPACK == 0 {
                item.charge_start_time = now;
            }
        }
    }
}

/// `SetEquipItemTemplate(inventory, templateId, slot, grade)`.
fn set_equip_item_template(
    server: &GameServer,
    inventory: &Inventory,
    template_id: u32,
    slot: i32,
    grade: u8,
) {
    if template_id == 0 {
        return; // AddOrMoveExistingItem(null) -> false
    }
    let data = &server.game_data.items;
    let Some(mut item) = server.items.create(
        template_id,
        1,
        grade,
        server.config.id,
        data,
        &server.ids.item,
        Utc::now().timestamp(),
    ) else {
        // Create returned null -> AddOrMoveExistingItem(null).
        return;
    };
    item.slot_type = slot_type::EQUIPMENT;
    item.slot = slot;
    let mut eq = inventory.equipment().lock().expect("container poisoned");
    if let Err(item) = eq.add_new_item(item, slot, data) {
        tracing::warn!(
            "SetEquipItemTemplate: item {} ({template_id}) refused in slot {slot}",
            item.id
        );
    }
}

fn equip_pack(server: &GameServer, inv: &Inventory, i: &EquipItemsTemplate) {
    use equip_slot::*;
    for (slot, (id, grade)) in [
        (HEAD, i.headgear),
        (NECK, i.necklace),
        (CHEST, i.shirt),
        (WAIST, i.belt),
        (LEGS, i.pants),
        (HANDS, i.gloves),
        (FEET, i.shoes),
        (ARMS, i.bracelet),
        (BACK, i.back),
        (UNDERSHIRT, i.undershirts),
        (UNDERPANTS, i.underpants),
        (MAINHAND, i.mainhand),
        (OFFHAND, i.offhand),
        (RANGED, i.ranged),
        (MUSICAL, i.musical),
        (COSPLAY, i.cosplay),
    ] {
        set_equip_item_template(server, inv, id, slot, grade);
    }
}

/// `CharacterManager.Create`.
pub async fn create(
    server: &GameServer,
    conn: &mut GameConnection,
    req: CreateCharacterRequest,
) -> Result<()> {
    let name = normalize_name(&req.name);
    let pending = if server.names.name_exists(&name) {
        is_character_pending_deletion(&server.db, &name).await?
    } else {
        false
    };
    let code = server.names.validate_character_name(&name, || pending);
    if code != CharacterCreateError::Ok {
        conn.send(&SCCharacterCreationFailedPacket { reason: code as u8 });
        return Ok(());
    }
    // NOTE: This is purely a warning to log potential cheaters
    if req.ability2 != ability::NONE || req.ability3 != ability::NONE {
        tracing::error!(
            "User tried to make a new character that has 2nd and/or 3rd ability already set. Account {}, Name {name}, Class {}, {}, {}",
            conn.account_id(),
            req.ability1,
            req.ability2,
            req.ability3
        );
    }
    let account = get_account_details(&server.db, &server.config, conn.account_id()).await;
    let mut access_level = server.config.account.access_level_default;
    if server.names.no_names_registered() {
        access_level = server
            .config
            .account
            .access_level_first_character
            .max(access_level);
    }
    let id = server
        .ids
        .character
        .get_next_id()
        .map_err(|e| GameError::Protocol(e.to_string()))?;
    server.names.add_character(id, &name, conn.account_id());
    let data = &server.game_data;
    let template = data
        .characters
        .template(req.race, req.gender)
        .cloned()
        .ok_or_else(|| {
            GameError::Protocol(format!(
                "no template for race {} gender {}",
                req.race, req.gender
            ))
        })?;

    let mut c = Character::new(id, conn.account_id(), req.model_params.clone());
    c.unit.name = name.clone();
    c.race = req.race;
    c.gender = req.gender;
    let sp = template.spawn_position;
    c.unit.transform = Transform {
        instance_id: crate::models::character::DEFAULT_INSTANCE_ID,
        world_id: data.worlds.default_world_template_id(),
        zone_id: sp.zone_id,
        x: sp.x,
        y: sp.y,
        z: sp.z,
        rot_x: sp.roll,
        rot_y: sp.pitch,
        rot_z: sp.yaw,
        parent_obj_id: 0,
    };
    c.unit.level = req.level;
    c.unit.faction_id = template.faction_id;
    c.faction_name = String::new();
    c.access_level = access_level;
    c.labor_power = account.labor;
    c.labor_power_modified = crate::db::unix(account.last_updated);
    c.num_inventory_slots = template.num_inventory_slot;
    c.num_bank_slots = template.num_bank_slot;
    c.inventory = Inventory::new(
        id,
        c.num_inventory_slots,
        c.num_bank_slots,
        &server.items,
        &server.ids.container,
    );
    let now = Utc::now().timestamp();
    c.created = now;
    c.updated = now;
    c.ability1 = req.ability1;
    c.ability2 = req.ability2;
    c.ability3 = req.ability3;
    c.return_district_id = template.return_district_id;
    c.resurrection_district_id = template.resurrection_district_id;

    let items = data
        .characters
        .ability_items
        .get(&req.ability1)
        .cloned()
        .ok_or_else(|| GameError::Protocol(format!("no ability items for {}", req.ability1)))?;
    equip_pack(server, &c.inventory, &items.items);
    for (i, &body) in req.body_items.iter().enumerate() {
        let item = if body == 0 && template.items[i] > 0 {
            template.items[i]
        } else {
            body
        };
        set_equip_item_template(server, &c.inventory, item, i as i32 + 19, 0);
    }

    let mut slot = 10usize;
    let bag = c.inventory.bag().clone();
    for s in &items.supplies {
        acquire_default_item(server, &bag, s.id, s.amount, i32::from(s.grade))?;
        c.set_action(slot, ActionSlotType::ItemType, u64::from(s.id));
        slot += 1;
    }
    let general = data
        .characters
        .ability_items
        .get(&0)
        .cloned()
        .ok_or_else(|| GameError::Protocol("no ability items for ability 0".into()))?;
    for s in &general.supplies {
        acquire_default_item(server, &bag, s.id, s.amount, i32::from(s.grade))?;
        c.set_action(slot, ActionSlotType::ItemType, u64::from(s.id));
        slot += 1;
    }

    c.abilities = CharacterAbilities::default();
    c.abilities.set_ability(c.ability1, 0);
    c.actability = CharacterActability::for_new_character(&data.characters);

    for ds in &data.skills.default_skills {
        if !ds.add_to_slot {
            continue;
        }
        c.set_action(
            usize::from(ds.slot),
            ActionSlotType::Spell,
            u64::from(ds.skill_id),
        );
    }
    let mut slot = 1usize;
    while c
        .slots
        .get(slot)
        .is_some_and(|s| s.kind != ActionSlotType::None)
    {
        slot += 1;
    }
    let start = data
        .skills
        .start_ability_skills(c.ability1)
        .map(<[u32]>::to_vec)
        .ok_or_else(|| {
            GameError::Protocol(format!("no start skills for ability {}", c.ability1))
        })?;
    for skill_id in start {
        // Skills.AddSkill(template, 1, false): the level comes from the ability level.
        let ab = crate::db_character::ab_level(
            server,
            &c,
            data.skills.skill(skill_id).map_or(0, |t| t.ability_id),
        );
        c.skills.add_skill_template(&data.skills, skill_id, ab);
        c.set_action(slot, ActionSlotType::Spell, u64::from(skill_id));
        slot += 1;
    }
    let _ = learn_skill;

    let stats = c.stats(&data.formulas);
    c.unit.hp = stats.max_hp;
    c.unit.mp = stats.max_mp;
    c.restore_saved_hp_mp(&data.formulas);

    if save_directly_to_database(&server.db, &mut c).await {
        let resp = create_character_response(&c);
        conn.characters.push(c);
        conn.send(&resp);
    } else {
        // There is no actual response for internal DB saving error for the client.
        conn.send(&SCCharacterCreationFailedPacket {
            reason: CharacterCreateError::Failed as u8,
        });
        server.ids.character.release_id(id);
        server.names.remove_character_id(id);
        // DeleteCharacterAssets(character, true): houses / guild / family / mails are later
        // phases ("fullWipe is currently not implemented yet" in C# too).
        tracing::warn!(
            "DeleteCharacterAssets - fullWipe is currently not implemented yet, charId: {id}"
        );
    }
    Ok(())
}

/// `CharacterManager.SetDeleteCharacter`.
pub async fn set_delete_character(
    server: &GameServer,
    conn: &mut GameConnection,
    character_id: u32,
) -> Result<()> {
    if let Some(i) = conn.characters.iter().position(|c| c.id == character_id) {
        let now = Utc::now().timestamp();
        let level = conn.characters[i].unit.level;
        let mut delay = 0;
        for t in &server.config.account.delete_timings {
            if level >= t.level {
                delay = t.delay;
            }
        }
        let c = &mut conn.characters[i];
        c.delete_request_time = now;
        c.delete_time = now + i64::from(delay) * 60;
        let (req, del) = (c.delete_request_time, c.delete_time);
        if set_delete_times(&server.db, character_id, req, del).await? == 1 {
            conn.send(&SCDeleteCharacterResponsePacket {
                character_id,
                status: 2,
                delete_request_time: req,
                delete_time: del,
            });
        } else {
            // Not the correct message, but it seems funny enough (C#)
            conn.send(&SCErrorMsgPacket::new(
                ERROR_CANNOT_DELETE_CHAR_WHILE_BOT_SUSPECTED,
                0,
                true,
            ));
        }
    } else {
        conn.send(&SCDeleteCharacterResponsePacket {
            character_id,
            status: 0,
            delete_request_time: 0,
            delete_time: 0,
        });
    }
    // Trigger our task queueing
    match server.self_arc() {
        Some(s) => schedule_deleted_characters_check(s).await,
        None => tracing::warn!("CheckForDeletedCharacters skipped: server not shared"),
    }
    Ok(())
}

/// `CharacterManager.SetRestoreCharacter`.
pub async fn set_restore_character(
    server: &GameServer,
    conn: &mut GameConnection,
    character_id: u32,
) -> Result<()> {
    if let Some(c) = conn.characters.iter_mut().find(|c| c.id == character_id) {
        c.delete_request_time = 0;
        c.delete_time = 0;
        conn.send(&SCCancelCharacterDeleteResponsePacket {
            character_id,
            status: 3,
        });
        set_delete_times(&server.db, character_id, 0, 0).await?;
    } else {
        conn.send(&SCCancelCharacterDeleteResponsePacket {
            character_id,
            status: 4,
        });
    }
    Ok(())
}

/// `CheckForDeletedCharacters` + scheduling of the next `CharacterDeleteTask`.
pub async fn schedule_deleted_characters_check(server: Arc<GameServer>) {
    if let Some(next) = run_deleted_characters_check(&server).await {
        tokio::spawn(character_delete_task(server, next));
    }
}

/// One `CheckForDeletedCharacters` pass; returns the next deletion time to wait for.
async fn run_deleted_characters_check(server: &GameServer) -> Option<i64> {
    tracing::debug!("CheckForDeletedCharacters - Begin");
    match check_for_deleted_characters(server).await {
        Ok(Some(t)) => {
            tracing::debug!("CheckForDeletedCharacters - Next delete scheduled at {t}");
            Some(t)
        }
        Ok(None) => {
            tracing::debug!("CheckForDeletedCharacters - No new deletions scheduled");
            None
        }
        Err(e) => {
            tracing::error!("CheckForDeletedCharacters: {e}");
            None
        }
    }
}

/// `CharacterDeleteTask`: waits for the next deletion time and runs the check again.
async fn character_delete_task(server: Arc<GameServer>, mut next: i64) {
    loop {
        let wait = (next - Utc::now().timestamp()).max(0) as u64;
        tokio::time::sleep(Duration::from_secs(wait)).await;
        match run_deleted_characters_check(&server).await {
            Some(n) => next = n,
            None => break,
        }
    }
}

#[cfg(test)]
#[path = "character_tests.rs"]
mod character_tests;

#[cfg(test)]
#[path = "unit_character_manager_tests.rs"]
mod unit_character_manager_tests;
