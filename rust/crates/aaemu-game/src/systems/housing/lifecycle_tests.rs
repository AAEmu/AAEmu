//! Construction failures and success through the real economy bridge, without MySQL.

use std::collections::BTreeMap;
use std::sync::Arc;

use super::data::{
    HousingBuildStep, HousingDecoration, HousingItemHousings, HousingTemplate,
    ItemHousingDecoration, Taxation,
};
use super::hooks::{construction_certificates, HouseConstructionError, HouseMail, MailAttachment};
use super::manager::{self, BOUND_TAX_CERTIFICATE, TAX_CERTIFICATE};
use super::ops;
use super::packets::CSCreateHousePacket;
use crate::data::features::Feature;
use crate::data::items::ItemTemplate;
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::models::doodad::Doodad;
use crate::models::item::Item;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::bridges::EconomyForHousing;
use crate::systems::economy::inventory::Inv;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, HouseRef, PlayerRef, WorldObject};

const DESIGN: u32 = 300;
const DESIGN_ITEM: u32 = 9000;
const CHAIR_ITEM: u32 = 8000;
const CHAIR_DOODAD: u32 = 5000;

struct Fixture {
    server: GameServer,
    player: PlayerRef,
    client: TestClient,
}

fn fixture(tax_item: bool, money: i64) -> Fixture {
    fixture_with_plot(tax_item, money, None)
}

fn fixture_with_plot(tax_item: bool, money: i64, can_extend: Option<bool>) -> Fixture {
    fixture_with_data(tax_item, money, can_extend, GameData::empty())
}

fn fixture_with_data(
    tax_item: bool,
    money: i64,
    can_extend: Option<bool>,
    mut data: GameData,
) -> Fixture {
    data.zones.zones.insert(
        crate::test_support::TEST_ZONE,
        crate::data::world_data::Zone {
            id: 1,
            zone_key: crate::test_support::TEST_ZONE,
            group_id: 25,
            ..Default::default()
        },
    );
    data.features.set(Feature::TaxItem, tax_item);
    data.systems.housing.add_template(HousingTemplate {
        id: DESIGN,
        hp: 1000,
        main_model_id: 700,
        garden_radius: 8.0,
        taxation: Some(Taxation {
            id: 1,
            tax: 100_001,
            show: true,
        }),
        build_steps: BTreeMap::from([(
            0,
            HousingBuildStep {
                id: 1,
                housing_id: DESIGN,
                step: 0,
                model_id: 710,
                skill_id: 11000,
                num_actions: 2,
            },
        )]),
        ..Default::default()
    });
    data.systems.housing.add_item_housing(HousingItemHousings {
        id: 1,
        item_id: DESIGN_ITEM,
        design_id: DESIGN,
    });
    data.systems
        .housing
        .add_decoration(HousingDecoration {
            id: 50,
            doodad_id: CHAIR_DOODAD,
            ..Default::default()
        })
        .unwrap();
    data.systems
        .housing
        .add_item_housing_decoration(ItemHousingDecoration {
            id: 1,
            item_id: CHAIR_ITEM,
            design_id: 50,
            restore: true,
        });
    data.systems.npcs.doodad.templates.insert(
        CHAIR_DOODAD,
        crate::systems::npcs::doodad::templates::DoodadTemplate {
            id: CHAIR_DOODAD,
            ..Default::default()
        },
    );
    for (id, max) in [
        (DESIGN_ITEM, 1),
        (CHAIR_ITEM, 1),
        (TAX_CERTIFICATE, 1000),
        (BOUND_TAX_CERTIFICATE, 1000),
    ] {
        data.items
            .templates
            .insert(id, ItemTemplate::plain(id, max));
    }
    if let Some(can_extend) = can_extend {
        data.systems
            .npcs
            .subzone
            .housing_zones
            .entry(0)
            .or_default()
            .insert(
                1,
                vec![crate::systems::npcs::subzone::Area {
                    id: 5,
                    points: vec![
                        [90.0, 90.0, 0.0],
                        [150.0, 90.0, 0.0],
                        [150.0, 150.0, 0.0],
                        [90.0, 150.0, 0.0],
                    ],
                    ..Default::default()
                }],
            );
        data.systems.housing.areas.insert(5, 13);
        data.systems.housing.groups.insert(
            13,
            super::data::HousingGroup {
                id: 13,
                can_extend,
                ..Default::default()
            },
        );
        data.systems.housing.group_categories.insert(13, vec![0]);
    }
    let server = test_server_with(data);
    server.names.add_character(1, "Builder", 1001);
    server
        .systems
        .housing
        .hooks
        .install(Arc::new(EconomyForHousing));
    let mut client = TestClient::new(&server, 1, 1001);
    let mut character = test_character(1, "Builder", 100.0, 100.0);
    character.money = money;
    let obj_id = server.ids.object.get_next_id().unwrap();
    let player = spawn_player(&server, &client, character, obj_id);
    client.drain();
    Fixture {
        server,
        player,
        client,
    }
}

