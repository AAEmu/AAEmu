//! `HousingManager` state (`_houses`, `_housesTl`, `_removedHousings`) and the `House` model
//! behaviour that needs other objects: `Create`, the `CurrentStep` setter (bound doodads),
//! `AddBuildAction`, `Spawn` / `Delete` with the attached doodads, `AllowedToInteract`,
//! `LoadPlayerHousing` / `SpawnAll` / `ReconcileBoundDoodads` / `Save`, the tax helpers
//! (`CalculateBuildingTaxInfo`, `UpdateTaxInfo` + `MailForTax`, `CheckHousingTaxes`,
//! `PayWeeklyTax`), the untouchable / removal buffs and `GetHouseAtLocation`.
//!
//! Locking: the manager lock only guards the maps and is never held while calling the world,
//! a hook or another object's lock; house data locks are held one at a time (see
//! `world::object`).

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use crate::models::house::{housing_permission, House};
use crate::models::item::UnixTime;
use crate::server::GameServer;
use crate::world::{DoodadRef, Entity, HouseRef, PlayerRef, WorldInstance, WorldObject};

use super::data::{HousingBindingDoodad, HousingTemplate};
use super::hooks::{mail_type, HouseMail, TaxMailUpdate, MAIL_STATUS_UNPAID};

/// `ForSaleMarkerDoodadId`.
pub const FOR_SALE_MARKER_DOODAD_ID: u32 = 6760;
/// `MaxHeavyTaxCounted`.
pub const MAX_HEAVY_TAX_COUNTED: i32 = 10;
/// `HoursForFailedTaxToReturnHouse`.
pub const HOURS_FOR_FAILED_TAX_TO_RETURN_HOUSE: i64 = 22;
/// `CopperPerCertificate`.
pub const COPPER_PER_CERTIFICATE: f64 = 1_000_000.0;
/// `BuffConstants.Untouchable`.
pub const BUFF_UNTOUCHABLE: u32 = 545;
/// `BuffConstants.RemovalDebuff`.
pub const BUFF_REMOVAL_DEBUFF: u32 = 2250;
/// `FactionsEnum.Monstrosity`.
pub const FACTION_MONSTROSITY: u32 = 115;
/// `Item.TaxCertificate` / `BoundTaxCertificate` / `AppraisalCertificate`.
pub const TAX_CERTIFICATE: u32 = 31891;
pub const BOUND_TAX_CERTIFICATE: u32 = 31892;
pub const APPRAISAL_CERTIFICATE: u32 = 28085;

const DAY: i64 = 24 * 3600;

/// `DateTime.UtcNow` in unix seconds (housing dates are stored with second precision, like the
/// MySQL `DATETIME` columns).
pub fn now() -> UnixTime {
    chrono::Utc::now().timestamp()
}

/// `TaxDueDate` (`ProtectionEndDate.AddDays(-7)`).
pub fn tax_due_date(h: &House) -> UnixTime {
    h.protection_end_date - 7 * DAY
}

/// The `HousingManager` maps.
#[derive(Debug, Default)]
struct Inner {
    /// `_houses` (by DB id) and their insertion order (C# dictionary enumeration order).
    houses: HashMap<u32, HouseRef>,
    order: Vec<u32>,
    /// `_housesTl`.
    by_tl: HashMap<u16, HouseRef>,
    /// `_removedHousings`.
    removed: Vec<u32>,
    /// `House.AttachedDoodads` (by house obj id; `House.attached_doodads` mirrors the obj ids
    /// for the visibility packets).
    attached: HashMap<u32, Vec<DoodadRef>>,
}

/// `HousingManager` (`server.systems.housing.manager`).
#[derive(Debug, Default)]
pub struct HousingManager {
    inner: Mutex<Inner>,
    /// `_isCheckingTaxTiming`.
    checking_tax: AtomicBool,
}

impl HousingManager {
    fn lock(&self) -> MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Clears every map (`LoadPlayerHousing`).
    pub fn clear(&self) {
        *self.lock() = Inner::default();
    }

    /// `_houses.Add(house.Id, house)` + `_housesTl.Add(house.TlId, house)`. `false` when the id
    /// or tl id is already used (the C# `Dictionary.Add` throws).
    pub fn insert(&self, house: &HouseRef) -> bool {
        let (id, tl) = {
            let h = house.data();
            (h.id, h.unit.tl_id)
        };
        let mut inner = self.lock();
        if inner.houses.contains_key(&id) || inner.by_tl.contains_key(&tl) {
            return false;
        }
        inner.houses.insert(id, house.clone());
        inner.order.push(id);
        inner.by_tl.insert(tl, house.clone());
        true
    }

    /// Every house in `_houses` order.
    pub fn all(&self) -> Vec<HouseRef> {
        let inner = self.lock();
        inner
            .order
            .iter()
            .filter_map(|id| inner.houses.get(id).cloned())
            .collect()
    }

    pub fn count(&self) -> usize {
        self.lock().houses.len()
    }

    /// `GetByAccountId(values, accountId)`.
    pub fn by_account(&self, account_id: u32) -> Vec<HouseRef> {
        self.all()
            .into_iter()
            .filter(|h| h.data().account_id == account_id)
            .collect()
    }

    /// `GetByCharacterId(values, characterId)`.
    pub fn by_character(&self, character_id: u32) -> Vec<HouseRef> {
        self.all()
            .into_iter()
            .filter(|h| h.data().unit.owner_id == character_id)
            .collect()
    }

