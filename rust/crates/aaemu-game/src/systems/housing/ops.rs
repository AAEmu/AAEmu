//! The player-driven `HousingManager` operations: `ConstructHouseTax`, `HouseTaxInfo`, `Build`,
//! `ChangeHousePermission`, `ChangeHouseName`, `Demolish`, `ReturnHouseItemsToOwner`,
//! `SetForSaleMarkers`, `SetForSale`, `CancelForSale`, `UpdateFurnitureOwner`, `BuyHouse`,
//! `DecorateHouse`, `HousingToggleAllowRecover`, plus the `Building` branch of `CraftEffect`
//! (build steps through skills).

use crate::models::house::{housing_permission, House};
use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{HouseRef, PlayerRef, WorldObject};

use super::hooks::{
    item_task, mail_type, slot_type, HouseConstructionError, HouseItem, HouseMail, MailAttachment,
};
use super::manager::{
    self, broadcast, error_message as err, now, send_error, set_permission, tax_due_date,
    template_of, update_house_faction, update_tax_info, world_of, APPRAISAL_CERTIFICATE,
    BOUND_TAX_CERTIFICATE, COPPER_PER_CERTIFICATE, FACTION_MONSTROSITY, FOR_SALE_MARKER_DOODAD_ID,
    HOURS_FOR_FAILED_TAX_TO_RETURN_HOUSE,
};
use super::packets::{
    CSCreateHousePacket, CSDecorateHousePacket, SCConstructHouseTaxPacket,
    SCHouseBuildProgressPacket, SCHouseDemolishedPacket, SCHousePermissionChangedPacket,
    SCHouseResetForSalePacket, SCHouseSetForSalePacket, SCHouseSoldPacket, SCHouseTaxInfoPacket,
    SCHousingRecoverTogglePacket, SCMyHousePacket, SCMyHouseRemovedPacket,
};

const DAY: i64 = 24 * 3600;

/// `house.Write(stream)` with the owner / buyer names (`NameManager.GetCharacterName ?? ""`).
pub fn house_body(server: &GameServer, house: &HouseRef) -> Vec<u8> {
    let (owner, sell_to) = {
        let h = house.data();
        (h.unit.owner_id, h.sell_to_player_id)
    };
    let owner_name = server.names.get_character_name(owner).unwrap_or_default();
    let sell_to_name = server.names.get_character_name(sell_to).unwrap_or_default();
    let h = house.data();
    // Houses have no parent: `Transform.World` is the local transform.
    let pos = h.unit.transform.local();
    let mut s = aaemu_commons::PacketStream::new();
    h.write(house.obj_id, &pos, &owner_name, &sell_to_name, &mut s);
    s.into_bytes()
}

/// `new SCMyHousePacket(house)`.
pub fn my_house_packet(server: &GameServer, house: &HouseRef) -> SCMyHousePacket {
    SCMyHousePacket {
        body: house_body(server, house),
    }
}

/// `new SCLoginCharInfoHouse(id, house)`.
pub fn login_char_info_house_packet(
    server: &GameServer,
    id: u32,
    house: &HouseRef,
) -> super::packets::SCLoginCharInfoHousePacket {
    let mut body = id.to_le_bytes().to_vec();
    body.extend(house_body(server, house));
    super::packets::SCLoginCharInfoHousePacket { body }
}

fn player_ids(player: &PlayerRef) -> (u32, u32, u32, String) {
    let c = player.data();
    (c.id, c.account_id, c.unit.faction_id, c.unit.name.clone())
}

// ---------------------------------------------------------------------------------------------
// taxes
// ---------------------------------------------------------------------------------------------

/// `ConstructHouseTax(connection, designId, x, y, z)`.
pub fn construct_house_tax(
    server: &GameServer,
    player: &PlayerRef,
    design_id: u32,
    x: f32,
    y: f32,
    _z: f32,
) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(template) = server.game_data.systems.housing.template(design_id) else {
        tracing::error!("ConstructHouseTax: unknown design {design_id} (NullReferenceException)");
        return;
    };
    let account = player.data().account_id;
    let instance = player.data().unit.transform.instance_id;
    let info = match super::dominion::tax::quote_at(server, account, template, true, instance, x, y)
    {
        Ok(quote) => quote.info,
        Err(e) => {
            tracing::error!("ConstructHouseTax: {e}");
            return;
        }
    };
    let base_tax = template.tax_or_zero() as i32;
    let deposit_tax = base_tax.wrapping_mul(2);
    player.send(SCConstructHouseTaxPacket {
        design_id,
        heavy_tax_house_count: info.heavy_house_count,
        normal_tax_house_count: info.normal_house_count,
        is_heavy_tax_house: template.heavy_tax,
        base_tax_money_amount: base_tax,
        deposit_tax_money_amount: deposit_tax,
        total_tax_money_amount: info.total_tax_to_pay,
    });
}

/// `HouseTaxInfo(connection, tlId)` (name plaque).
pub fn house_tax_info(server: &GameServer, player: &PlayerRef, tl: u16) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(house) = server.systems.housing.manager.by_tl(tl) else {
        return;
    };
    let Some(template) = template_of(server, &house) else {
        return;
    };
    let info = match super::dominion::tax::quote_house(server, &house) {
        Ok(quote) => quote.info,
        Err(e) => {
            tracing::error!("HouseTaxInfo: {e}");
            return;
        }
    };
    let base_tax = template.tax_or_zero() as i32;
    let deposit_tax = base_tax.wrapping_mul(2);
    let h = house.data().clone();
    let now = now();
    let weeks_without_pay = if h.protection_end_date <= now {
        1
    } else if tax_due_date(&h) <= now {
        0
    } else {
        -1
    };
    player.send(SCHouseTaxInfoPacket {
        tl: h.unit.tl_id,
        dominion_tax_rate: info.hostile_tax_rate,
        money_amount: deposit_tax,
        money_amount2: info.total_tax_to_pay,
        due: h.protection_end_date,
        is_already_paid: weeks_without_pay < 0,
        weeks_without_pay,
        is_heavy_tax_house: template.heavy_tax,
    });
}

// ---------------------------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------------------------

