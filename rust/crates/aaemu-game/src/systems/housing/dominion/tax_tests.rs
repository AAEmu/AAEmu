use std::collections::BTreeMap;
use std::sync::Arc;

use super::state::{DominionError, DominionManager, DominionState, TowerIdentity, Treasury};
use super::tax::*;
use crate::data::features::Feature;
use crate::data::items::ItemTemplate;
use crate::data::world_data::{SystemFaction, Zone};
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::models::house::House;
use crate::models::item::Item;
use crate::server::GameServer;
use crate::systems::economy::bridges::{EconomyComposite, EconomyForHousing};
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::mail;
use crate::systems::housing::config::DominionPropertyTaxBase;
use crate::systems::housing::data::{
    GuardTowerSetting, HousingItemHousings, HousingTemplate, Taxation,
};
use crate::systems::housing::hooks::{HouseMail, HousingEconomyHooks};
use crate::systems::housing::manager::{self, TaxInfo, TAX_CERTIFICATE};
use crate::systems::housing::{ops, packets::CSCreateHousePacket};
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{Entity, HouseRef, PlayerRef};

fn authority(rate: i32) -> DominionState {
    let mut state = DominionState::unowned(
        25,
        TowerIdentity {
            house_id: 500,
            instance_id: 0,
            guard_tower_setting_id: 6,
        },
    );
    state.owner_expedition_id = 10;
    state.ownership_generation = 1;
    state.reign_start_at = 1000;
    state.tax_rate = rate;
    state
}

#[test]
fn configurable_surcharge_excludes_deposit_and_wire_certificate_count_matches_ceil_payment() {
    let info = TaxInfo {
        one_week_tax_count: 150_000,
        ..TaxInfo::default()
    };
    let after = quote(
        info,
        100_000,
        true,
        Some(authority(25)),
        DominionPropertyTaxBase::AfterHeavy,
        true,
    )
    .unwrap();
    assert_eq!(
        (
            after.property_revenue,
            after.deposit,
            after.info.total_tax_to_pay
        ),
        (37_500, 200_000, 390_000)
    );
    let base = quote(
        info,
        100_000,
        true,
        Some(authority(25)),
        DominionPropertyTaxBase::Base,
        true,
    )
    .unwrap();
    assert_eq!(
        (
            base.property_revenue,
            base.deposit,
            base.info.total_tax_to_pay
        ),
        (25_000, 200_000, 380_000)
    );
    for (nominal, charged, count) in [
        (152_500, 160_000, 16),
        (1, 10_000, 1),
        (10_001, 20_000, 2),
        (0, 0, 0),
    ] {
        let q = quote(
            TaxInfo {
                one_week_tax_count: nominal,
                ..TaxInfo::default()
            },
            0,
            false,
            None,
            DominionPropertyTaxBase::AfterHeavy,
            true,
        )
        .unwrap();
        assert_eq!(q.info.total_tax_to_pay, charged);
        assert_eq!(
            crate::systems::housing::hooks::construction_certificates(q.info.total_tax_to_pay),
            count
        );
        // Verified native CountTaxItemForTax: floor(wire/10000 + 0.5).
        assert_eq!(((f64::from(charged) / 10000.0) + 0.5).floor() as i32, count);
    }
    let gold = quote(
        info,
        100_000,
        false,
        Some(authority(25)),
        DominionPropertyTaxBase::AfterHeavy,
        false,
    )
    .unwrap();
    assert_eq!(gold.info.total_tax_to_pay, 187_500);
    assert!(quote(
        TaxInfo {
            one_week_tax_count: i32::MAX,
            ..TaxInfo::default()
        },
        100_000,
        false,
        Some(authority(50)),
        DominionPropertyTaxBase::AfterHeavy,
        true
    )
    .is_err());
}