    /// `GetHouseById(houseId)`.
    pub fn by_id(&self, id: u32) -> Option<HouseRef> {
        self.lock().houses.get(&id).cloned()
    }

    /// `GetHouseByTlId(houseTlId)`.
    pub fn by_tl(&self, tl: u16) -> Option<HouseRef> {
        self.lock().by_tl.get(&tl).cloned()
    }

    /// `_houses.ContainsKey(id)`.
    pub fn contains(&self, id: u32) -> bool {
        self.lock().houses.contains_key(&id)
    }

    /// Removes a house from `_houses` / `_housesTl` (`RemoveDeadHouse`).
    fn remove(&self, id: u32, tl: u16) {
        let mut inner = self.lock();
        inner.houses.remove(&id);
        inner.order.retain(|x| *x != id);
        inner.by_tl.remove(&tl);
    }

    /// `_removedHousings.Add(id)`.
    pub fn mark_removed(&self, id: u32) {
        self.lock().removed.push(id);
    }

    /// The pending `_removedHousings` (tests).
    pub fn removed_ids(&self) -> Vec<u32> {
        self.lock().removed.clone()
    }

    fn take_removed(&self) -> Vec<u32> {
        std::mem::take(&mut self.lock().removed)
    }

    /// `House.AttachedDoodads`.
    pub fn attached(&self, house_obj_id: u32) -> Vec<DoodadRef> {
        self.lock()
            .attached
            .get(&house_obj_id)
            .cloned()
            .unwrap_or_default()
    }

    fn set_attached(&self, house: &HouseRef, list: Vec<DoodadRef>) {
        let ids: Vec<u32> = list.iter().map(|d| d.obj_id).collect();
        if list.is_empty() {
            self.lock().attached.remove(&house.obj_id);
        } else {
            self.lock().attached.insert(house.obj_id, list);
        }
        house.data().attached_doodads = ids;
    }

    /// `AttachedDoodads.Add(doodad)`.
    pub fn add_attached(&self, house: &HouseRef, doodad: DoodadRef) {
        let mut list = self.attached(house.obj_id);
        list.push(doodad);
        self.set_attached(house, list);
    }

    /// `AttachedDoodads.Remove(doodad)` (first occurrence).
    pub fn remove_attached(&self, house: &HouseRef, doodad: &DoodadRef) {
        let mut list = self.attached(house.obj_id);
        if let Some(i) = list.iter().position(|d| Arc::ptr_eq(d, doodad)) {
            list.remove(i);
            self.set_attached(house, list);
        }
    }

    fn take_attached(&self, house: &HouseRef) -> Vec<DoodadRef> {
        let list = self
            .lock()
            .attached
            .remove(&house.obj_id)
            .unwrap_or_default();
        house.data().attached_doodads.clear();
        list
    }
}

// ---------------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------------

/// The house's `ParentWorld`.
pub fn world_of(server: &GameServer, house: &HouseRef) -> Option<Arc<WorldInstance>> {
    let instance = house.data().unit.transform.instance_id;
    server.world.world(instance)
}

/// `house.Template`.
pub fn template_of<'a>(server: &'a GameServer, house: &HouseRef) -> Option<&'a HousingTemplate> {
    let id = house.data().unit.template_id;
    server.game_data.systems.housing.template(id)
}

/// `house.BroadcastPacket(packet, false)` (players around the house).
pub fn broadcast(
    server: &GameServer,
    house: &HouseRef,
    packet: &dyn crate::net::packet::ServerPacket,
) {
    if let Some(world) = world_of(server, house) {
        crate::world::broadcast(&world, house, packet, false);
    }
}

/// `Character.SendErrorMessage(type)`.
pub fn send_error(player: &PlayerRef, error: i16) {
    player.send(crate::systems::npcs::doodad::packets::error_msg(error));
}

/// `ErrorMessageType` values housing sends.
pub mod error_message {
    pub const HOUSE_CANNOT_DECORATE: i16 = 122;
    pub const BAG_INVALID_ITEM: i16 = 181;
    pub const MAIL_NOT_ENOUGH_MONEY_TO_PAY_TAXES: i16 = 481;
    pub const INVALID_HOUSE_INFO: i16 = 614;
    pub const HOUSE_CANNOT_SELL_AS_NOT_ENOUGH_SEAL: i16 = 647;
    pub const HOUSE_CANNOT_SELL_AS_DESIGNATED_BUYER_NOT_FOUND: i16 = 648;
    pub const HOUSE_CANNOT_BUY_AS_NOT_FOR_SALE: i16 = 654;
    pub const HOUSE_CANNOT_BUY_AS_OWNER: i16 = 655;
    pub const HOUSE_CANNOT_BUY_AS_NOT_DESIGNATED_BUYER: i16 = 656;
    pub const HOUSE_CANNOT_BUY_AS_NOT_ENOUGH_MONEY: i16 = 657;
    pub const HOUSE_CANNOT_BUY_AS_SALE_INFO_CHANGED: i16 = 658;
}

/// The `Permission` setter (always-public templates force `Public`).
pub fn set_permission(h: &mut House, template: Option<&HousingTemplate>, value: u8) {
    h.permission = if template.is_some_and(|t| t.always_public) {
        housing_permission::PUBLIC
    } else {
        value
    };
    h.is_dirty = true;
}

/// `House.Expedition` (always the owner's guild): refreshes the stored expedition id.
pub fn refresh_expedition(server: &GameServer, house: &HouseRef) {
    let owner = house.data().unit.owner_id;
    let guild = if owner == 0 {
        0
    } else {
        server.expeditions.expedition_of_character(owner)
    };
    house.data().unit.expedition_id = guild;
}