fn give(f: &Fixture, slot_type: u8, template_id: u32, count: i32, slot: i32) -> u64 {
    let id = f.server.ids.item.get_next_id().unwrap() as u64;
    let item = Item::create(
        id,
        f.server.game_data.items.template(template_id).unwrap(),
        count,
        0,
        1,
        &f.server.game_data.items,
        0,
    );
    let container = Inv::of(&f.player).get(slot_type).unwrap();
    container
        .lock()
        .unwrap()
        .add_new_item(item, slot, &f.server.game_data.items)
        .unwrap();
    f.server.items.add_container(container);
    id
}

fn count(f: &Fixture, template_id: u32) -> i32 {
    Inv::of(&f.player).items_count_in(slot_type::INVENTORY, template_id, -1)
}

fn packet(item_id: u64) -> CSCreateHousePacket {
    CSCreateHousePacket {
        design_id: DESIGN,
        x: 120.0,
        y: 100.0,
        z: 100.0,
        z_rot: 0.0,
        item_id,
        money_amount: 0,
        ht: 0,
        auto_use_aa_point: false,
    }
}

fn return_mail(attachments: Vec<MailAttachment>) -> HouseMail {
    HouseMail {
        receiver_id: 1,
        receiver_name: Some("Builder".into()),
        attachments,
        ..Default::default()
    }
}

fn chair(f: &Fixture, house: &HouseRef) -> (crate::world::DoodadRef, u64) {
    let item_id = give(f, slot_type::SYSTEM, CHAIR_ITEM, 1, 0);
    let mut d = Doodad::new(CHAIR_DOODAD);
    d.item_id = item_id;
    d.item_template_id = CHAIR_ITEM;
    d.owner_id = 1;
    d.owner_db_id = house.data().id;
    d.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
    d.attach_point = crate::models::unit::attach_point::NONE;
    d.parent_obj_id = house.obj_id;
    let d = Entity::new(f.server.ids.object.get_next_id().unwrap(), d);
    let world = f.server.world.world(0).unwrap();
    world.attach_child(&WorldObject::from(house), &WorldObject::from(&d));
    world.spawn(&d, &f.server.view_ctx());
    (d, item_id)
}

#[tokio::test]
async fn construction_pays_complete_gold_and_consumes_selected_design_once() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    let other = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 1);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(f.server.systems.housing.manager.count(), 1);
    assert_eq!(f.player.data().money, 99_997);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
    assert!(Inv::of(&f.player).item_by_id(selected).is_none());
    assert!(Inv::of(&f.player).item_by_id(other).is_some());
    // A replay cannot charge again or consume another design.
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(f.player.data().money, 99_997);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
    assert_eq!(f.server.systems.housing.manager.count(), 1);
}

#[tokio::test]
async fn new_house_prepaid_period_is_paid_before_first_bill_and_renewal_covers_one_week() {
    use crate::systems::economy::{inventory::ItemTxn, mail};
    let mut f = fixture(false, 500_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    let (place, due, protected, tl) = {
        let h = house.data();
        (
            h.place_date,
            manager::tax_due_date(&h),
            h.protection_end_date,
            h.unit.tl_id,
        )
    };
    assert_eq!(due, place + 7 * 86400);
    assert_eq!(protected, place + 14 * 86400);
    assert!(due > manager::now());
    assert_eq!(f.player.data().money, 199_997); // first period + deposit already charged
    assert!(f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id)
        .is_empty());
    f.client.drain();
    ops::house_tax_info(&f.server, &f.player, tl);
    let body = f
        .client
        .drain()
        .into_iter()
        .find(|(op, _)| *op == sc::SCHouseTaxInfoPacket)
        .unwrap()
        .1;
    assert_eq!(body[22], 1); // original 1.2 paid flag
    assert_eq!(i32::from_le_bytes(body[23..27].try_into().unwrap()), -1);

    // Move the persisted schedule past the prepaid period without sleeping seven days.
    {
        let mut h = house.data();
        h.place_date = manager::now() - 7 * 86400 - 1;
        h.protection_end_date = h.place_date + 14 * 86400;
    }
    manager::update_tax_info(&f.server, &house);
    let mails = f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id);
    assert_eq!(mails.len(), 1);
    f.server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(crate::systems::economy::bridges::EconomyComposite));
    let before = house.data().protection_end_date;
    {
        let mut txn = ItemTxn::begin(&f.server);
        assert!(mail::pay_charge_money(&mut txn, &f.player, mails[0], false));
    }
    assert_eq!(house.data().protection_end_date, before + 7 * 86400);
    assert_eq!(f.player.data().money, 99_996);
    manager::update_tax_info(&f.server, &house);
    assert!(f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id)
        .is_empty());
    f.client.drain();
    ops::house_tax_info(&f.server, &f.player, tl);
    let body = f
        .client
        .drain()
        .into_iter()
        .find(|(op, _)| *op == sc::SCHouseTaxInfoPacket)
        .unwrap()
        .1;
    assert_eq!(body[22], 1);
    assert_eq!(i32::from_le_bytes(body[23..27].try_into().unwrap()), -1);
}