/// `Build(connection, designId, posX, posY, posZ, zRot, itemId, moneyAmount, ht,
/// autoUseAaPoint)`.
pub fn build(server: &GameServer, player: &PlayerRef, account_id: u32, p: &CSCreateHousePacket) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let hooks = server.systems.housing.hooks.get();
    let (char_id, char_account, faction_id, _) = player_ids(player);
    if account_id != char_account || ![p.x, p.y, p.z, p.z_rot].iter().all(|v| v.is_finite()) {
        send_error(player, err::INVALID_HOUSE_INFO);
        return;
    }
    let source = hooks.inventory_item(server, player, p.item_id).filter(|i| {
        i.owner_id == u64::from(char_id) && i.count > 0 && i.slot_type == slot_type::INVENTORY
    });
    let Some(source) = source else {
        // Invalid itemId supplied or the id is not owned by the user
        send_error(player, err::BAG_INVALID_ITEM);
        return;
    };
    let Some(template) = server.game_data.systems.housing.template(p.design_id) else {
        tracing::error!(
            "Build: unknown design {} (NullReferenceException)",
            p.design_id
        );
        return;
    };
    if server
        .game_data
        .systems
        .housing
        .item_id_by_design(p.design_id, |item_id| item_id == source.template_id)
        == 0
    {
        send_error(player, err::BAG_INVALID_ITEM);
        return;
    }
    if let Err(e) = super::placement::validate_house(server, player, template, p.x, p.y, p.z) {
        send_error(player, e);
        return;
    }
    let fort_owner =
        match super::dominion::fort::placement_owner(server, player, template, p.x, p.y) {
            Ok(owner) => owner,
            Err(e) => {
                send_error(player, e);
                return;
            }
        };
    let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
        send_error(player, err::INVALID_HOUSE_INFO);
        return;
    };
    let quote = match super::dominion::tax::quote_at(
        server,
        char_account,
        template,
        true,
        world.id,
        p.x,
        p.y,
    ) {
        Ok(quote) => quote,
        Err(e) => {
            tracing::error!("Build: {e}");
            return;
        }
    };
    let total = quote.info.total_tax_to_pay;

    let tax_item = server
        .game_data
        .features
        .check(crate::data::features::Feature::TaxItem);
    // Reserve every identifier before charging the player. The house remains unpublished
    // until the complete payment and selected design have been consumed together.
    let Ok(house_id) = server.ids.housing.get_next_id() else {
        send_error(player, err::INVALID_HOUSE_INFO);
        return;
    };
    let Some(house) = manager::create(server, p.design_id, faction_id, &world, 0, 0) else {
        server.ids.housing.release_id(house_id);
        send_error(player, err::INVALID_HOUSE_INFO);
        return;
    };
    house.data().id = house_id;
    let mut payment_error = None;
    let mut payment_effects = None;
    let revenue = super::dominion::tax::charge_with(server, &quote, || {
        match hooks.pay_house_construction_deferred(server, player, source, total, tax_item) {
            Ok(effects) => {
                payment_effects = effects;
                true
            }
            Err(error) => {
                payment_error = Some(error);
                false
            }
        }
    });
    if revenue.is_err() {
        manager::discard_created_house(server, &house);
        server.ids.housing.release_id(house_id);
        send_error(
            player,
            match payment_error {
                Some(HouseConstructionError::InvalidDesign) => err::BAG_INVALID_ITEM,
                Some(HouseConstructionError::InsufficientPayment) => {
                    err::MAIL_NOT_ENOUGH_MONEY_TO_PAY_TAXES
                }
                None => crate::systems::economy::error_message::INVALID_TAXATION,
            },
        );
        return;
    }
    // Spawn the actual house
    let data = &server.game_data.systems.housing;
    {
        let mut h = house.data();
        // Fallback for un-translated buildings (en_us)
        if h.unit.name.is_empty() {
            let mut fake = data.localized_item_name(source.template_id, &template.name);
            if fake.ends_with(" Design") {
                fake = fake.replace(" Design", "");
            }
            h.unit.name = fake;
        }
        h.unit.transform.set_xyz(p.x, p.y, p.z);
        // In 1.2 the rotation is sent as one sbyte: quantize so the server agrees with the
        // client (see the C# comment in Build).
        let (_, _, yaw) =
            PositionAndRotation::new(0.0, 0.0, 0.0, 0.0, 0.0, p.z_rot).roll_pitch_yaw_sbytes();
        let (_, _, z_rot) = PositionAndRotation::rotation_from_sbytes(0, 0, yaw);
        h.unit.transform.rot_x = 0.0;
        h.unit.transform.rot_y = 0.0;
        h.unit.transform.rot_z = z_rot;
    }
    let place_date = now();
    let initial_days = if super::placement::can_extend_house(server, &house) {
        // The placement fee includes the first paid period (1.2 build_taxPaymentDesc).
        // Protection also includes the existing seven-day interval before removal.
        i64::from(server.config.world.days_for_tax_payment) + manager::TAX_PAYMENT_GRACE_DAYS
    } else {
        // Original nonrenewable beginner plots expire after seven days.
        7
    };
    {
        let mut h = house.data();
        h.unit.owner_id = char_id;
        h.co_owner_id = char_id;
        h.account_id = account_id;
        set_permission(&mut h, Some(template), housing_permission::PRIVATE);
        h.allow_recover = true;
        h.place_date = place_date;
        h.protection_end_date = place_date + initial_days * DAY;
        h.is_dirty = true;
    }
    if let Some(owner) = fort_owner {
        super::dominion::fort::apply_owner(server, &house, owner);
    }
    let step = if template.build_steps.is_empty() {
        -1
    } else {
        0
    };
    manager::set_current_step(server, &house, step, false);
    manager::refresh_expedition(server, &house);
    if !server.systems.housing.manager.insert(&house) {
        tracing::error!("Build: house id / tl already in use (ArgumentException)");
        return;
    }
    if fort_owner.is_none_or(|owner| owner.character_id == char_id) {
        player.send(my_house_packet(server, &house));
    }
    manager::spawn_house(server, &house);
    update_tax_info(server, &house);
    if let Ok(Some(state)) = revenue {
        crate::systems::social::dominion::broadcast_snapshot(server, &state, false);
    }
    drop(_operation);
    if let Some(effects) = payment_effects {
        crate::systems::economy::fx::flush(server, effects);
    }
}

// ---------------------------------------------------------------------------------------------
// permission / name / recover
// ---------------------------------------------------------------------------------------------

/// `ChangeHousePermission(connection, tlId, permission)`.
pub fn change_house_permission(server: &GameServer, player: &PlayerRef, tl: u16, permission: u8) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    if !matches!(
        permission,
        housing_permission::PRIVATE
            | housing_permission::GUILD
            | housing_permission::PUBLIC
            | housing_permission::FAMILY
    ) {
        return;
    }
    let Some(house) = server.systems.housing.manager.by_tl(tl) else {
        return; // invalid house
    };
    super::dominion::fort::refresh_owner(server, &house);
    if super::dominion::fort::is_fort(server, &house) && permission != housing_permission::GUILD {
        return;
    }
    let char_id = player.data().id;
    if house.data().unit.owner_id != char_id {
        return; // not the owner
    }
    let template = template_of(server, &house);
    let effective_permission = {
        let mut h = house.data();
        set_permission(&mut h, template, permission);
        h.permission
    };
    broadcast(
        server,
        &house,
        &SCHousePermissionChangedPacket {
            tl,
            permission: effective_permission,
        },
    );
}