#[test]
fn revenue_preflight_rejects_stale_and_overflow_before_charge_and_collection_preserves_peace() {
    let manager = DominionManager::default();
    let state = authority(25);
    manager.restore(vec![state.clone()]).unwrap();
    assert_eq!(
        manager.credit_property_tax_with(&state, 25000, || false),
        Err(DominionError::Resources)
    );
    assert_eq!(manager.get(25), Some(state.clone()));
    let mut stale = state.clone();
    stale.ownership_generation += 1;
    assert_eq!(
        manager.credit_property_tax_with(&stale, 25000, || panic!("stale quote must not debit")),
        Err(DominionError::StaleTaxQuote)
    );
    manager
        .credit_treasury(
            25,
            Treasury {
                cur_house_tax_money: i32::MAX,
                peace_tax_money: 123,
                ..Treasury::default()
            },
        )
        .unwrap();
    assert_eq!(
        manager.credit_property_tax_with(&state, 1, || panic!("overflow must not debit")),
        Err(DominionError::InvalidTreasury)
    );
    assert_eq!(
        manager.collect_property_tax_with(&state, 2000, |_, _| false),
        Err(DominionError::Resources)
    );
    let paid = manager
        .collect_property_tax_with(&state, 2000, |amount, start| {
            amount == i32::MAX && start == 1000
        })
        .unwrap();
    assert_eq!(
        (
            paid.treasury.cur_house_tax_money,
            paid.treasury.peace_tax_money,
            paid.last_paid_at
        ),
        (0, 123, 2000)
    );
    assert_eq!(
        manager.collect_property_tax_with(&paid, 2001, |_, _| panic!(
            "repeated empty collection must not create mail"
        )),
        Ok(paid)
    );
}

pub(crate) struct Fixture {
    pub(crate) server: GameServer,
    pub(crate) payer: PlayerRef,
    pub(crate) leader: PlayerRef,
    successor: PlayerRef,
    tower: HouseRef,
}

pub(crate) fn fixture() -> Fixture {
    let mut data = GameData::empty();
    data.features.set(Feature::TaxItem, true);
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            zone_key: TEST_ZONE,
            group_id: 25,
            ..Default::default()
        },
    );
    data.systems.housing.add_template(HousingTemplate {
        id: 139,
        guard_tower_setting_id: 6,
        hp: 1000,
        ..Default::default()
    });
    data.systems.housing.castles.guard_tower_settings.insert(
        6,
        GuardTowerSetting {
            id: 6,
            radius_dominion: 100,
            ..Default::default()
        },
    );
    for (design, item, heavy) in [(300, 9000, true), (301, 9001, false)] {
        data.systems.housing.add_template(HousingTemplate {
            id: design,
            hp: 1000,
            garden_radius: 1.0,
            heavy_tax: heavy,
            taxation: Some(Taxation {
                id: 2,
                tax: 100_000,
                show: true,
            }),
            build_steps: BTreeMap::new(),
            ..Default::default()
        });
        data.systems.housing.add_item_housing(HousingItemHousings {
            item_id: item,
            design_id: design,
            ..Default::default()
        });
        data.items
            .templates
            .insert(item, ItemTemplate::plain(item, 1));
    }
    for id in [TAX_CERTIFICATE, manager::BOUND_TAX_CERTIFICATE] {
        data.items
            .templates
            .insert(id, ItemTemplate::plain(id, 10000));
    }
    let server = test_server_with(data);
    server
        .systems
        .housing
        .hooks
        .install(Arc::new(EconomyForHousing));
    server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(EconomyComposite));
    let mut players = Vec::new();
    for (id, name, expedition) in [(1, "Lord", 10), (2, "Taxpayer", 0), (3, "Newlord", 20)] {
        server.names.add_character(id, name, 1000 + id);
        let client = TestClient::new(&server, id, 1000 + id);
        let mut character = test_character(id, name, 600.0, 100.0);
        character.unit.expedition_id = expedition;
        players.push(spawn_player(
            &server,
            &client,
            character,
            server.ids.object.get_next_id().unwrap(),
        ));
    }
    for (expedition, leader) in [(10, &players[0]), (20, &players[2])] {
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id: expedition,
                owner_id: leader.character_id,
                ..Default::default()
            },
            members: vec![member_from_character(&server, expedition, leader, false)],
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
    }
    let mut h = House::new(500, 139);
    h.current_step = -1;
    h.unit.hp = 1000;
    h.unit.max_hp = 1000;
    h.unit.transform.zone_id = TEST_ZONE;
    h.unit.transform.set_xyz(100.0, 100.0, 100.0);
    let tower = Entity::new(server.ids.object.get_next_id().unwrap(), h);
    server.systems.housing.manager.insert(&tower);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&tower, &server.view_ctx());
    let (_, identity) = super::runtime::tower_identity(&server, &tower).unwrap();
    let state = server
        .systems
        .housing
        .dominions
        .declare_unowned_with(25, identity, 10, manager::now() - 1000, || true)
        .unwrap();
    server
        .systems
        .housing
        .dominions
        .set_tax_rate(25, state.owner_expedition_id, 25, manager::now())
        .unwrap();
    Fixture {
        server,
        leader: players.remove(0),
        payer: players.remove(0),
        successor: players.remove(0),
        tower,
    }
}

