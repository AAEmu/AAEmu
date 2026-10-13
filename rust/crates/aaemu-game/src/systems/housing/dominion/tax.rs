//! Territory property revenue. The configurable heavy-tax order and nominal gold credit are
//! explicit world-server policy; the original 1.2 client does not define those server rules.

use crate::data::features::Feature;
use crate::server::GameServer;
use crate::systems::housing::config::DominionPropertyTaxBase;
use crate::systems::housing::data::HousingTemplate;
use crate::systems::housing::hooks::HouseMail;
use crate::systems::housing::manager::{self, TaxInfo};
use crate::world::{HouseRef, PlayerRef, WorldObject};

use super::state::{DominionError, DominionState};

/// Original 1.2 content_configs61/kind23 and verified native tax formatter's divisor.
pub const COPPER_PER_TAX_CERTIFICATE: i64 = 10_000;

#[derive(Debug, Clone)]
pub struct TaxQuote {
    pub info: TaxInfo,
    /// Refundable placement deposit, never territory revenue.
    pub deposit: i32,
    /// Nominal copper surcharge; certificate rounding excess is never fabricated revenue.
    pub property_revenue: i32,
    pub authority: Option<DominionState>,
}

/// A taxable province is the entire zone group. Fort placement's lodestone radius does not
/// restrict ordinary property tax. Every levy still requires the retained live claimed tower.
pub fn authority_at(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
) -> Option<DominionState> {
    if !x.is_finite() || !y.is_finite() {
        return None;
    }
    let world = server.world.world(instance_id)?;
    let zone = world.template.zone_id_at(x, y);
    let group = server.game_data.zones.zone_by_key(zone)?.group_id;
    authority_for_zone(server, group, instance_id)
}

pub fn authority_for_zone(
    server: &GameServer,
    zone_group_id: u32,
    instance_id: u32,
) -> Option<DominionState> {
    let state = server.systems.housing.dominions.get(zone_group_id)?;
    if state.owner_expedition_id == 0 || state.tower.instance_id != instance_id {
        return None;
    }
    let house = server.systems.housing.manager.by_id(state.tower.house_id)?;
    if !super::runtime::is_live_object(server, &WorldObject::from(&house))
        || super::runtime::tower_identity(server, &house)? != (zone_group_id, state.tower)
        || house.data().current_step != -1
        || super::fort::owner(server, &state).is_none()
    {
        return None;
    }
    Some(state)
}

pub fn quote_at(
    server: &GameServer,
    account_id: u32,
    template: &HousingTemplate,
    building: bool,
    instance_id: u32,
    x: f32,
    y: f32,
) -> Result<TaxQuote, String> {
    let (_, info) = manager::calculate_building_tax_info(server, account_id, template, building)?;
    let authority = (template.heavy_tax
        && super::fort::design(template).is_none()
        && template.guard_tower_setting_id == 0)
        .then(|| authority_at(server, instance_id, x, y))
        .flatten();
    quote(
        info,
        template.tax_or_zero(),
        building,
        authority,
        server.config.housing.dominion_property_tax_base,
        server.game_data.features.check(Feature::TaxItem),
    )
}

pub fn quote_house(server: &GameServer, house: &HouseRef) -> Result<TaxQuote, String> {
    let h = house.data().clone();
    let template = manager::template_of(server, house)
        .ok_or_else(|| "property template is missing".to_owned())?;
    quote_at(
        server,
        h.account_id,
        template,
        false,
        h.unit.transform.instance_id,
        h.unit.transform.x,
        h.unit.transform.y,
    )
}