#[tokio::test]
async fn nonrenewable_beginner_construction_keeps_authored_seven_day_lifetime() {
    let f = fixture_with_plot(false, 500_000, Some(false));
    // Changing ordinary payment coverage must not extend the authored beginner-plot lifetime.
    f.server
        .config
        .update(|config| config.world.days_for_tax_payment = 14);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    {
        let h = house.data();
        assert_eq!(h.protection_end_date, h.place_date + 7 * 86400);
    }
    assert!(!manager::pay_weekly_tax_for(&f.server, &house, Some(1)));
    assert!(f
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(house.data().id)
        .is_empty());
}

#[tokio::test]
async fn insufficient_gold_keeps_design_and_releases_all_staged_identifiers() {
    let mut f = fixture(false, 300_002);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    let obj = f.server.ids.object.get_next_id().unwrap();
    f.server.ids.object.release_id(obj);
    let tl = f.server.ids.housing_tl.get_next_id().unwrap();
    f.server.ids.housing_tl.release_id(tl);
    let id = f.server.ids.housing.get_next_id().unwrap();
    f.server.ids.housing.release_id(id);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(f.player.data().money, 300_002);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
    assert_eq!(f.server.systems.housing.manager.count(), 0);
    assert_eq!(f.server.ids.object.get_next_id().unwrap(), obj);
    assert_eq!(f.server.ids.housing_tl.get_next_id().unwrap(), tl);
    assert_eq!(f.server.ids.housing.get_next_id().unwrap(), id);
    assert_eq!(f.client.drain_ops(), vec![sc::SCErrorMsgPacket]);
}

#[tokio::test]
async fn insufficient_certificates_do_not_partially_consume_bound_taxes_or_design() {
    let f = fixture(true, 0);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    give(&f, slot_type::INVENTORY, BOUND_TAX_CERTIFICATE, 20, 1);
    give(&f, slot_type::INVENTORY, TAX_CERTIFICATE, 10, 2);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(count(&f, BOUND_TAX_CERTIFICATE), 20);
    assert_eq!(count(&f, TAX_CERTIFICATE), 10);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
    assert_eq!(f.server.systems.housing.manager.count(), 0);
}

#[tokio::test]
async fn construction_rounds_certificates_up_and_consumes_bound_first() {
    let f = fixture(true, 999);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    give(&f, slot_type::INVENTORY, BOUND_TAX_CERTIFICATE, 25, 1);
    give(&f, slot_type::INVENTORY, TAX_CERTIFICATE, 10, 2);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(count(&f, BOUND_TAX_CERTIFICATE), 0);
    assert_eq!(count(&f, TAX_CERTIFICATE), 4);
    assert_eq!(count(&f, DESIGN_ITEM), 0);
    assert_eq!(f.player.data().money, 999);
    assert_eq!(f.server.systems.housing.manager.count(), 1);
}

#[tokio::test]
async fn bank_design_cannot_charge_tax_or_build() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::BANK, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    assert_eq!(f.player.data().money, 400_000);
    assert!(Inv::of(&f.player).item_by_id(selected).is_some());
    assert_eq!(f.server.systems.housing.manager.count(), 0);
}

#[tokio::test]
async fn atomic_payment_rechecks_the_selected_design_before_touching_money() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    let hooks = f.server.systems.housing.hooks.get();
    let mut source = hooks
        .inventory_item(&f.server, &f.player, selected)
        .unwrap();
    source.item_id += 100;
    assert_eq!(
        hooks.pay_house_construction(&f.server, &f.player, source, 300_003, false),
        Err(HouseConstructionError::InvalidDesign)
    );
    assert_eq!(f.player.data().money, 400_000);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
}

#[tokio::test]
async fn construction_rejects_nonfinite_location_rotation_and_wrong_account() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    for bad in [f32::NAN, f32::INFINITY, f32::NEG_INFINITY] {
        for field in 0..4 {
            let mut p = packet(selected);
            match field {
                0 => p.x = bad,
                1 => p.y = bad,
                2 => p.z = bad,
                _ => p.z_rot = bad,
            }
            ops::build(&f.server, &f.player, 1001, &p);
        }
    }
    ops::build(&f.server, &f.player, 1002, &packet(selected));
    assert_eq!(f.player.data().money, 400_000);
    assert_eq!(count(&f, DESIGN_ITEM), 1);
    assert_eq!(f.server.systems.housing.manager.count(), 0);
}