/// `ChangeHouseName(connection, tlId, name)`.
pub fn change_house_name(
    server: &GameServer,
    conn: &mut crate::net::connection::GameConnection,
    player: &PlayerRef,
    tl: u16,
    name: &str,
) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(house) = server.systems.housing.manager.by_tl(tl) else {
        return;
    };
    super::dominion::fort::refresh_owner(server, &house);
    let char_id = player.data().id;
    if house.data().unit.owner_id != char_id {
        return;
    }
    let mut chars = name.chars();
    let Some(first) = chars.next() else {
        // C#: Substring(0, 1) of an empty name throws.
        tracing::error!("ChangeHouseName: empty name (ArgumentOutOfRangeException)");
        return;
    };
    let new_name: String = first.to_uppercase().chain(chars).collect();
    {
        let mut h = house.data();
        h.unit.name = new_name.clone();
        h.is_dirty = true;
    }
    let packet = crate::systems::social::packets::misc::SCUnitNameChangedPacket {
        obj_id: house.obj_id,
        name: new_name,
    };
    // The owner's My Houses window can rename a house outside the visible region.
    // Reply once to the requester and also refresh the nearby viewers' name plaques.
    conn.send(&packet);
    if let Some(world) = world_of(server, &house) {
        let mut out = crate::net::connection::Fanout::new(&packet);
        for viewer in world.players_around(WorldObject::from(&house)) {
            if viewer.obj_id != player.obj_id {
                out.send(&viewer.conn);
            }
        }
    }
}

/// `HousingToggleAllowRecover(character, houseTl)`.
pub fn toggle_allow_recover(server: &GameServer, player: &PlayerRef, tl: u16) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(house) = server.systems.housing.manager.by_tl(tl) else {
        return;
    };
    super::dominion::fort::refresh_owner(server, &house);
    let char_id = player.data().id;
    let allow = {
        let mut h = house.data();
        if h.unit.owner_id != char_id {
            return;
        }
        h.allow_recover = !h.allow_recover;
        h.is_dirty = true;
        h.allow_recover
    };
    broadcast(
        server,
        &house,
        &SCHousingRecoverTogglePacket {
            tl,
            allow_recover: allow,
        },
    );
}

// ---------------------------------------------------------------------------------------------
// Demolish / ReturnHouseItemsToOwner
// ---------------------------------------------------------------------------------------------

/// `Demolish(connection, house, failedToPayTax, forceRestoreAllDecor)` (`player == None` = the
/// tax timer / GM).
pub fn demolish(
    server: &GameServer,
    player: Option<&PlayerRef>,
    house: &HouseRef,
    failed_to_pay_tax: bool,
    force_restore_all_decor: bool,
) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let manager = &server.systems.housing.manager;
    super::dominion::fort::refresh_owner(server, house);
    let (id, owner, tl) = {
        let h = house.data();
        (h.id, h.unit.owner_id, h.unit.tl_id)
    };
    if owner == 0 || !manager.contains(id) {
        if let Some(p) = player {
            send_error(p, err::INVALID_HOUSE_INFO);
        }
        return;
    }
    let is_owner = player.is_none_or(|p| {
        p.data().id == owner || super::dominion::rights::can_demolish_ordinary(server, p, house)
    });
    if !is_owner {
        // Non-owner should not be able to press demolish
        if let Some(p) = player {
            send_error(p, err::INVALID_HOUSE_INFO);
        }
        return;
    }
    let owner_char = server.world.character_by_id(owner);
    // Return items to player by mail
    if let Err(e) = return_house_items_to_owner(
        server,
        house,
        failed_to_pay_tax,
        force_restore_all_decor,
        None,
    ) {
        // C#: the exception aborts Demolish here.
        tracing::error!("Demolish {id}: {e}");
        return;
    }
    {
        // Expire and remove outstanding bills only after every return was committed.
        let mut h = house.data();
        h.protection_end_date = now() - 1;
        h.is_dirty = true;
    }
    update_tax_info(server, house);
    {
        // Remove owner
        let template = template_of(server, house);
        let mut h = house.data();
        h.unit.owner_id = 0;
        h.co_owner_id = 0;
        h.account_id = 0;
        h.sell_price = 0;
        h.sell_to_player_id = 0;
        set_permission(&mut h, template, housing_permission::PUBLIC);
    }
    manager::refresh_expedition(server, house);
    broadcast(server, house, &SCHouseDemolishedPacket { tl });
    if let Some(o) = owner_char {
        o.send(SCMyHouseRemovedPacket { tl });
    }
    // Make killable
    update_house_faction(server, house, FACTION_MONSTROSITY);
    set_for_sale_markers(server, house, false);
    house.data().is_dirty = true;
    // TODO (C#): better house killing handling
    manager.mark_removed(id);
    super::dominion::fort::refresh_for_house(server, house);
}

/// One entry of `returnedItems`.
#[derive(Debug, Clone, Copy)]
struct Returned {
    attachment: MailAttachment,
    template_id: u32,
    count: i32,
    max_count: i32,
}

impl Returned {
    fn new_item(server: &GameServer, template_id: u32, count: i32, owner_id: u32) -> Option<Self> {
        let t = server.game_data.items.template(template_id)?;
        let grade = if t.fixed_grade >= 0 {
            t.fixed_grade as u8
        } else {
            0
        };
        Some(Self {
            attachment: MailAttachment::NewItem {
                template_id,
                count,
                grade,
                owner_id,
            },
            template_id,
            count,
            max_count: t.max_count,
        })
    }

    fn existing(item: &HouseItem) -> Self {
        Self {
            attachment: MailAttachment::Existing {
                item_id: item.item_id,
                owner_id: item.owner_id as u32,
                add_count: 0,
            },
            template_id: item.template_id,
            count: item.count,
            max_count: item.max_count,
        }
    }

    /// `oldItem.Count++`.
    fn add_one(&mut self) {
        self.count += 1;
        match &mut self.attachment {
            MailAttachment::NewItem { count, .. } => *count += 1,
            MailAttachment::Existing { add_count, .. } => *add_count += 1,
        }
    }
}