/// `FactionManager.GetFaction(id)?.Id` (0 = null).
fn faction_or_null(server: &GameServer, faction_id: u32) -> u32 {
    if server.game_data.factions.get(faction_id).is_some() {
        faction_id
    } else {
        0
    }
}

// ---------------------------------------------------------------------------------------------
// Create / CurrentStep / AddBuildAction
// ---------------------------------------------------------------------------------------------

/// `HousingManager.Create(templateId, factionId, worldInstance, objectId, tlId)`: a new house
/// with the untouchable buff (not spawned, not registered). `None` for an unknown template.
pub fn create(
    server: &GameServer,
    template_id: u32,
    faction_id: u32,
    world: &Arc<WorldInstance>,
    obj_id: u32,
    tl_id: u16,
) -> Option<HouseRef> {
    let data = &server.game_data.systems.housing;
    let template = data.template(template_id)?;
    let tl = if tl_id > 0 {
        tl_id
    } else {
        server.ids.housing_tl.get_next_id().ok()? as u16
    };
    let obj_id = if obj_id > 0 {
        obj_id
    } else {
        server.ids.object.get_next_id().ok()?
    };
    // Id = template.Id (duplicate of TemplateId until the DB id is assigned)
    let mut h = House::new(template.id, template.id);
    h.unit.tl_id = tl;
    h.unit.level = 1;
    h.unit.faction_id = faction_or_null(server, faction_id);
    h.unit.name = data.localized_house_name(template.id);
    h.unit.transform.instance_id = world.id;
    h.unit.transform.world_id = world.template.id;
    // Template setter (AllAction) and the template-derived values the packets read.
    h.all_action = template.all_actions();
    h.build_step_count = template.build_steps.len() as i32;
    h.tax = template.tax_or_zero() as i32;
    h.unit.max_hp = template.hp;
    h.unit.hp = template.hp;
    h.unit.model_id = template.main_model_id;
    // Force public on always public properties on create
    if template.always_public {
        set_permission(&mut h, Some(template), housing_permission::PUBLIC);
    }
    h.is_dirty = true;
    let house = Entity::new(obj_id, h);
    set_untouchable(server, &house, true);
    Some(house)
}

/// The `CurrentStep` setter: model, bound doodads (finished house) or their removal, and
/// `BaseAction`. `loading` = `IsBeingLoadedFromDb` (bound doodads come from the DB instead).
pub fn set_current_step(server: &GameServer, house: &HouseRef, value: i32, loading: bool) {
    let Some(template) = template_of(server, house) else {
        tracing::error!("House {}: unknown template", house.obj_id);
        return;
    };
    let model = if value == -1 {
        Some(template.main_model_id)
    } else {
        template.build_steps.get(&value).map(|s| s.model_id)
    };
    {
        let mut h = house.data();
        h.current_step = value;
        h.is_dirty = true;
        match model {
            Some(m) => h.unit.model_id = m,
            None => {
                // C#: KeyNotFoundException in the setter (the step is kept).
                tracing::error!(
                    "House {}: template {} has no build step {value}",
                    h.id,
                    template.id
                );
                return;
            }
        }
    }
    let manager = &server.systems.housing.manager;
    if value == -1 {
        if !loading {
            for binding in &template.binding_doodads {
                if let Some(d) = create_bound_doodad(server, house, binding) {
                    let persistent = server.config.world.use_persistent_house_doodads;
                    if let Some(world) = world_of(server, house) {
                        if persistent {
                            d.data().is_persistent = true;
                            crate::systems::npcs::doodad::manager::init_doodad(server, &world, &d);
                            crate::systems::npcs::doodad::db::save(server, &d);
                        } else {
                            crate::systems::npcs::doodad::manager::init_doodad(server, &world, &d);
                        }
                    }
                    manager.add_attached(house, d);
                }
            }
        }
    } else {
        let attached = manager.take_attached(house);
        let world = world_of(server, house);
        for d in attached {
            let persistent = d.data().is_persistent;
            if persistent {
                server.ids.object.release_id(d.obj_id);
                if let Some(w) = &world {
                    crate::systems::npcs::doodad::manager::delete(server, w, &d);
                }
            } else {
                server.ids.object.release_id(d.obj_id);
                // Deviation: the C# leaves the (never spawned / hidden) doodad object behind.
                if let Some(w) = &world {
                    w.detach_child(&WorldObject::from(&d));
                    w.remove_object(d.obj_id);
                }
            }
        }
    }
    if value > 0 {
        let mut base = 0;
        for i in 0..value {
            match template.build_steps.get(&i) {
                Some(s) => base += s.num_actions,
                None => {
                    tracing::error!("House template {}: no build step {i}", template.id);
                    return;
                }
            }
        }
        let mut h = house.data();
        h.base_action = base;
        h.is_dirty = true;
    }
}