#[tokio::test]
async fn completed_and_abandoned_houses_do_not_accept_construction_actions() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    assert!(!ops::craft_building(&f.server, &f.player, &house, 999));
    assert_eq!(house.data().current_action(), 0);
    assert!(f.player.data().skill_char.skill_cancelled);
    assert!(ops::craft_building(&f.server, &f.player, &house, 11000));
    assert!(ops::craft_building(&f.server, &f.player, &house, 11000));
    assert_eq!(house.data().current_step, -1);
    assert!(!ops::craft_building(&f.server, &f.player, &house, 11000));
    manager::set_current_step(&f.server, &house, 0, false);
    house.data().unit.owner_id = 0;
    assert!(!ops::craft_building(&f.server, &f.player, &house, 11000));
    assert_eq!(house.data().current_action(), 0);
}

#[test]
fn certificate_rounding_is_exact_for_large_and_fractional_gold_tax() {
    assert_eq!(construction_certificates(0), 0);
    assert_eq!(construction_certificates(10_000), 1);
    assert_eq!(construction_certificates(10_001), 2);
    assert_eq!(construction_certificates(2_147_480_001), 214_749);
    assert_eq!(construction_certificates(i32::MAX), 214_749);
}

#[tokio::test]
async fn invalid_later_return_mail_keeps_all_items_money_and_mail_store_unchanged() {
    let f = fixture(false, 400_000);
    let existing = give(&f, slot_type::SYSTEM, CHAIR_ITEM, 1, 0);
    let item_id = f.server.ids.item.get_next_id().unwrap();
    f.server.ids.item.release_id(item_id);
    f.server.items.mark_for_db_deletion(u64::from(item_id));
    let mut invalid = return_mail(vec![]);
    invalid.receiver_name = Some("Missing".into());
    let mails = vec![
        return_mail(vec![
            MailAttachment::Existing {
                item_id: existing,
                owner_id: 1,
                add_count: 0,
            },
            MailAttachment::NewItem {
                template_id: DESIGN_ITEM,
                count: 1,
                grade: 0,
                owner_id: 1,
            },
        ]),
        invalid,
    ];
    assert!(!f.server.systems.housing.hooks.get().send_return_mails(
        &f.server,
        mails,
        Some((&f.player, 1000))
    ));
    assert_eq!(f.player.data().money, 400_000);
    assert_eq!(
        f.server.items.item_by_id(existing).unwrap().slot_type,
        slot_type::SYSTEM
    );
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 0);
    assert_eq!(f.server.ids.item.get_next_id().unwrap(), item_id);
    assert!(f.server.items.removed_items().contains(&u64::from(item_id)));
}

#[tokio::test]
async fn missing_attachment_in_later_batch_does_not_publish_earlier_batch() {
    let f = fixture(false, 400_000);
    let valid = MailAttachment::NewItem {
        template_id: DESIGN_ITEM,
        count: 1,
        grade: 0,
        owner_id: 1,
    };
    let missing = MailAttachment::NewItem {
        template_id: 123_456,
        count: 1,
        grade: 0,
        owner_id: 1,
    };
    let mails = vec![return_mail(vec![valid; 10]), return_mail(vec![missing])];
    assert!(!f.server.systems.housing.hooks.get().send_return_mails(
        &f.server,
        mails,
        Some((&f.player, 1000))
    ));
    assert_eq!(f.player.data().money, 400_000);
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 0);
    for id in 1..=10 {
        assert!(f.server.items.item_by_id(id).is_none());
    }
}

#[tokio::test]
async fn failed_demolition_keeps_furniture_ownership_and_tax_protection() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    let (chair, item_id) = chair(&f, &house);
    let coffer = f
        .server
        .items
        .new_coffer_container(1, &f.server.ids.container);
    let container_id = coffer.lock().unwrap().container_id;
    let content_id = f.server.ids.item.get_next_id().unwrap() as u64;
    let content = Item::create(
        content_id,
        f.server.game_data.items.template(CHAIR_ITEM).unwrap(),
        1,
        0,
        1,
        &f.server.game_data.items,
        0,
    );
    coffer
        .lock()
        .unwrap()
        .add_new_item(content, 0, &f.server.game_data.items)
        .unwrap();
    chair.data().coffer = Some(crate::models::doodad::DoodadCofferState {
        container_id,
        capacity: 10,
        ..Default::default()
    });
    let before = house.data().clone();
    let mails_before = f.server.systems.economy.mail.state().all.len();
    // Simulate a broken recipient mapping: mail validation must precede furniture deletion.
    f.server.names.remove_character_id(1);
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    assert_eq!(house.data().unit.owner_id, before.unit.owner_id);
    assert_eq!(house.data().protection_end_date, before.protection_end_date);
    assert!(f
        .server
        .world
        .world(0)
        .unwrap()
        .doodad(chair.obj_id)
        .is_some());
    assert_eq!(
        f.server.items.item_by_id(item_id).unwrap().slot_type,
        slot_type::SYSTEM
    );
    assert!(f.server.systems.housing.manager.removed_ids().is_empty());
    assert!(coffer.lock().unwrap().get_item_by_id(content_id).is_some());
    assert_eq!(
        f.server.systems.economy.mail.state().all.len(),
        mails_before
    );
}