pub(crate) fn give(f: &Fixture, template: u32, count: i32, slot: i32) -> u64 {
    let id = u64::from(f.server.ids.item.get_next_id().unwrap());
    let item = Item::create(
        id,
        f.server.game_data.items.template(template).unwrap(),
        count,
        0,
        1,
        &f.server.game_data.items,
        0,
    );
    let container = Inv::of(&f.payer).get(slot_type::INVENTORY).unwrap();
    container
        .lock()
        .unwrap()
        .add_new_item(item, slot, &f.server.game_data.items)
        .unwrap();
    f.server.items.add_container(container);
    id
}

pub(crate) fn place(f: &Fixture, design: u32, item: u32, x: f32, slot: i32) -> HouseRef {
    let item_id = give(f, item, 1, slot);
    ops::build(
        &f.server,
        &f.payer,
        1002,
        &CSCreateHousePacket {
            design_id: design,
            x,
            y: 100.0,
            z: 100.0,
            z_rot: 0.0,
            item_id,
            money_amount: 0,
            ht: 0,
            auto_use_aa_point: false,
        },
    );
    f.server
        .systems
        .housing
        .manager
        .all()
        .into_iter()
        .find(|h| h.data().unit.template_id == design)
        .unwrap()
}

pub(crate) fn certificates(f: &Fixture) -> i32 {
    Inv::of(&f.payer).items_count_in(slot_type::INVENTORY, TAX_CERTIFICATE, -1)
}

#[tokio::test]
async fn province_construction_and_weekly_mail_pay_actual_surcharge_once_and_exempt_properties_do_not(
) {
    let f = fixture();
    give(&f, TAX_CERTIFICATE, 100, 5);
    // 500m from the lodestone, outside its 100m castle radius, still in its taxable province.
    let quote = quote_at(
        &f.server,
        1002,
        f.server.game_data.systems.housing.template(300).unwrap(),
        true,
        0,
        600.0,
        100.0,
    )
    .unwrap();
    assert_eq!(
        (
            quote.info.hostile_tax_rate,
            quote.deposit,
            quote.info.total_tax_to_pay
        ),
        (25, 200_000, 330_000)
    );
    let house = place(&f, 300, 9000, 600.0, 0);
    assert_eq!(certificates(&f), 67);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        25000
    );
    let _exempt = place(&f, 301, 9001, 650.0, 1);
    assert_eq!(certificates(&f), 37);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        25000
    );
    house.data().protection_end_date = manager::now() + 6 * 86400;
    manager::update_tax_info(&f.server, &house);
    let id = f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id)[0];
    let bill = f.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(bill.body.billing_amount, 130000);
    let end = house.data().protection_end_date;
    assert!(mail::pay_housing_tax(&f.server, &f.payer, id, false));
    assert_eq!(certificates(&f), 24);
    assert_eq!(house.data().protection_end_date, end + 7 * 86400);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        50000
    );
    assert!(!mail::pay_housing_tax(&f.server, &f.payer, id, false));
    assert_eq!(certificates(&f), 24);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        50000
    );
}