/// One `HousingBindingDoodad` of a finished house: `DoodadManager.Create(ParentWorld, 0,
/// doodadId, house, true)`, attach point, `Transform = house.Transform.CloneDetached`, parent
/// and `ApplyWorldSpawnPositionWithDeg(binding.Position)`.
pub fn create_bound_doodad(
    server: &GameServer,
    house: &HouseRef,
    binding: &HousingBindingDoodad,
) -> Option<DoodadRef> {
    let world = world_of(server, house)?;
    let house_obj = WorldObject::from(house);
    let Some(d) = crate::systems::npcs::doodad::manager::create(
        server,
        &world,
        0,
        binding.doodad_id,
        Some(&house_obj),
        true,
    ) else {
        let id = house.data().id;
        tracing::error!(
            "CurrentStep: Failed to create bound doodad templateId={} for house {id} — template not found, skipping.",
            binding.doodad_id
        );
        return None;
    };
    let ht = house.data().unit.transform;
    {
        let mut dd = d.data();
        dd.attach_point = binding.attach_point_id;
        dd.transform.instance_id = ht.instance_id;
        dd.transform.world_id = ht.world_id;
        dd.transform.zone_id = ht.zone_id;
        let p = &binding.position;
        dd.transform.set_position(
            p.x,
            p.y,
            p.z,
            p.roll.to_radians(),
            p.pitch.to_radians(),
            p.yaw.to_radians(),
        );
    }
    world.attach_child(&house_obj, &WorldObject::from(&d));
    Some(d)
}

/// `House.AddBuildAction()`.
pub fn add_build_action(server: &GameServer, house: &HouseRef) {
    let Some(template) = template_of(server, house) else {
        return;
    };
    let next_step = {
        let mut h = house.data();
        if h.current_step == -1 {
            return;
        }
        let Some(step) = template.build_steps.get(&h.current_step) else {
            tracing::error!("House {}: no build step {}", h.id, h.current_step);
            return;
        };
        let next_action = h.num_action + 1;
        if step.num_actions > next_action {
            h.num_action = next_action;
            h.is_dirty = true;
            return;
        }
        h.num_action = 0;
        h.is_dirty = true;
        let next = h.current_step + 1;
        if (template.build_steps.len() as i32) > next {
            next
        } else {
            -1
        }
    };
    set_current_step(server, house, next_step, false);
}

// ---------------------------------------------------------------------------------------------
// Spawn / Delete
// ---------------------------------------------------------------------------------------------