/// `ReturnHouseItemsToOwner(house, failedToPayTax, forceRestoreAllDecor, newOwner)`: the
/// design (and tax deposit) and the restorable furniture go back to the owner by mail; the rest
/// is destroyed (or handed to the new owner). `Err` where the C# throws.
pub fn return_house_items_to_owner(
    server: &GameServer,
    house: &HouseRef,
    failed_to_pay_tax: bool,
    force_restore_all_decor: bool,
    new_owner: Option<&PlayerRef>,
) -> Result<(), String> {
    return_house_items_with_payment(
        server,
        house,
        failed_to_pay_tax,
        force_restore_all_decor,
        new_owner,
        Vec::new(),
        None,
    )
}

enum ReturnAction {
    Detach(crate::world::DoodadRef),
    Delete(crate::world::DoodadRef, bool),
    Transfer(crate::world::DoodadRef, u64),
}

/// Stage the same returns together with purchase receipts and payment. No furniture or
/// coffer changes are published until the economy has committed the entire mail batch.
#[allow(clippy::too_many_arguments)]
pub fn return_house_items_with_payment(
    server: &GameServer,
    house: &HouseRef,
    failed_to_pay_tax: bool,
    force_restore_all_decor: bool,
    new_owner: Option<&PlayerRef>,
    mut mails: Vec<HouseMail>,
    payment: Option<(&PlayerRef, i32)>,
) -> Result<(), String> {
    let (previous_owner, house_id) = {
        let h = house.data();
        (h.unit.owner_id, h.id)
    };
    if previous_owner == 0 {
        return Ok(());
    }
    let template = template_of(server, house).ok_or("house without template")?;
    let owner = if super::dominion::fort::design(template).is_some() {
        super::dominion::fort::state_for_house(server, house)
            .and_then(|state| super::dominion::fort::owner(server, &state))
            .ok_or("fort without a current expedition leader")?
            .character_id
    } else {
        previous_owner
    };
    let data = &server.game_data.systems.housing;
    let hooks = server.systems.housing.hooks.get();
    let mut returned: Vec<Returned> = Vec::new();
    let mut actions = Vec::new();
    let mut returned_money = 0;

    // If returning items because of a new House Owner, then don't include the design
    if new_owner.is_none() {
        // TODO (C#): proper grades for design / Full-Kit demolition
        let design_item_id = data.item_id_by_design(template.id, |id| {
            server.game_data.items.template(id).is_some()
        });
        returned.push(
            Returned::new_item(server, design_item_id, 1, owner)
                .ok_or_else(|| format!("house {house_id} has no returnable design template"))?,
        );
        // Return taxes
        if !failed_to_pay_tax {
            let tax = template
                .taxation
                .ok_or("house template without taxation (NullReferenceException)")?
                .tax;
            if server
                .game_data
                .features
                .check(crate::data::features::Feature::TaxItem)
            {
                let count = super::hooks::construction_certificates(
                    i32::try_from(u64::from(tax) * 2)
                        .map_err(|_| "tax refund exceeds the packet money limit")?,
                );
                if count > 0 {
                    let r = Returned::new_item(server, BOUND_TAX_CERTIFICATE, count, owner)
                        .ok_or("tax certificate template missing (NullReferenceException)")?;
                    returned.push(r);
                }
            } else {
                returned_money = i32::try_from(u64::from(tax) * 2)
                    .map_err(|_| "tax refund exceeds the packet money limit")?;
            }
        }
    }

    let new_owner_id = new_owner.map(|p| p.data().id);
    let world = world_of(server, house).ok_or("house without world")?;
    for f in world.doodads_by_house_db_id(house_id) {
        let (attach, template_id, item_id, item_template_id, container_id) = {
            let d = f.data();
            (
                d.attach_point,
                d.template_id,
                d.item_id,
                d.item_template_id,
                d.coffer.as_ref().map(|c| c.container_id),
            )
        };
        // Ignore attached objects (those are doors/windows etc)
        if attach != crate::models::unit::attach_point::NONE {
            continue;
        }
        // Ignore for sale signs
        if template_id == FOR_SALE_MARKER_DOODAD_ID {
            continue;
        }
        let this_item = hooks.item(server, item_id);
        let placed_item_template = this_item.map_or(item_template_id, |item| item.template_id);
        let Some(deco) = data.decoration_for_item(template_id, placed_item_template) else {
            // Is not furniture, probably plants or backpacks
            actions.push(ReturnAction::Detach(f));
            continue;
        };
        let Some(deco_info) = data.decoration_item(deco.id, placed_item_template) else {
            // No design info for this item ? Just detach it for now
            actions.push(ReturnAction::Detach(f.clone()));
            tracing::warn!(
                "ReturnHouseItemsToOwner - Furniture has a design, but couldn't find a item for it, DoodadObjId:{} Template:{template_id}, DesignId: {}",
                f.obj_id,
                deco.id
            );
            continue;
        };
        let mut want_returned =
            (new_owner.is_none() && deco_info.restore) || force_restore_all_decor;
        // If item is bound, always return it owner
        if item_id > 0 {
            let item = this_item.ok_or_else(|| {
                format!("furniture item {item_id} not found (NullReferenceException)")
            })?;
            if item.slot_type != slot_type::SYSTEM {
                return Err(format!(
                    "furniture item {item_id} is not in its system container"
                ));
            }
            if item.soul_bound {
                want_returned = true;
            }
        }
        // A coffer with an ItemContainer: all of its items are returned too
        if let Some(container_id) = container_id.filter(|c| *c > 0) {
            for mut item in hooks.coffer_items(server, container_id) {
                item.owner_id = u64::from(owner);
                returned.push(Returned::existing(&item));
            }
        }
        if !want_returned {
            match new_owner {
                None => {
                    // Just delete the doodad and attached item if no new owner
                    actions.push(ReturnAction::Delete(f, false));
                }
                Some(_) => {
                    // Move the doodad and item to the new owner
                    actions.push(ReturnAction::Transfer(f, item_id));
                }
            }
            continue;
        }
        // Item needs to be actually returned
        if item_id > 0 {
            let mut item = this_item
                .filter(|i| i.slot_type == slot_type::SYSTEM)
                .ok_or_else(|| {
                    format!("furniture item {item_id} is not in its system container")
                })?;
            item.owner_id = u64::from(owner);
            returned.push(Returned::existing(&item));
        } else if item_template_id > 0 {
            // try to stack stackable items
            if let Some(old) = returned.iter_mut().find(|x| {
                x.template_id == item_template_id
                    && x.count < x.max_count
                    && matches!(x.attachment, MailAttachment::NewItem { .. })
            }) {
                old.add_one();
            } else {
                let r = Returned::new_item(server, item_template_id, 1, owner).ok_or_else(|| {
                    format!("furniture template {item_template_id} missing (NullReferenceException)")
                })?;
                returned.push(r);
            }
        } else {
            // Not sure what happened here, just ignore it
            continue;
        }
        actions.push(ReturnAction::Delete(f, true));
    }

    // Split items into mails of maximum 10 attachments
    let receiver_name = server.names.get_character_name(owner);
    let now_ms = crate::world::now_ms();
    let recv_ms = now_ms
        + if failed_to_pay_tax {
            HOURS_FOR_FAILED_TAX_TO_RETURN_HOUSE * 3600 * 1000
        } else {
            0
        };
    let mut mail: Option<HouseMail> = None;
    let last = returned.len().saturating_sub(1);
    for (i, r) in returned.iter().enumerate() {
        if i % 10 == 0 {
            mail = Some(HouseMail {
                mail_type: mail_type::DEMOLISH,
                receiver_name: receiver_name.clone(),
                receiver_id: owner,
                sender_id: 0,
                sender_name: ".houseDemolish".into(),
                extra: i64::from(house_id),
                title: "title".into(),
                text: "body".into(),
                send_date_ms: now_ms,
                recv_date_ms: recv_ms,
                ..HouseMail::default()
            });
        }
        let m = mail.as_mut().expect("mail created at i % 10 == 0");
        // Only attach money to first mail
        if returned_money > 0 && i == 0 {
            m.copper_coins = returned_money;
        }
        m.attachments.push(r.attachment);
        if i % 10 == 9 || i == last {
            mails.push(m.clone());
        }
    }
    if !hooks.send_return_mails(server, mails, payment) {
        return Err("unable to commit housing returns and purchase payment".into());
    }
    for action in actions {
        match action {
            ReturnAction::Detach(f) => {
                world.detach_child(&WorldObject::from(&f));
                let persistent = {
                    let mut d = f.data();
                    d.parent_obj_id = 0;
                    d.owner_db_id = 0;
                    d.is_persistent
                };
                if persistent {
                    crate::systems::npcs::doodad::db::save(server, &f);
                }
            }
            ReturnAction::Delete(f, returned_item) => {
                let item_id = f.data().item_id;
                if !returned_item && item_id != 0 {
                    hooks.destroy_item(server, item_id);
                }
                world.detach_child(&WorldObject::from(&f));
                f.data().item_id = 0;
                crate::systems::npcs::doodad::manager::delete(server, &world, &f);
            }
            ReturnAction::Transfer(f, item_id) => {
                if let Some(no) = new_owner {
                    if item_id != 0 {
                        hooks.move_item_to_system_container(
                            server,
                            no,
                            item_task::INVALID,
                            item_id,
                        );
                    }
                    f.data().owner_id = new_owner_id.unwrap_or(0);
                }
            }
        }
    }
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// Sale
// ---------------------------------------------------------------------------------------------

/// `CalculateSaleCertifcates(house, salePrice)`: one appraisal certificate per 100 gold.
pub fn calculate_sale_certificates(sale_price: u32) -> i32 {
    let n = (f64::from(sale_price) / COPPER_PER_CERTIFICATE).ceil() as i32;
    n.max(1)
}

/// `SetForSaleMarkers(house, isForSale)`: four sale signs at the garden corners, or their
/// removal.
pub fn set_for_sale_markers(server: &GameServer, house: &HouseRef, is_for_sale: bool) {
    let Some(world) = world_of(server, house) else {
        let id = house.data().id;
        tracing::warn!(
            "Trying to set a house for sale of a house that doesn't have a world attached {id}"
        );
        return;
    };
    let (house_id, hx, hy, hz) = {
        let h = house.data();
        let t = &h.unit.transform;
        (h.id, t.x, t.y, t.z)
    };
    if is_for_sale {
        let radius = template_of(server, house).map_or(0.0, |t| t.garden_radius);
        for post in 0..4 {
            let x_mul = if post % 2 == 0 { -1.0 } else { 1.0 };
            let y_mul = if post / 2 == 0 { -1.0 } else { 1.0 };
            // `135f + 90f * postId % 360` (the modulo binds to the product)
            let z_rot = (135f32 + (90f32 * post as f32) % 360f32).to_radians();
            let Some(d) = crate::systems::npcs::doodad::manager::create(
                server,
                &world,
                0,
                FOR_SALE_MARKER_DOODAD_ID,
                None,
                true,
            ) else {
                tracing::error!("SetForSaleMarkers: doodad {FOR_SALE_MARKER_DOODAD_ID} missing");
                return;
            };
            let x = radius * x_mul + hx;
            let y = radius * y_mul + hy;
            // adjust height to the floor (`GeoData.GetHeight`: raw heightmap without geodata)
            let z = ground_height(server, &world, x, y).unwrap_or(hz);
            {
                let mut dd = d.data();
                dd.transform.set_xyz(x, y, z);
                dd.transform.rot_z = z_rot;
                dd.transform.instance_id = world.id;
                dd.item_template_id = 0;
                dd.item_id = 0;
                dd.owner_id = 0;
                dd.parent_obj_id = 0;
                dd.ucc_id = 0;
                dd.attach_point = crate::models::unit::attach_point::NONE;
                dd.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
                dd.owner_db_id = house_id;
            }
            crate::systems::npcs::doodad::manager::init_doodad(server, &world, &d);
            crate::systems::npcs::doodad::manager::spawn(server, &world, &d);
        }
    } else {
        let posts = world.doodads_by_house_db_id(house_id);
        for d in posts.iter().rev() {
            if d.data().template_id == FOR_SALE_MARKER_DOODAD_ID {
                server.systems.housing.manager.remove_attached(house, d);
                crate::systems::npcs::doodad::manager::delete(server, &world, d);
            }
        }
    }
}

/// `AiGeoDataManager.GetHeight(pos)` without geodata: the raw heightmap height at the rounded
/// position (0 on errors). `None` when no heightmaps are loaded.
fn ground_height(
    server: &GameServer,
    world: &crate::world::WorldInstance,
    x: f32,
    y: f32,
) -> Option<f32> {
    let maps = server.systems.npcs.heightmaps.get()?;
    let t = maps.template(world.template.id)?;
    Some(
        t.get_raw_height_map_height(
            maps.files(),
            x.round_ties_even() as i32,
            y.round_ties_even() as i32,
        )
        .unwrap_or(0.0),
    )
}

/// `SetForSale(house, price, buyerId, seller)` (`seller == None` = GM: no certificates).
pub fn set_for_sale(
    server: &GameServer,
    house: &HouseRef,
    price: u32,
    buyer_id: u32,
    seller: Option<&PlayerRef>,
) -> bool {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    if template_of(server, house)
        .is_some_and(|template| super::dominion::fort::castle_category(template.category_id))
    {
        if let Some(player) = seller {
            send_error(
                player,
                crate::systems::economy::error_message::HOUSE_CANNOT_SELL_AS_CASTLE,
            );
        }
        return false;
    }
    if price == 0 || i32::try_from(price).is_err() {
        return false;
    }
    // The tax timer can still be waiting to demolish an expired house. Do not charge
    // appraisal certificates for property the seller is about to lose.
    if house.data().protection_end_date <= now() {
        return false;
    }
    if seller.is_some_and(|player| house.data().unit.owner_id != player.character_id) {
        return false;
    }
    if !template_of(server, house).is_some_and(|t| t.is_sellable) {
        return false;
    }
    // Check if buyer exists (we just check if the name exists)
    let buyer_name = server.names.get_character_name(buyer_id);
    if buyer_id != 0 && buyer_name.is_none() {
        return false;
    }
    let buyer_name = buyer_name.unwrap_or_default();
    {
        let h = house.data();
        if h.sell_price == price && h.sell_to_player_id == buyer_id {
            return true;
        }
    }
    if let Some(seller) = seller {
        let cert = calculate_sale_certificates(price);
        let hooks = server.systems.housing.hooks.get();
        if !hooks.pay_house_sale_certificates(server, seller, cert) {
            send_error(seller, err::HOUSE_CANNOT_SELL_AS_NOT_ENOUGH_SEAL);
            return false;
        }
    }
    let (tl, name) = {
        let mut h = house.data();
        h.sell_price = price;
        h.sell_to_player_id = buyer_id;
        h.is_dirty = true;
        (h.unit.tl_id, h.unit.name.clone())
    };
    broadcast(
        server,
        house,
        &SCHouseSetForSalePacket {
            tl,
            money_amount: price,
            sell_to_player_id: buyer_id,
            sell_to_name: buyer_name,
            house_name: name,
        },
    );
    set_for_sale_markers(server, house, false);
    set_for_sale_markers(server, house, true);
    true
}

/// `SetForSale(houseTlId, ...)`.
pub fn set_for_sale_by_tl(
    server: &GameServer,
    tl: u16,
    price: u32,
    buyer_id: u32,
    seller: Option<&PlayerRef>,
) -> bool {
    match server.systems.housing.manager.by_tl(tl) {
        Some(h) => set_for_sale(server, &h, price, buyer_id, seller),
        None => false,
    }
}

/// `zoneManager.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString()` (`""` for null).
fn zone_group_text(server: &GameServer, house: &House) -> String {
    server
        .game_data
        .zones
        .zone_by_key(house.unit.transform.zone_id)
        .map(|z| z.group_id.to_string())
        .unwrap_or_default()
}

/// `CancelForSale(house, returnCertificates)`.
pub fn cancel_for_sale(server: &GameServer, house: &HouseRef, return_certificates: bool) -> bool {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let h = house.data().clone();
    if h.sell_price == 0 {
        return true;
    }
    let cert = calculate_sale_certificates(h.sell_price);
    let owner = server.world.character_by_id(h.unit.owner_id);
    // Can only return certificates if owner is online and is the one resetting the sale
    if cert > 0 && return_certificates {
        let now_ms = crate::world::now_ms();
        let mail = HouseMail {
            mail_type: mail_type::HOUSING_SALE,
            receiver_id: h.unit.owner_id,
            sender_name: ".houseSellCancel".into(),
            receiver_name: server.names.get_character_name(h.unit.owner_id),
            title: format!("title({},'{}')", zone_group_text(server, &h), h.unit.name),
            text: format!("body('{}', {APPRAISAL_CERTIFICATE}, {cert})", h.unit.name),
            send_date_ms: now_ms,
            recv_date_ms: now_ms + 1,
            ..HouseMail::default()
        };
        let hooks = server.systems.housing.hooks.get();
        if let Some(owner) = &owner {
            if !hooks.send_mail_with_new_items(server, owner, mail, APPRAISAL_CERTIFICATE, cert) {
                tracing::warn!("CancelForSale - Failed to create Appraisal Certificates for mail");
                return false;
            }
        } else {
            let mut mail = mail;
            mail.attachments.push(MailAttachment::NewItem {
                template_id: APPRAISAL_CERTIFICATE,
                count: cert,
                grade: 0,
                owner_id: h.unit.owner_id,
            });
            if !hooks.send_mail(server, mail) {
                return false;
            }
        }
    }
    {
        let mut hd = house.data();
        hd.sell_price = 0;
        hd.sell_to_player_id = 0;
        hd.is_dirty = true;
    }
    broadcast(
        server,
        house,
        &SCHouseResetForSalePacket {
            tl: h.unit.tl_id,
            house_name: h.unit.name.clone(),
        },
    );
    set_for_sale_markers(server, house, false);
    true
}

/// `CancelForSale(houseTlId, returnCertificates)` (an unknown tl throws in C#: logged).
pub fn cancel_for_sale_by_tl(server: &GameServer, tl: u16, return_certificates: bool) -> bool {
    match server.systems.housing.manager.by_tl(tl) {
        Some(h) => cancel_for_sale(server, &h, return_certificates),
        None => {
            tracing::error!("CancelForSale: unknown house tl {tl} (NullReferenceException)");
            false
        }
    }
}

/// `UpdateFurnitureOwner(house, characterId, newFaction)`: the number of doodads updated.
pub fn update_furniture_owner(
    server: &GameServer,
    house: &HouseRef,
    character_id: u32,
    new_faction: u32,
) -> u32 {
    let Some(world) = world_of(server, house) else {
        return 0;
    };
    let id = house.data().id;
    let mut res = 0;
    for f in world.doodads_by_house_db_id(id) {
        let persistent = {
            let mut d = f.data();
            d.owner_id = character_id;
            d.is_persistent
        };
        crate::world::broadcast(
            &world,
            &f,
            &crate::systems::npcs::doodad::packets::SCDoodadOriginatorPacket {
                obj_id: f.obj_id,
                new_owner_id: character_id,
                new_faction,
            },
            true,
        );
        if persistent {
            crate::systems::npcs::doodad::db::save(server, &f);
        }
        res += 1;
    }
    res
}

/// `BuyHouse(houseTlId, money, character)`.
pub fn buy_house(server: &GameServer, tl: u16, money: u32, player: &PlayerRef) -> bool {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(house) = server.systems.housing.manager.by_tl(tl) else {
        send_error(player, err::INVALID_HOUSE_INFO);
        return false;
    };
    if template_of(server, &house)
        .is_some_and(|template| super::dominion::fort::castle_category(template.category_id))
    {
        send_error(
            player,
            crate::systems::economy::error_message::HOUSE_CANNOT_SELL_AS_CASTLE,
        );
        return false;
    }
    let (char_id, char_account, char_faction, char_name) = player_ids(player);
    let h = house.data().clone();
    if h.unit.owner_id == 0 || h.protection_end_date <= now() {
        send_error(player, err::INVALID_HOUSE_INFO);
        return false;
    }
    if h.sell_price == 0 {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_NOT_FOR_SALE);
        return false;
    }
    let Ok(sale_price) = i32::try_from(h.sell_price) else {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_SALE_INFO_CHANGED);
        return false;
    };
    if h.sell_price != money {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_SALE_INFO_CHANGED);
        return false;
    }
    if h.sell_to_player_id != 0 && h.sell_to_player_id != char_id {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_NOT_DESIGNATED_BUYER);
        return false;
    }
    if h.unit.owner_id == char_id {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_OWNER);
        return false;
    }
    // NOTE (C#): check tax due maybe ?
    if player.data().money < i64::from(sale_price) {
        send_error(player, err::HOUSE_CANNOT_BUY_AS_NOT_ENOUGH_MONEY);
        return false;
    }
    let previous_owner = h.unit.owner_id;
    let previous_owner_name = server.names.get_character_name(previous_owner);
    let prev_name_text = previous_owner_name.clone().unwrap_or_default();
    let now_ms = crate::world::now_ms();

    // The buyer payment, furniture return and both receipts are committed together.
    let receipt_mails = vec![
        HouseMail {
            mail_type: mail_type::HOUSING_SALE,
            receiver_id: char_id,
            sender_name: ".houseBought".into(),
            receiver_name: Some(char_name.clone()),
            title: format!("title({},'{}')", zone_group_text(server, &h), h.unit.name),
            text: format!(
                "body('{prev_name_text}', '{}', {})",
                h.unit.name, h.sell_price
            ),
            send_date_ms: now_ms,
            recv_date_ms: now_ms + 1,
            ..HouseMail::default()
        },
        HouseMail {
            mail_type: mail_type::HOUSING_SALE,
            receiver_id: previous_owner,
            sender_name: ".houseSold".into(),
            receiver_name: previous_owner_name,
            title: format!("title('{char_name}','{}')", h.unit.name),
            text: format!("body('{char_name}', '{}', {})", h.unit.name, h.sell_price),
            copper_coins: sale_price,
            send_date_ms: now_ms,
            recv_date_ms: now_ms + 1,
            ..HouseMail::default()
        },
    ];

    if let Err(e) = return_house_items_with_payment(
        server,
        &house,
        false,
        false,
        Some(player),
        receipt_mails,
        Some((player, sale_price)),
    ) {
        tracing::error!("BuyHouse {}: {e}", h.id);
        return false;
    }

    // Set new owner info
    {
        let template = template_of(server, &house);
        let always_public = template.is_some_and(|t| t.always_public);
        let mut hd = house.data();
        hd.sell_price = 0;
        hd.sell_to_player_id = 0;
        hd.account_id = char_account;
        hd.unit.owner_id = char_id;
        hd.co_owner_id = char_id; // not entirely sure if this actually needs to change
        let p = if always_public {
            housing_permission::PUBLIC
        } else {
            housing_permission::PRIVATE
        };
        set_permission(&mut hd, template, p);
    }
    manager::refresh_expedition(server, &house);
    update_house_faction(server, &house, char_faction);
    update_tax_info(server, &house); // send tax due mails etc. if needed ...

    // TODO (C#): broadcast changes
    let name = house.data().unit.name.clone();
    broadcast(
        server,
        &house,
        &SCHouseSoldPacket {
            tl,
            unk_id: previous_owner,
            unk2_id: char_id,
            new_owner_acc: char_account,
            owner_name: char_name,
            house_name: name,
        },
    );
    set_for_sale_markers(server, &house, false);
    player.send(my_house_packet(server, &house));
    if let Some(old) = server.world.character_by_id(previous_owner) {
        // `oldOwner is { IsOnline: true }` (online players are the ones in the world)
        old.send(SCMyHouseRemovedPacket { tl });
    }
    update_furniture_owner(server, &house, char_id, char_faction);
    house.data().is_dirty = true;
    true
}