#[tokio::test]
async fn collection_receipt_requires_current_leader_and_failed_mail_preserves_balance_and_peace_fund(
) {
    let f = fixture();
    give(&f, TAX_CERTIFICATE, 100, 5);
    place(&f, 300, 9000, 600.0, 0);
    f.server
        .systems
        .housing
        .dominions
        .credit_treasury(
            25,
            Treasury {
                peace_tax_money: 123,
                ..Treasury::default()
            },
        )
        .unwrap();
    assert_eq!(
        collect_locked(&f.server, &f.payer, 25),
        Err(DominionError::WrongOwner)
    );
    struct FailedMail;
    impl HousingEconomyHooks for FailedMail {
        fn send_return_mails(
            &self,
            _: &GameServer,
            _: Vec<HouseMail>,
            _: Option<(&PlayerRef, i32)>,
        ) -> bool {
            false
        }
    }
    f.server.systems.housing.hooks.install(Arc::new(FailedMail));
    assert_eq!(
        collect_locked(&f.server, &f.leader, 25),
        Err(DominionError::Resources)
    );
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        25000
    );
    f.server
        .systems
        .housing
        .hooks
        .install(Arc::new(EconomyForHousing));
    let state = collect_locked(&f.server, &f.leader, 25).unwrap();
    assert_eq!(
        (
            state.treasury.cur_house_tax_money,
            state.treasury.peace_tax_money
        ),
        (0, 123)
    );
    let receipt = mail::get_current_mail_list_for(&f.server, 1);
    let receipts: Vec<_> = receipt
        .iter()
        .filter_map(|id| f.server.systems.economy.mail.get_mail_by_id(*id))
        .filter(|m| m.mail_type == mail::mail_type::BALANCE_RECEIPT)
        .collect();
    assert_eq!(receipts.len(), 1);
    assert_eq!(receipts[0].body.copper_coins, 25000);
    assert!(receipts[0].body.text.ends_with("'25000','0','0','0','0')"));
    collect_locked(&f.server, &f.leader, 25).unwrap();
    assert_eq!(
        mail::get_current_mail_list_for(&f.server, 1).len(),
        receipt.len()
    );
}

#[tokio::test]
async fn failed_weekly_payment_preserves_certificates_protection_bill_and_territory_revenue() {
    let f = fixture();
    give(&f, TAX_CERTIFICATE, 35, 5);
    let house = place(&f, 300, 9000, 600.0, 0);
    house.data().protection_end_date = manager::now() + 6 * 86400;
    manager::update_tax_info(&f.server, &house);
    let mail_id = f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id)[0];
    let before = house.data().protection_end_date;
    let authority = f.server.systems.housing.dominions.get(25).unwrap();
    assert!(!mail::pay_housing_tax(&f.server, &f.payer, mail_id, false));
    assert_eq!(certificates(&f), 2);
    assert_eq!(house.data().protection_end_date, before);
    assert_eq!(f.server.systems.housing.dominions.get(25), Some(authority));
    assert_eq!(
        f.server
            .systems
            .economy
            .mail
            .get_mail_by_id(mail_id)
            .unwrap()
            .header
            .status,
        mail::mail_status::UNPAID
    );
    give(&f, TAX_CERTIFICATE, 20, 6);
    assert!(mail::pay_housing_tax(&f.server, &f.payer, mail_id, false));
    assert_eq!(certificates(&f), 9);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        50_000
    );
}