/// `House.Spawn()`: the house (`GameObject.Spawn`), then every attached doodad.
pub fn spawn_house(server: &GameServer, house: &HouseRef) {
    let Some(world) = world_of(server, house) else {
        tracing::error!("Tried to spawn a object without a owning parent world");
        return;
    };
    let attached = server.systems.housing.manager.attached(house.obj_id);
    // The C# `AttachedDoodads` are objects before they are spawned: register them so the
    // house's `SCDoodadsCreatedPacket` can list them.
    for d in &attached {
        world.add_object(d);
    }
    let changes = world.spawn(house, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
    for d in &attached {
        crate::systems::npcs::doodad::manager::spawn(server, &world, d);
    }
}

/// `House.Delete()`: persistent attached doodads are deleted (DB row included), the others
/// detached; obj ids released; then `GameObject.Delete`.
pub fn delete_house(server: &GameServer, house: &HouseRef) {
    let world = world_of(server, house);
    let attached = server.systems.housing.manager.attached(house.obj_id);
    for d in &attached {
        let (persistent, attach_point) = {
            let dd = d.data();
            (dd.is_persistent, dd.attach_point)
        };
        if persistent {
            server.ids.object.release_id(d.obj_id);
            if let Some(w) = &world {
                crate::systems::npcs::doodad::manager::delete(server, w, d);
            }
        } else {
            if let Some(w) = &world {
                if attach_point == crate::models::unit::attach_point::NONE {
                    w.detach_child(&WorldObject::from(d));
                }
            }
            server.ids.object.release_id(d.obj_id);
        }
    }
    if let Some(w) = &world {
        w.delete(house);
        // Deviation: the non-persistent attached doodads stay in the C# object table.
        for d in &attached {
            if !d.data().is_persistent {
                w.remove_object(d.obj_id);
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// AllowedToInteract / GetHouseAtLocation / act ability bonus
// ---------------------------------------------------------------------------------------------

/// `House.AllowedToInteract(player)`.
pub fn allowed_to_interact(server: &GameServer, house: &HouseRef, player: &PlayerRef) -> bool {
    let always_public = template_of(server, house).is_some_and(|t| t.always_public);
    let (step, permission, owner) = {
        let h = house.data();
        (h.current_step, h.permission, h.unit.owner_id)
    };
    if always_public {
        return true;
    }
    if step != -1 {
        // unfinished houses can't be used to private store, so always true
        return true;
    }
    let (char_id, account_id, family, expedition) = {
        let c = player.data();
        (c.id, c.account_id, c.family, c.unit.expedition_id)
    };
    if char_id == owner {
        return true;
    }
    match permission {
        housing_permission::PRIVATE => {
            let owner_account = server.names.get_character_account(owner);
            account_id != 0 && account_id == owner_account
        }
        housing_permission::FAMILY if family > 0 => server
            .systems
            .social
            .families
            .family(family)
            .is_some_and(|f| f.members.iter().any(|m| m.id == owner)),
        housing_permission::GUILD if expedition > 0 => server
            .expeditions
            .get(expedition)
            .is_some_and(|e| e.members.iter().any(|m| m.character_id == owner)),
        housing_permission::PUBLIC => true,
        _ => false,
    }
}

/// `GetHouseAtLocation(x, y)`: the first house whose garden square contains the point
/// (`RectangleF.Contains`).
pub fn house_at_location(server: &GameServer, x: f32, y: f32) -> Option<HouseRef> {
    for house in server.systems.housing.manager.all() {
        let Some(t) = template_of(server, &house) else {
            continue;
        };
        let r = t.garden_radius;
        let (hx, hy) = {
            let h = house.data();
            (h.unit.transform.x, h.unit.transform.y)
        };
        let (bx, by, w, hgt) = (hx - r, hy - r, r * 2.0, r * 2.0);
        if bx <= x && x < bx + w && by <= y && y < by + hgt {
            return Some(house);
        }
    }
    None
}

/// `GetActAbilityBonusFromHouse(actabilityGroupId, house)`.
pub fn act_ability_bonus_from_house(
    server: &GameServer,
    actability_group_id: i32,
    house: &HouseRef,
) -> u32 {
    if actability_group_id <= 0 {
        return 0;
    }
    let Some(world) = world_of(server, house) else {
        return 0;
    };
    let id = house.data().id;
    let data = &server.game_data.systems.housing;
    let mut seen: Vec<(u32, u32)> = Vec::new(); // every furniture type only counts once
    for f in world.doodads_by_house_db_id(id) {
        let (attach, template_id) = {
            let d = f.data();
            (d.attach_point, d.template_id)
        };
        if attach != crate::models::unit::attach_point::NONE {
            continue;
        }
        if template_id == FOR_SALE_MARKER_DOODAD_ID {
            continue;
        }
        if let Some(deco) = data.decoration_by_doodad(template_id) {
            if deco.actability_group_id as i32 == actability_group_id
                && !seen.iter().any(|(t, _)| *t == template_id)
            {
                seen.push((template_id, deco.actability_up));
            }
        }
    }
    seen.iter().map(|(_, b)| *b).sum()
}

// ---------------------------------------------------------------------------------------------
// Buffs / factions
// ---------------------------------------------------------------------------------------------

fn set_house_buff(server: &GameServer, house: &HouseRef, buff_id: u32, on: bool, what: &str) {
    let obj = WorldObject::from(house);
    let has = house.data().unit.buffs.check_buff(buff_id);
    if on {
        if has {
            return;
        }
        let Some(t) = server.game_data.systems.skills.buff(buff_id).cloned() else {
            tracing::error!("Unable to find {what} template");
            return;
        };
        let caster = crate::systems::skills::types::SkillCaster::Unit(house.obj_id);
        let b = crate::systems::skills::buffs::new_buff(
            server,
            &t,
            Some(&obj),
            &caster,
            None,
            crate::world::now_ms(),
        );
        crate::systems::skills::buffs::add_buff(server, &obj, b, 0, 0);
    } else if has {
        crate::systems::skills::buffs::remove_buff(server, &obj, buff_id);
    }
}

/// `SetUntouchable(house, isUntouchable)`.
pub fn set_untouchable(server: &GameServer, house: &HouseRef, on: bool) {
    set_house_buff(server, house, BUFF_UNTOUCHABLE, on, "Untouchable buff");
}

/// `SetRemovalDebuff(house, isDeteriorating)`.
pub fn set_removal_debuff(server: &GameServer, house: &HouseRef, on: bool) {
    set_house_buff(server, house, BUFF_REMOVAL_DEBUFF, on, "Removal Debuff");
}

/// `UpdateHouseFaction(house, factionId)`.
pub fn update_house_faction(server: &GameServer, house: &HouseRef, faction_id: u32) {
    let (name, old) = {
        let h = house.data();
        (h.unit.name.clone(), h.unit.faction_id)
    };
    if let Some(world) = world_of(server, house) {
        crate::world::broadcast(
            &world,
            house,
            &crate::systems::skills::packets::SCUnitFactionChangedPacket {
                obj_id: house.obj_id,
                unit_name: name,
                id: old,
                id2: faction_id,
                temp: false,
            },
            true,
        );
    }
    house.data().unit.faction_id = faction_or_null(server, faction_id);
}

/// `UpdateOwnedHousingFaction(characterId, factionId)`.
pub fn update_owned_housing_faction(server: &GameServer, character_id: u32, faction_id: u32) {
    for h in server.systems.housing.manager.by_character(character_id) {
        let current = h.data().unit.faction_id;
        if current == 0 || current != faction_id {
            update_house_faction(server, &h, faction_id);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Taxes
// ---------------------------------------------------------------------------------------------

/// The `out` values of `CalculateBuildingTaxInfo`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct TaxInfo {
    pub total_tax_to_pay: i32,
    pub heavy_house_count: i32,
    pub normal_house_count: i32,
    pub hostile_tax_rate: i32,
    pub one_week_tax_count: i32,
}

/// `CalculateBuildingTaxInfo(accountId, newHouseTemplate, buildingNewHouse, out ...)`:
/// `Ok((returned, info))`; `Err` where the C# throws (`Taxation` is null).
pub fn calculate_building_tax_info(
    server: &GameServer,
    account_id: u32,
    template: &HousingTemplate,
    building_new_house: bool,
) -> Result<(bool, TaxInfo), String> {
    let mut info = TaxInfo::default();
    let user_houses = server.systems.housing.manager.by_account(account_id);
    if user_houses.is_empty() {
        return Ok((false, info));
    }
    for h in &user_houses {
        let heavy = template_of(server, h).is_some_and(|t| t.heavy_tax);
        if heavy {
            info.heavy_house_count += 1;
        } else {
            info.normal_house_count += 1;
        }
    }
    if building_new_house {
        if template.heavy_tax {
            info.heavy_house_count += 1;
        } else {
            info.normal_house_count += 1;
        }
    }
    // Default Heavy Tax formula for 1.2
    let mut multiplier = info.heavy_house_count.min(MAX_HEAVY_TAX_COUNTED) as f32 * 0.5f32;
    if info.heavy_house_count < 3 || !template.heavy_tax {
        multiplier = 1.0;
    }
    let Some(taxation) = template.taxation else {
        return Err(format!(
            "housing template {} has no taxation (NullReferenceException)",
            template.id
        ));
    };
    let weekly = f64::from(taxation.tax as f32 * multiplier).ceil() as i32;
    info.total_tax_to_pay = weekly;
    info.one_week_tax_count = weekly;
    if building_new_house {
        info.total_tax_to_pay = info
            .total_tax_to_pay
            .wrapping_add(taxation.tax.wrapping_mul(2) as i32);
    }
    Ok((true, info))
}

/// `MailForTax.UpdateTaxInfo(mail, house)` as the assignments it makes.
pub fn tax_mail_update(server: &GameServer, house: &HouseRef) -> TaxMailUpdate {
    let mut u = TaxMailUpdate::default();
    let h = house.data().clone();
    let Some(owner_name) = server.names.get_character_name(h.unit.owner_id) else {
        return u;
    };
    u.receiver = Some((h.unit.owner_id, owner_name));
    let Some(zone) = server.game_data.zones.zone_by_key(h.unit.transform.zone_id) else {
        return u;
    };
    let group = zone.group_id;
    u.title = Some(format!("title({group})"));
    let Some(template) = server
        .game_data
        .systems
        .housing
        .template(h.unit.template_id)
    else {
        return u;
    };
    let Ok((true, info)) = calculate_building_tax_info(server, h.account_id, template, false)
    else {
        return u;
    };
    let now = now();
    let mut late_fees = 0;
    let mut deadline = tax_due_date(&h);
    if tax_due_date(&h) <= now {
        late_fees = 1;
        deadline = h.protection_end_date;
    }
    let tax = template.tax_or_zero();
    let text = format!(
        "body('{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}')",
        h.unit.name,
        unix_or_zero(h.place_date),
        unix_or_zero(h.protection_end_date),
        unix_or_zero(deadline),
        tax,
        info.hostile_tax_rate,
        info.heavy_house_count,
        late_fees,
        info.total_tax_to_pay,
        if template.heavy_tax { "true" } else { "false" },
        info.normal_house_count
    );
    let extra_unknown: u16 = 0;
    let extra = (i64::from(group) << 48) + (i64::from(extra_unknown) << 32) + i64::from(h.id);
    u.body = Some((text, info.total_tax_to_pay, extra));
    u
}

/// `Helpers.UnixTime` (dates before the epoch are 0).
fn unix_or_zero(t: UnixTime) -> i64 {
    t.max(0)
}

/// `new MailForTax(house)` + `FinalizeMail()` (the mail is sent whatever `FinalizeMail`
/// returned, as in `UpdateTaxInfo`).
pub fn new_tax_mail(server: &GameServer, house: &HouseRef) -> HouseMail {
    let now_ms = crate::world::now_ms();
    let mut mail = HouseMail {
        mail_type: mail_type::BILLING,
        recv_date_ms: now_ms,
        sender_id: 0,
        sender_name: ".houseTax".into(),
        ..HouseMail::default()
    };
    let u = tax_mail_update(server, house);
    if let Some((id, name)) = u.receiver {
        mail.receiver_id = id;
        mail.receiver_name = Some(name);
    }
    if let Some(t) = u.title {
        mail.title = t;
    }
    if let Some((text, billing, extra)) = u.body {
        mail.text = text;
        mail.billing_amount = billing;
        mail.extra = extra;
        mail.status = MAIL_STATUS_UNPAID;
    }
    mail
}

/// `UpdateTaxInfo(house)`: untouchable / removal buffs, then the tax mails of an owned house.
pub fn update_tax_info(server: &GameServer, house: &HouseRef) {
    let now = now();
    let (is_demolished, is_tax_due, owner, id) = {
        let h = house.data();
        (
            h.protection_end_date <= now,
            tax_due_date(&h) <= now,
            h.unit.owner_id,
            h.id,
        )
    };
    set_untouchable(server, house, !is_demolished);
    set_removal_debuff(server, house, is_demolished);
    if owner == 0 {
        return;
    }
    let hooks = server.systems.housing.hooks.get();
    if is_demolished {
        hooks.delete_house_mails(server, id);
    } else if is_tax_due {
        let mails = hooks.house_mail_ids(server, id);
        if mails.is_empty() {
            let mail = new_tax_mail(server, house);
            hooks.send_mail(server, mail);
            tracing::trace!("New Tax Mail sent for house {id} owned by {owner}");
        } else {
            let update = tax_mail_update(server, house);
            for m in mails {
                hooks.update_tax_mail(server, m, &update);
                tracing::trace!("Tax Mail {m} updated for house {id} owned by {owner}");
            }
        }
    }
}

/// `PayWeeklyTax(house)` (called by the mail subsystem when a tax bill is paid).
pub fn pay_weekly_tax(server: &GameServer, house: &HouseRef) -> bool {
    let days = i64::from(server.config.world.days_for_tax_payment);
    let mut h = house.data();
    h.protection_end_date += days * DAY;
    h.is_dirty = true;
    true
}

/// `CheckHousingTaxes()` (the `HousingTaxTask`, every 10 s).
pub fn check_housing_taxes(server: &GameServer) {
    let manager = &server.systems.housing.manager;
    if manager.checking_tax.swap(true, Ordering::SeqCst) {
        return;
    }
    let now = now();
    let mut expired = Vec::new();
    for house in manager.all() {
        let (end, owner) = {
            let h = house.data();
            (h.protection_end_date, h.unit.owner_id)
        };
        if end <= now && owner > 0 {
            expired.push(house.clone());
        }
        update_tax_info(server, &house);
    }
    for house in expired {
        super::ops::demolish(server, None, &house, true, false);
    }
    manager.checking_tax.store(false, Ordering::SeqCst);
}

/// Starts the `HousingTaxTask` (first run after 30 s, then every 10 s).
pub fn start_tax_task(server: &Arc<GameServer>) {
    let Ok(rt) = tokio::runtime::Handle::try_current() else {
        return;
    };
    let weak = Arc::downgrade(server);
    rt.spawn(async move {
        tokio::time::sleep(std::time::Duration::from_secs(30)).await;
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(10));
        loop {
            tick.tick().await;
            let Some(server) = weak.upgrade() else {
                return;
            };
            check_housing_taxes(&server);
        }
    });
    tracing::info!("Started Housing Tax Timer");
}

// ---------------------------------------------------------------------------------------------
// Load / SpawnAll / ReconcileBoundDoodads / Save
// ---------------------------------------------------------------------------------------------

/// One `housings` row.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct HouseRow {
    pub id: u32,
    pub account_id: u32,
    pub owner: u32,
    pub co_owner: u32,
    pub template_id: u32,
    pub name: String,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub yaw: f32,
    pub pitch: f32,
    pub roll: f32,
    pub current_step: i32,
    pub current_action: i32,
    pub permission: u8,
    pub place_date: UnixTime,
    pub protected_until: UnixTime,
    pub faction_id: u32,
    pub sell_to: u32,
    pub sell_price: u32,
    pub allow_recover: bool,
}

/// The per-row part of `LoadPlayerHousing`: creates and registers the house (not spawned).
pub fn load_house_row(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    r: &HouseRow,
) -> Option<HouseRef> {
    let Some(house) = create(server, r.template_id, r.faction_id, world, 0, 0) else {
        // C#: NullReferenceException on the null house aborts the load.
        tracing::error!(
            "LoadPlayerHousing: house {} has unknown template {}",
            r.id,
            r.template_id
        );
        return None;
    };
    let template = template_of(server, &house);
    {
        let mut h = house.data();
        h.id = r.id;
        h.account_id = r.account_id;
        h.unit.owner_id = r.owner;
        h.co_owner_id = r.co_owner;
        h.unit.name = r.name.clone();
        h.unit
            .transform
            .set_position(r.x, r.y, r.z, r.roll, r.pitch, r.yaw);
        h.unit.transform.instance_id = world.id;
        h.unit.transform.zone_id = world.template.zone_id_at(r.x, r.y);
    }
    let loading = server.config.world.use_persistent_house_doodads;
    set_current_step(server, &house, r.current_step, loading);
    {
        let mut h = house.data();
        h.num_action = r.current_action;
        set_permission(&mut h, template, r.permission);
        h.place_date = r.place_date;
        h.protection_end_date = r.protected_until;
        h.sell_to_player_id = r.sell_to;
        h.sell_price = r.sell_price;
        h.allow_recover = r.allow_recover;
    }
    if !server.systems.housing.manager.insert(&house) {
        tracing::error!(
            "LoadPlayerHousing: duplicate house id {} / tl (ArgumentException)",
            r.id
        );
        return None;
    }
    {
        // Manually placed houses (or after upgrading MySQL), will get 2 weeks for free
        let mut h = house.data();
        if h.place_date == h.protection_end_date {
            h.protection_end_date = h.place_date + 14 * DAY;
        }
    }
    refresh_expedition(server, &house);
    update_tax_info(server, &house);
    house.data().is_dirty = false;
    Some(house)
}

/// `LoadPlayerHousing(worldInstance)` (+ the tax timer).
pub async fn load_player_housing(
    server: &Arc<GameServer>,
    world: &Arc<WorldInstance>,
) -> anyhow::Result<usize> {
    server.systems.housing.manager.clear();
    tracing::info!("Loading Player Buildings ...");
    let rows = super::db::load_rows(&server.db).await?;
    for r in &rows {
        load_house_row(server, world, r);
    }
    let n = server.systems.housing.manager.count();
    tracing::info!("Loaded {n} Player Buildings");
    start_tax_task(server);
    Ok(n)
}

/// `SpawnAll()` (houses always go to the `main_world` instance).
pub fn spawn_all(server: &GameServer) {
    for house in server.systems.housing.manager.all() {
        house.data().unit.transform.instance_id = crate::models::character::DEFAULT_INSTANCE_ID;
        spawn_house(server, &house);
    }
}

/// The `SpawnPersistentDoodads` housing part for one loaded doodad (`houseId > 0` and no parent
/// doodad): parent it to its house and, with `UsePersistentHouseDoodads`, register a bound
/// doodad in `AttachedDoodads`. False when the house does not exist.
pub fn attach_loaded_doodad(
    server: &GameServer,
    world: &WorldInstance,
    house_id: u32,
    doodad: &DoodadRef,
) -> bool {
    let Some(house) = server.systems.housing.manager.by_id(house_id) else {
        return false;
    };
    doodad.data().parent_obj_id = house.obj_id;
    world.attach_child(&WorldObject::from(&house), &WorldObject::from(doodad));
    if server.config.world.use_persistent_house_doodads {
        let (template_id, attach_point) = {
            let d = doodad.data();
            (d.template_id, d.attach_point)
        };
        let bound = template_of(server, &house).is_some_and(|t| {
            t.binding_doodads
                .iter()
                .any(|b| b.doodad_id == template_id && b.attach_point_id == attach_point)
        });
        if bound {
            server
                .systems
                .housing
                .manager
                .add_attached(&house, doodad.clone());
        }
    }
    true
}

/// `ReconcileBoundDoodads()`: spawns + saves missing bound doodads of finished houses and
/// deletes duplicates. Returns (added, removed).
pub fn reconcile_bound_doodads(server: &GameServer) -> (usize, usize) {
    tracing::info!("Reconciling bound doodads for completed houses...");
    let manager = &server.systems.housing.manager;
    let (mut added, mut removed) = (0, 0);
    for house in manager.all() {
        if house.data().current_step != -1 {
            continue;
        }
        let Some(template) = template_of(server, &house) else {
            continue;
        };
        if template.binding_doodads.is_empty() {
            continue;
        }
        let house_id = house.data().id;
        for binding in &template.binding_doodads {
            let matches: Vec<DoodadRef> = manager
                .attached(house.obj_id)
                .into_iter()
                .filter(|d| {
                    let d = d.data();
                    d.template_id == binding.doodad_id && d.attach_point == binding.attach_point_id
                })
                .collect();
            if matches.is_empty() {
                tracing::debug!(
                    "Reconcile: Spawning missing bound doodad templateId={} attachPoint={} for house {house_id}",
                    binding.doodad_id,
                    binding.attach_point_id
                );
                let Some(world) = world_of(server, &house) else {
                    continue;
                };
                let Some(d) = create_bound_doodad(server, &house, binding) else {
                    tracing::error!(
                        "Reconcile: Failed to create doodad templateId={} for house {house_id} — template not found, skipping.",
                        binding.doodad_id
                    );
                    continue;
                };
                d.data().is_persistent = true;
                crate::systems::npcs::doodad::manager::init_doodad(server, &world, &d);
                crate::systems::npcs::doodad::manager::spawn(server, &world, &d);
                crate::systems::npcs::doodad::db::save(server, &d);
                manager.add_attached(&house, d.clone());
                server
                    .systems
                    .npcs
                    .spawns
                    .get_or_create(&world)
                    .add_player_doodad(d);
                added += 1;
            } else if matches.len() > 1 {
                tracing::warn!(
                    "Reconcile: Removing {} duplicate(s) for templateId={} attachPoint={} on house {house_id}",
                    matches.len() - 1,
                    binding.doodad_id,
                    binding.attach_point_id
                );
                for extra in &matches[1..] {
                    manager.remove_attached(&house, extra);
                    server.ids.object.release_id(extra.obj_id);
                    if let Some(world) = world_of(server, &house) {
                        crate::systems::npcs::doodad::manager::delete(server, &world, extra);
                    }
                    removed += 1;
                }
            }
        }
    }
    tracing::info!(
        "Bound doodad reconciliation complete: {added} added, {removed} duplicates removed."
    );
    (added, removed)
}

/// `RemoveDeadHouse(house)` (`House.OnDeath`).
pub fn remove_dead_house(server: &GameServer, house: &HouseRef) {
    let (id, tl) = {
        let h = house.data();
        (h.id, h.unit.tl_id)
    };
    let manager = &server.systems.housing.manager;
    manager.mark_removed(id);
    manager.remove(id, tl);
    server.ids.housing_tl.release_id(u32::from(tl));
    server.ids.housing.release_id(id);
    // TODO (C#): not sure how to handle this, just instant delete it for now
    delete_house(server, house);
}

/// `HousingManager.Save(connection, transaction)`: (updated, deleted).
pub async fn save(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
) -> crate::Result<(usize, usize)> {
    let manager = &server.systems.housing.manager;
    let mut delete_count = 0;
    let removed = manager.take_removed();
    if !removed.is_empty() {
        super::db::delete_rows(tx, &removed).await?;
        delete_count += 1;
    }
    let mut update_count = 0;
    for house in manager.all() {
        let Some(row) = save_row(&house) else {
            continue;
        };
        super::db::write_row(tx, &row).await?;
        house.data().is_dirty = false;
        update_count += 1;
    }
    Ok((update_count, delete_count))
}

/// The `House.Save` row (`None` = not dirty, or a recently destroyed / expired house).
pub fn save_row(house: &HouseRef) -> Option<HouseRow> {
    let h = house.data();
    if !h.is_dirty {
        return None;
    }
    if h.account_id == 0 || h.unit.owner_id == 0 {
        return None;
    }
    let t = &h.unit.transform;
    Some(HouseRow {
        id: h.id,
        account_id: h.account_id,
        owner: h.unit.owner_id,
        co_owner: h.co_owner_id,
        template_id: h.unit.template_id,
        name: h.unit.name.clone(),
        x: t.x,
        y: t.y,
        z: t.z,
        roll: t.rot_x,
        pitch: t.rot_y,
        yaw: t.rot_z,
        current_step: h.current_step,
        current_action: h.num_action,
        permission: h.permission,
        place_date: h.place_date,
        protected_until: h.protection_end_date,
        faction_id: h.unit.faction_id,
        sell_to: h.sell_to_player_id,
        sell_price: h.sell_price,
        allow_recover: h.allow_recover,
    })
}

/// `doodad_owner_type::HOUSING` doodads of a house (`GetDoodadByHouseDbId`).
pub fn house_doodads(server: &GameServer, house: &HouseRef) -> Vec<DoodadRef> {
    let Some(world) = world_of(server, house) else {
        return Vec::new();
    };
    let id = house.data().id;
    world.doodads_by_house_db_id(id)
}