#[tokio::test]
async fn successful_demolition_returns_design_deposit_and_existing_furniture_once() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    let (chair, item_id) = chair(&f, &house);
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    assert_eq!(house.data().unit.owner_id, 0);
    assert!(f
        .server
        .world
        .world(0)
        .unwrap()
        .doodad(chair.obj_id)
        .is_none());
    assert_eq!(
        f.server.items.item_by_id(item_id).unwrap().slot_type,
        slot_type::MAIL
    );
    let state = f.server.systems.economy.mail.state();
    let mails = state.all.iter().map(|(_, mail)| mail).collect::<Vec<_>>();
    assert_eq!(mails.len(), 1);
    assert_eq!(mails[0].body.copper_coins, 200_002);
    assert_eq!(mails[0].body.attachments.len(), 2);
    drop(state);
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 1);
}

#[tokio::test]
async fn coffer_contents_are_returned_in_multiple_mails_before_coffer_deletion() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    let (chair, furniture_id) = chair(&f, &house);
    let coffer = f
        .server
        .items
        .new_coffer_container(1, &f.server.ids.container);
    let container_id = coffer.lock().unwrap().container_id;
    let mut contents = Vec::new();
    for slot in 0..10 {
        let id = f.server.ids.item.get_next_id().unwrap() as u64;
        let item = Item::create(
            id,
            f.server.game_data.items.template(CHAIR_ITEM).unwrap(),
            1,
            0,
            1,
            &f.server.game_data.items,
            0,
        );
        coffer
            .lock()
            .unwrap()
            .add_new_item(item, slot, &f.server.game_data.items)
            .unwrap();
        contents.push(id);
    }
    chair.data().coffer = Some(crate::models::doodad::DoodadCofferState {
        container_id,
        capacity: 10,
        ..Default::default()
    });
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    let state = f.server.systems.economy.mail.state();
    let mails = state.all.iter().map(|(_, mail)| mail).collect::<Vec<_>>();
    assert_eq!(mails.len(), 2);
    assert_eq!(mails[0].body.attachments.len(), 10);
    assert_eq!(mails[1].body.attachments.len(), 2);
    assert_eq!(mails[0].body.copper_coins, 200_002);
    assert_eq!(mails[1].body.copper_coins, 0);
    for id in contents.into_iter().chain([furniture_id]) {
        assert!(mails.iter().any(|mail| mail.body.attachments.contains(&id)));
        assert_eq!(
            f.server.items.item_by_id(id).unwrap().slot_type,
            slot_type::MAIL
        );
    }
    assert!(coffer.lock().unwrap().items.is_empty());
}

#[tokio::test]
async fn duplicate_existing_attachment_is_rejected_without_mutation() {
    let f = fixture(false, 400_000);
    let existing = give(&f, slot_type::SYSTEM, CHAIR_ITEM, 1, 0);
    let attachment = MailAttachment::Existing {
        item_id: existing,
        owner_id: 1,
        add_count: 0,
    };
    assert!(!f.server.systems.housing.hooks.get().send_return_mails(
        &f.server,
        vec![return_mail(vec![attachment]), return_mail(vec![attachment])],
        None
    ));
    assert_eq!(
        f.server.items.item_by_id(existing).unwrap().slot_type,
        slot_type::SYSTEM
    );
    assert!(f.server.systems.economy.mail.state().all.is_empty());
}

#[tokio::test]
async fn demolished_garden_plants_detach_in_world_coordinates_and_save_without_old_house() {
    let f = fixture(false, 400_000);
    let selected = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(selected));
    let house = f.server.systems.housing.manager.all()[0].clone();
    let mut plant = Doodad::new(9999);
    plant.attach_point = crate::models::unit::attach_point::NONE;
    plant.owner_id = 1;
    plant.owner_db_id = house.data().id;
    plant.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
    plant.parent_obj_id = house.obj_id;
    plant.is_persistent = true;
    plant.transform.set_xyz(1.0, 2.0, 3.0);
    let plant = Entity::new(f.server.ids.object.get_next_id().unwrap(), plant);
    let world = f.server.world.world(0).unwrap();
    world.attach_child(&WorldObject::from(&house), &WorldObject::from(&plant));
    world.spawn(&plant, &f.server.view_ctx());
    let position = world.world_pos_rot(&WorldObject::from(&plant));
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    assert!(world.doodad(plant.obj_id).is_some());
    let row = crate::systems::npcs::doodad::db::snapshot(&f.server, &plant).unwrap();
    assert_eq!(row.house_id, 0);
    assert_eq!(plant.data().parent_obj_id, 0);
    assert_eq!((row.x, row.y, row.z), (position.x, position.y, position.z));
}