// ---------------------------------------------------------------------------------------------
// DecorateHouse
// ---------------------------------------------------------------------------------------------

/// `DecorateHouse(player, houseTlId, designId, pos, quat, parentObjId, itemId)`: `Ok(false)` =
/// failed (the packet sends `HouseCannotDecorate`), `Err` = the C# throws.
pub fn decorate_house(
    server: &GameServer,
    player: &PlayerRef,
    p: &CSDecorateHousePacket,
) -> Result<bool, String> {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let hooks = server.systems.housing.hooks.get();
    let char_id = player.data().id;
    // Check Item
    let Some(item) = hooks.inventory_item(server, player, p.item_id).filter(|i| {
        i.owner_id == u64::from(char_id) && i.slot_type == slot_type::INVENTORY && i.count > 0
    }) else {
        return Ok(false);
    };
    // Check House
    let Some(house) = server
        .systems
        .housing
        .manager
        .by_tl(p.house_tl)
        .filter(|h| h.data().unit.tl_id == p.house_tl)
    else {
        send_error(player, err::INVALID_HOUSE_INFO);
        return Ok(false);
    };
    if (p.parent_obj_id != 0 && p.parent_obj_id != house.obj_id)
        || ![p.x, p.y, p.z]
            .into_iter()
            .chain(p.quat)
            .all(f32::is_finite)
        || p.quat.iter().map(|v| f64::from(*v).powi(2)).sum::<f64>() <= f64::EPSILON
    {
        return Ok(false);
    }
    if !manager::allowed_to_interact(server, &house, player)
        || !server
            .game_data
            .systems
            .housing
            .decoration_accepts_item(p.design_id, item.template_id)
    {
        return Ok(false);
    }
    let ucc_id = if item.ucc_id > 0 {
        server
            .streams
            .ucc
            .get_ucc(item.ucc_id)
            .map_or(0, |_| item.ucc_id)
    } else {
        0
    };
    // Create decoration doodad
    let design = server
        .game_data
        .systems
        .housing
        .decoration(p.design_id)
        .ok_or_else(|| {
            format!(
                "unknown decoration design {} (NullReferenceException)",
                p.design_id
            )
        })?;
    let world = world_of(server, &house).ok_or("house without world")?;
    if player.data().unit.transform.instance_id != world.id || house.data().current_step != -1 {
        return Ok(false);
    }
    let Some(template) = template_of(server, &house) else {
        return Ok(false);
    };
    let house_id = house.data().id;
    let mut decoration_count = 0;
    let mut group_count = 0;
    for d in world.doodads_by_house_db_id(house_id) {
        let (doodad_id, item_template_id) = {
            let d = d.data();
            (d.template_id, d.item_template_id)
        };
        let Some(existing) = server
            .game_data
            .systems
            .housing
            .decoration_for_item(doodad_id, item_template_id)
        else {
            continue;
        };
        decoration_count += 1;
        if existing.deco_act_ability_group_id == design.deco_act_ability_group_id {
            group_count += 1;
        }
    }
    if [template.deco_limit, template.absolute_deco_limit]
        .into_iter()
        .any(|limit| limit > 0 && decoration_count >= limit)
        || server
            .game_data
            .systems
            .housing
            .decoration_limit(
                template.housing_deco_limit_id,
                design.deco_act_ability_group_id,
            )
            .is_some_and(|limit| group_count >= limit)
    {
        return Ok(false);
    }
    let house_obj = WorldObject::from(&house);
    let doodad = crate::systems::npcs::doodad::manager::create(
        server,
        &world,
        0,
        design.doodad_id,
        Some(&house_obj),
        true,
    )
    .ok_or_else(|| {
        format!(
            "doodad template {} missing (NullReferenceException)",
            design.doodad_id
        )
    })?;
    {
        let mut d = doodad.data();
        let q = crate::models::transform::Quaternion {
            x: p.quat[0],
            y: p.quat[1],
            z: p.quat[2],
            w: p.quat[3],
        };
        let (rx, ry, rz) = PositionAndRotation::rotation_from_quaternion(q);
        d.transform.set_position(p.x, p.y, p.z, rx, ry, rz);
        d.item_template_id = item.template_id;
        d.item_id = if item.max_count <= 1 { p.item_id } else { 0 };
        d.owner_db_id = house_id;
        if house_id > 0 {
            if let Some((weight, length)) = item.big_fish {
                let w = weight as i16;
                let l = length as i16;
                d.data = (i32::from(l) << 16).wrapping_add(i32::from(w));
            }
        }
        d.owner_id = char_id;
        d.parent_obj_id = house.obj_id;
        d.attach_point = crate::models::unit::attach_point::NONE;
        d.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
        d.ucc_id = ucc_id;
        d.is_persistent = true;
    }
    // An unpaid decoration must never be published or saved. Count one cannot leave a
    // partially consumed stack; unique items move into the system container atomically.
    let paid = if item.max_count > 1 {
        hooks.bag_consume_item(
            server,
            player,
            item_task::DOODAD_CREATE,
            item.template_id,
            1,
            Some(item.item_id),
        ) == 1
    } else {
        hooks.move_item_to_system_container(server, player, item_task::DOODAD_CREATE, item.item_id)
    };
    if !paid {
        server.ids.object.release_id(doodad.obj_id);
        return Ok(false);
    }
    world.attach_child(&house_obj, &WorldObject::from(&doodad));
    if doodad.data().coffer.is_some() {
        if let Some(h) = server.systems.npcs.hooks.economy.get() {
            h.initialize_coffer(server, &doodad, char_id);
        }
    }
    crate::systems::npcs::doodad::manager::init_doodad(server, &world, &doodad);
    crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);
    crate::systems::npcs::doodad::db::save(server, &doodad);

    Ok(true)
}