pub(crate) fn quote(
    mut info: TaxInfo,
    base_tax: u32,
    building: bool,
    authority: Option<DominionState>,
    policy: DominionPropertyTaxBase,
    tax_items: bool,
) -> Result<TaxQuote, String> {
    let basis = match policy {
        DominionPropertyTaxBase::AfterHeavy => i64::from(info.one_week_tax_count),
        DominionPropertyTaxBase::Base => i64::from(base_tax),
    };
    let rate = authority.as_ref().map_or(0, |s| s.tax_rate);
    if basis < 0 || !(0..=50).contains(&rate) {
        return Err("property tax quote is invalid".into());
    }
    let revenue = i32::try_from(basis * i64::from(rate) / 100)
        .map_err(|_| "property revenue exceeds the native money field".to_owned())?;
    let deposit = if building { i64::from(base_tax) * 2 } else { 0 };
    let nominal = i64::from(info.one_week_tax_count) + i64::from(revenue) + deposit;
    // The existing server charges certificates with ceil. Quantize the wire amount to whole
    // certificates so the native nearest-integer formatter displays the actual charged cost.
    let charged = if tax_items {
        let unit = COPPER_PER_TAX_CERTIFICATE;
        (nominal + unit - 1) / unit * unit
    } else {
        nominal
    };
    info.total_tax_to_pay = i32::try_from(charged)
        .map_err(|_| "property payment exceeds the native money field".to_owned())?;
    info.one_week_tax_count = info
        .one_week_tax_count
        .checked_add(revenue)
        .ok_or_else(|| "weekly property tax exceeds the native money field".to_owned())?;
    info.hostile_tax_rate = rate;
    Ok(TaxQuote {
        info,
        deposit: i32::try_from(deposit)
            .map_err(|_| "property deposit exceeds the native money field".to_owned())?,
        property_revenue: revenue,
        authority,
    })
}

/// The caller holds housing.operation_lock, then this operation takes authority before
/// economy. A failed debit or overflowing/stale ledger leaves both unchanged.
pub fn charge_with(
    server: &GameServer,
    quote: &TaxQuote,
    charge: impl FnOnce() -> bool,
) -> Result<Option<DominionState>, DominionError> {
    match &quote.authority {
        Some(state) => server
            .systems
            .housing
            .dominions
            .credit_property_tax_with(state, quote.property_revenue, charge)
            .map(Some),
        None if charge() => Ok(None),
        None => Err(DominionError::Resources),
    }
}

/// Called with housing.operation_lock held. Only the current owning expedition's leader may
/// collect; a staged system receipt must succeed before any property ledger is cleared.
pub fn collect_locked(
    server: &GameServer,
    player: &PlayerRef,
    zone: u32,
) -> Result<DominionState, DominionError> {
    let instance = player.data().unit.transform.instance_id;
    let state = authority_for_zone(server, zone, instance).ok_or(DominionError::Missing)?;
    let owner = super::fort::owner(server, &state).ok_or(DominionError::WrongOwner)?;
    if owner.character_id != player.character_id
        || player.data().unit.expedition_id != owner.expedition_id
        || server
            .expeditions
            .expedition_of_character(player.character_id)
            != owner.expedition_id
    {
        return Err(DominionError::WrongOwner);
    }
    let now = manager::now();
    let receiver = server
        .names
        .get_character_name(player.character_id)
        .ok_or(DominionError::WrongOwner)?;
    server
        .systems
        .housing
        .dominions
        .collect_property_tax_with(&state, now, |amount, start| {
            server.systems.housing.hooks.get().send_return_mails(
                server,
                vec![HouseMail {
                    mail_type: crate::systems::economy::mail::mail_type::BALANCE_RECEIPT,
                    receiver_id: player.character_id,
                    receiver_name: Some(receiver),
                    sender_name: ".balanceReceipt".into(),
                    extra: i64::from(zone) << 48,
                    title: format!("title({zone})"),
                    // Original 1.2 locale.mail.balanceReceipt.body's eleven arguments.
                    text: format!(
                        "body('{start}','{now}','{amount}','0','0','0','{amount}','0','0','0','0')"
                    ),
                    copper_coins: amount,
                    send_date_ms: now.saturating_mul(1000),
                    recv_date_ms: now.saturating_mul(1000),
                    ..HouseMail::default()
                }],
                None,
            )
        })
}

/// Call after ownership/rate changes while the operation lock is held. Existing property
/// bills follow the current province, so they cannot pay a captured territory's former lord.
pub fn refresh_bills(server: &GameServer, zone_group_id: u32) {
    for house in server.systems.housing.manager.all() {
        let transform = house.data().unit.transform;
        if server
            .game_data
            .zones
            .zone_by_key(transform.zone_id)
            .is_some_and(|zone| zone.group_id == zone_group_id)
        {
            manager::update_tax_info(server, &house);
        }
    }
}