const REMOVAL_DAMAGE: u32 = 1876;

fn removal_skills() -> crate::systems::skills::Data {
    use crate::systems::skills::data::{BuffTemplate, EffectKind, EffectTemplate, TickEffect};

    let mut data = crate::systems::skills::Data::default();
    data.add_buff(BuffTemplate {
        id: manager::BUFF_UNTOUCHABLE,
        stack_rule: 1,
        max_stack: 1,
        melee_immune: true,
        ranged_immune: true,
        spell_immune: true,
        siege_immune: true,
        remove_on_death: true,
        ..Default::default()
    });
    data.add_buff(BuffTemplate {
        id: manager::BUFF_REMOVAL_DEBUFF,
        tick: 15_000.0,
        system: true,
        stack_rule: 1,
        max_stack: 1,
        remove_on_death: true,
        tick_effects: vec![TickEffect {
            effect_id: 12864,
            target_buff_tag_id: 0,
            target_no_buff_tag_id: 0,
        }],
        ..Default::default()
    });
    let mut damage = crate::systems::skills::tests::damage_effect(REMOVAL_DAMAGE, 10);
    damage.damage_type = 3;
    data.add_effect(
        EffectKind::DamageEffect,
        EffectTemplate::Damage(Box::new(damage)),
        12864,
    );
    data
}

fn removal_fixture(skills: crate::systems::skills::Data) -> (Fixture, HouseRef) {
    let mut data = GameData::empty();
    data.systems.skills = skills;
    // Registered factions are essential: missing factions allow self attacks and hide the bug.
    for id in [101, manager::FACTION_MONSTROSITY] {
        data.factions.add(crate::data::world_data::SystemFaction {
            id,
            ..Default::default()
        });
    }
    data.systems
        .ai
        .collision
        .prefab_models
        .insert(700, vec!["cgf://objects/test/removal-house.cgf".into()]);
    let f = fixture_with_data(false, 400_000, None, data);
    crate::systems::skills::install_hooks(&f.server);
    let item = give(&f, slot_type::INVENTORY, DESIGN_ITEM, 1, 0);
    ops::build(&f.server, &f.player, 1001, &packet(item));
    let house = f.server.systems.housing.manager.all()[0].clone();
    manager::set_current_step(&f.server, &house, -1, false);
    ops::demolish(&f.server, Some(&f.player), &house, false, false);
    assert_eq!(house.data().unit.owner_id, 0);
    assert!(f.server.world.object(house.obj_id).is_some());
    (f, house)
}

fn removal_buff(house: &HouseRef) -> crate::models::buff::Buff {
    house
        .data()
        .unit
        .buffs
        .effects
        .iter()
        .find(|b| b.template.id == manager::BUFF_REMOVAL_DEBUFF)
        .unwrap()
        .clone()
}

fn removal_args<'a>(
    server: &GameServer,
    house: &HouseRef,
    skill_object: &'a crate::systems::skills::types::SkillObject,
) -> crate::systems::skills::effects::EffectArgs<'a> {
    use crate::systems::skills::effects::{EffectArgs, EffectSource};
    use crate::systems::skills::geometry::Target;
    use crate::systems::skills::types::{CastAction, SkillCastTarget, SkillCaster};

    EffectArgs {
        caster: Some(WorldObject::from(house)),
        caster_caster: SkillCaster::Unit(house.obj_id),
        target: Target::Obj(WorldObject::from(house)),
        target_caster: SkillCastTarget::Unit(house.obj_id),
        cast: CastAction::Buff {
            buff_id: manager::BUFF_REMOVAL_DEBUFF,
            owner_obj_id: house.obj_id,
            index: removal_buff(house).index,
        },
        source: EffectSource::from_buff(
            server
                .game_data
                .systems
                .skills
                .buff(manager::BUFF_REMOVAL_DEBUFF)
                .unwrap()
                .clone(),
        ),
        skill_object,
    }
}

#[tokio::test]
async fn abandoned_house_removal_tick_damages_only_after_demolition() {
    use crate::systems::skills::geometry::Target;
    use crate::systems::skills::{buffs, relation};

    let (f, house) = removal_fixture(removal_skills());
    let target = Target::Obj(WorldObject::from(&house));
    assert!(!relation::can_attack(&f.server, &target, &target));
    let buff = removal_buff(&house);
    assert_eq!(buff.tick, 15_000.0);
    assert_eq!(buff.count, -1);
    let hp = house.data().unit.hp;
    buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(house.data().unit.hp, hp - 10);
    assert!(f
        .server
        .systems
        .housing
        .manager
        .by_id(house.data().id)
        .is_some());
    assert!(f.server.world.object(house.obj_id).is_some());
    // The same active system buff on an owned house cannot bypass attack authorization.
    house.data().unit.owner_id = f.player.character_id;
    buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(house.data().unit.hp, hp - 10);
}