// ---------------------------------------------------------------------------------------------
// CraftEffect (Building)
// ---------------------------------------------------------------------------------------------

/// `CraftEffect.Apply`, `WorldInteractionGroup.Building when target is House` for a character
/// caster. `used_skill` = `CastSkill.SkillId` (0 otherwise). Returns false when the C# throws
/// (the effect then ends without the quest event).
pub fn craft_building(
    server: &GameServer,
    player: &PlayerRef,
    house: &HouseRef,
    used_skill: u32,
) -> bool {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(template) = template_of(server, house) else {
        return false;
    };
    if super::dominion::fort::is_fort(server, house)
        && !super::dominion::fort::allowed_to_interact(server, house, player)
    {
        return false;
    }
    let char_name = player.data().unit.name.clone();
    let (current_action, current_step, owner, house_instance) = {
        let h = house.data();
        (
            h.current_action(),
            h.current_step,
            h.unit.owner_id,
            h.unit.transform.instance_id,
        )
    };
    if owner == 0 || current_step < 0 || player.data().unit.transform.instance_id != house_instance
    {
        return false;
    }
    // Get the house's current build step
    let step = if current_action >= 0 && current_step < template.build_steps.len() as i32 {
        match template.build_steps.get(&current_step) {
            Some(s) => Some(*s),
            None => {
                // C#: BuildSteps[-1] of a finished house throws KeyNotFoundException.
                tracing::error!(
                    "CraftEffect: house {} has no build step {current_step}",
                    house.obj_id
                );
                return false;
            }
        }
    } else {
        None
    };
    // Compare if the used skill (correct pack) is still valid when used
    if let Some(step) = step.filter(|s| used_skill != s.skill_id) {
        tracing::warn!(
            "{char_name} tried to building using the wrong skill, {used_skill} instead of {}",
            step.skill_id
        );
        player.data().skill_char.skill_cancelled = true;
        crate::systems::skills::skill::interrupt_skills(server, &WorldObject::from(player));
        return false;
    }
    // When done, set step to -1
    if template.build_steps.is_empty() {
        manager::set_current_step(server, house, -1, false);
    } else {
        manager::add_build_action(server, house);
        // MAKE_HOUSING: the owner's last build action finished the house (design id value1).
        if owner == player.character_id && house.data().current_step == -1 {
            crate::systems::report_achievement(
                server,
                player,
                AchievementInput::Increment {
                    kind_id: char_record_kind::MAKE_HOUSING,
                    amount: 1,
                    matcher: RecordMatch::Value1(template.id),
                },
            );
        }
    }
    let packet = {
        let h = house.data();
        SCHouseBuildProgressPacket {
            tl: h.unit.tl_id,
            model_id: h.unit.model_id,
            all_step: h.all_action,
            cur_step: if h.current_step == -1 {
                h.all_action
            } else {
                h.current_action()
            },
        }
    };
    if let Some(world) = server.world.world_of(&WorldObject::from(player)) {
        crate::world::broadcast(&world, player, &packet, true);
    }
    // When done, spawn all attached doodads like doors and windows
    if house.data().current_step == -1 {
        if let Some(world) = world_of(server, house) {
            for d in server.systems.housing.manager.attached(house.obj_id) {
                crate::systems::npcs::doodad::manager::spawn(server, &world, &d);
            }
        }
    }
    true
}