#[tokio::test]
async fn stale_province_authority_and_capture_quotes_never_charge_or_credit_previous_owner() {
    let f = fixture();
    let quote = quote_at(
        &f.server,
        1002,
        f.server.game_data.systems.housing.template(300).unwrap(),
        true,
        0,
        600.0,
        100.0,
    )
    .unwrap();
    let mut captured = f.server.systems.housing.dominions.get(25).unwrap();
    captured.owner_expedition_id = 20;
    captured.ownership_generation += 1;
    f.server
        .systems
        .housing
        .dominions
        .restore(vec![captured])
        .unwrap();
    assert_eq!(
        charge_with(&f.server, &quote, || panic!(
            "stale captured quote must not charge"
        )),
        Err(DominionError::StaleTaxQuote)
    );
    assert_eq!(
        collect_locked(&f.server, &f.leader, 25),
        Err(DominionError::WrongOwner)
    );
    assert!(collect_locked(&f.server, &f.successor, 25).is_ok());
    assert_eq!(
        quote_at(
            &f.server,
            1002,
            f.server.game_data.systems.housing.template(300).unwrap(),
            false,
            1,
            600.0,
            100.0
        )
        .unwrap()
        .property_revenue,
        0
    );
    f.server.world.world(0).unwrap().delete(&f.tower);
    assert!(authority_at(&f.server, 0, 600.0, 100.0).is_none());
    assert_eq!(
        quote_at(
            &f.server,
            1002,
            f.server.game_data.systems.housing.template(300).unwrap(),
            false,
            0,
            600.0,
            100.0
        )
        .unwrap()
        .property_revenue,
        0
    );
}

#[tokio::test]
async fn normal_leader_can_collect_but_cannot_access_siege_administration() {
    use crate::systems::services::commands::{output::PlayerCharacter, scripts, CommandContext};
    let f = fixture();
    let commands = &f.server.systems.services.commands;
    scripts::install(commands, scripts::ScriptEnv::default());
    assert!(commands.reload());
    commands
        .access_levels
        .load(&[("collectdominion".into(), 0)]);
    f.server
        .systems
        .housing
        .dominions
        .credit_treasury(
            25,
            Treasury {
                cur_house_tax_money: 25_000,
                ..Treasury::default()
            },
        )
        .unwrap();
    let ctx = CommandContext {
        server: Some(&f.server),
        character: PlayerCharacter::arc(&f.payer),
    };
    let (handled, _) = commands
        .handle_with_level_async(&ctx, 0, "collectdominion 25")
        .await;
    assert!(handled);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        25_000
    );
    let ctx = CommandContext {
        server: Some(&f.server),
        character: PlayerCharacter::arc(&f.leader),
    };
    let (handled, _) = commands
        .handle_with_level_async(&ctx, 0, "collectdominion 25")
        .await;
    assert!(handled);
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .get(25)
            .unwrap()
            .treasury
            .cur_house_tax_money,
        0
    );
    assert_eq!(
        f.server
            .systems
            .economy
            .mail
            .state()
            .all
            .iter()
            .filter(|(_, m)| m.mail_type == mail::mail_type::BALANCE_RECEIPT)
            .count(),
        1
    );
    commands
        .handle_with_level_async(&ctx, 0, "collectdominion 25")
        .await;
    assert_eq!(
        f.server
            .systems
            .economy
            .mail
            .state()
            .all
            .iter()
            .filter(|(_, m)| m.mail_type == mail::mail_type::BALANCE_RECEIPT)
            .count(),
        1
    );
    commands
        .handle_with_level_async(&ctx, 0, "dominion schedule 25 20 1")
        .await;
    commands
        .handle_with_level_async(&ctx, 0, "collectdominion schedule")
        .await;
    assert!(f
        .server
        .systems
        .housing
        .dominions
        .get(25)
        .unwrap()
        .siege
        .is_none());
}