#[tokio::test]
async fn house_removal_does_not_bypass_immunity_or_accept_other_damage_sources() {
    use crate::systems::skills::effects::combat::damage;
    use crate::systems::skills::state::SkillRun;
    use crate::systems::skills::types::{CastAction, SkillCastTarget, SkillCaster, SkillObject};

    let (f, house) = removal_fixture(removal_skills());
    let object = SkillObject::None;
    let valid = removal_args(&f.server, &house, &object);
    let effect = crate::systems::skills::tests::damage_effect(REMOVAL_DAMAGE, 10);
    assert!(super::is_abandoned_house_removal_damage(
        &f.server, effect.id, &valid
    ));
    let mut rejected = Vec::new();
    let mut args = valid.clone();
    args.source.buff = None;
    rejected.push(args);
    let mut args = valid.clone();
    args.source.buff = Some(Arc::new((**args.source.buff.as_ref().unwrap()).clone()));
    rejected.push(args); // same ID on an untrusted template is insufficient
    let mut args = valid.clone();
    args.source.skill = Some(SkillRun::new(
        Arc::new(crate::systems::skills::tests::skill_template(11404)),
        1,
    ));
    rejected.push(args);
    let mut args = valid.clone();
    args.source.is_trigger = true;
    rejected.push(args);
    let mut args = valid.clone();
    args.cast = CastAction::Skill {
        skill_id: 11404,
        tl_id: 1,
    };
    rejected.push(args);
    let mut args = valid.clone();
    args.cast = CastAction::Buff {
        buff_id: 545,
        owner_obj_id: house.obj_id,
        index: removal_buff(&house).index,
    };
    rejected.push(args);
    let mut args = valid.clone();
    args.cast = CastAction::Buff {
        buff_id: manager::BUFF_REMOVAL_DEBUFF,
        owner_obj_id: house.obj_id,
        index: removal_buff(&house).index + 100,
    };
    rejected.push(args);
    let mut args = valid.clone();
    args.caster = Some(WorldObject::from(&f.player));
    args.caster_caster = SkillCaster::Unit(f.player.obj_id);
    rejected.push(args);
    let mut args = valid.clone();
    args.caster = Some(WorldObject::from(Entity::new(
        house.obj_id,
        house.data().clone(),
    )));
    rejected.push(args); // equal IDs on distinct handles are not the same house
    let mut args = valid.clone();
    args.target_caster = SkillCastTarget::Unit(f.player.obj_id);
    rejected.push(args);
    let hp = house.data().unit.hp;
    for args in rejected {
        assert!(!super::is_abandoned_house_removal_damage(
            &f.server, effect.id, &args
        ));
        damage(&f.server, &effect, &args, None);
        assert_eq!(house.data().unit.hp, hp);
    }
    let mut wrong_effect = effect.clone();
    wrong_effect.id += 1;
    damage(&f.server, &wrong_effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
    manager::set_untouchable(&f.server, &house, true);
    damage(&f.server, &effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
    manager::set_untouchable(&f.server, &house, false);
    let index = removal_buff(&house).index;
    house
        .data()
        .unit
        .buffs
        .effect_by_index_mut(index)
        .unwrap()
        .skill_id = 11404;
    damage(&f.server, &effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
    house
        .data()
        .unit
        .buffs
        .effect_by_index_mut(index)
        .unwrap()
        .skill_id = 0;
    house
        .data()
        .unit
        .buffs
        .effect_by_index_mut(index)
        .unwrap()
        .caster_obj_id = f.player.obj_id;
    damage(&f.server, &effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
    house
        .data()
        .unit
        .buffs
        .effect_by_index_mut(index)
        .unwrap()
        .caster_obj_id = house.obj_id;
    house
        .data()
        .unit
        .buffs
        .effect_by_index_mut(index)
        .unwrap()
        .state = crate::models::buff::effect_state::FINISHING;
    damage(&f.server, &effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
    crate::systems::skills::buffs::remove_buff(
        &f.server,
        &WorldObject::from(&house),
        manager::BUFF_REMOVAL_DEBUFF,
    );
    damage(&f.server, &effect, &valid, None);
    assert_eq!(house.data().unit.hp, hp);
}

#[tokio::test]
async fn house_removal_death_clears_world_geometry_buffs_attachments_and_identifiers() {
    use crate::systems::ai::collision::{self, world::ObjKind};
    use aaemu_physics::geometry::primitives::BoxPrim;
    use aaemu_physics::geometry::shapes::stream::{save_phys_geometry, PhysGeomHeader};
    use aaemu_physics::geometry::shapes::BoxGeom;
    use aaemu_physics::math::{Quat, Vec3};

    let (mut f, house) = removal_fixture(removal_skills());
    let geometry = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0)));
    let header = PhysGeomHeader {
        inertia: Vec3::splat(1.0),
        q: Quat::IDENTITY,
        origin: Vec3::ZERO,
        volume: 8.0,
        unk52: 1,
        surface_idx: 0,
    };
    let mut blob = Vec::new();
    save_phys_geometry(&header, &geometry, &mut blob);
    f.server
        .systems
        .ai
        .collision
        .test_files
        .set(Arc::new(std::collections::HashMap::from([(
            "game/objects/test/removal-house.cgf".into(),
            collision::cgf::tests::synthetic_cgf(&blob),
        )])))
        .unwrap();
    f.server
        .systems
        .ai
        .collision
        .test_terrain
        .set(Arc::new(collision::terrain::tests::Plane {
            a: 100.0,
            b: 0.0,
            c: 0.0,
            n: 1024,
        }))
        .unwrap_or_else(|_| panic!("terrain"));
    let mut door = Doodad::new(CHAIR_DOODAD);
    door.attach_point = 9;
    door.parent_obj_id = house.obj_id;
    let door = Entity::new(f.server.ids.object.get_next_id().unwrap(), door);
    let world = f.server.world.world(0).unwrap();
    world.attach_child(&WorldObject::from(&house), &WorldObject::from(&door));
    world.spawn(&door, &f.server.view_ctx());
    f.server
        .systems
        .housing
        .manager
        .add_attached(&house, door.clone());
    let z = collision::ground_height(&f.server, 0, 120.0, 100.0, 103.0).unwrap();
    assert!((z - 101.0).abs() < 1e-3);
    let physics = collision::phys_world(&f.server, 0).unwrap();
    assert!(physics
        .lock()
        .objects
        .contains_key(&(ObjKind::House, house.obj_id)));
    let (id, tl) = {
        let h = house.data();
        (h.id, h.unit.tl_id)
    };
    let buff = removal_buff(&house);
    house.data().unit.hp = 10;
    f.client.drain();
    crate::systems::skills::buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(house.data().unit.hp, 0);
    assert!(f.server.systems.housing.manager.by_id(id).is_none());
    assert!(f.server.systems.housing.manager.by_tl(tl).is_none());
    assert!(world.house(house.obj_id).is_none());
    assert!(world.doodad(door.obj_id).is_none());
    assert!(house.data().attached_doodads.is_empty());
    assert!(f
        .server
        .systems
        .housing
        .manager
        .attached(house.obj_id)
        .is_empty());
    assert!(!house
        .data()
        .unit
        .buffs
        .check_buff(manager::BUFF_REMOVAL_DEBUFF));
    assert!(!physics
        .lock()
        .objects
        .contains_key(&(ObjKind::House, house.obj_id)));
    let z = collision::ground_height(&f.server, 0, 120.0, 100.0, 103.0).unwrap();
    assert!((z - 100.0).abs() < 1e-3);
    assert!(f.client.drain_ops().contains(&sc::SCUnitsRemovedPacket));
    assert_eq!(f.server.ids.housing.get_next_id().unwrap(), id);
    assert_eq!(
        f.server.ids.housing_tl.get_next_id().unwrap(),
        u32::from(tl)
    );
    assert_eq!(f.server.ids.object.get_next_id().unwrap(), house.obj_id);
    assert_eq!(f.server.ids.object.get_next_id().unwrap(), door.obj_id);
    // A queued stale tick cannot repeat cleanup after the identifiers are released.
    crate::systems::skills::buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(f.server.systems.housing.manager.removed_ids(), vec![id]);
}

#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB with original 1.2 skills"]
async fn real_compact_house_removal_buff_tick_uses_original_damage_and_death_cleanup() {
    use crate::systems::skills::data::{EffectKind, EffectTemplate};

    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let db =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let skills = crate::systems::skills::Data::load_from(
        &db,
        &crate::data::client_files::ClientFileManager::new(),
    )
    .unwrap();
    let buff = skills.buff(manager::BUFF_REMOVAL_DEBUFF).unwrap();
    assert!(buff.system);
    assert_eq!(buff.duration, 0);
    assert_eq!(buff.tick, 15_000.0);
    assert_eq!(buff.tick_effects.len(), 1);
    let Some(EffectTemplate::Damage(effect)) =
        skills.effect(EffectKind::DamageEffect, REMOVAL_DAMAGE)
    else {
        panic!("removal damage");
    };
    assert_eq!((effect.fixed_min, effect.fixed_max), (10, 10));
    assert!(effect.use_fixed_damage);
    let (f, house) = removal_fixture(skills);
    let buff = removal_buff(&house);
    let hp = house.data().unit.hp;
    crate::systems::skills::buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(house.data().unit.hp, hp - 10);
    house.data().unit.hp = 10;
    crate::systems::skills::buffs::dispel_task(&f.server, house.obj_id, buff.index, buff.task_gen);
    assert_eq!(house.data().unit.hp, 0);
    assert!(f.server.world.object(house.obj_id).is_none());
    assert!(f.server.systems.housing.manager.all().is_empty());
}
