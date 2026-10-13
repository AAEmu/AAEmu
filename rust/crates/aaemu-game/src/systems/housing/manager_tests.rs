//! Port of AAEmu.UnitTests/Game/Core/Managers/HousingManagerTests.cs
//!
//! The C# test checks that the DI constructor touches none of its 14 injected managers. The
//! Rust `HousingManager` has no injected dependencies, so this asserts that construction has no
//! side effects: no house is registered or queued for deletion.

use super::manager::{self, HousingManager};
use crate::data::GameData;
use crate::server::GameServer;
use crate::test_support::test_server_with;
use crate::world::HouseRef;

fn test_housing_data() -> GameData {
    let mut data = GameData::empty();
    data.systems
        .housing
        .add_template(super::data::HousingTemplate {
            id: 300,
            garden_radius: 8.0,
            heavy_tax: true,
            main_model_id: 700,
            taxation: Some(super::data::Taxation {
                id: 1,
                tax: 100_000,
                show: true,
            }),
            build_steps: [
                (
                    0,
                    super::data::HousingBuildStep {
                        model_id: 710,
                        num_actions: 2,
                        ..Default::default()
                    },
                ),
                (
                    1,
                    super::data::HousingBuildStep {
                        model_id: 711,
                        num_actions: 3,
                        ..Default::default()
                    },
                ),
            ]
            .into(),
            binding_doodads: vec![super::data::HousingBindingDoodad {
                attach_point_id: 9,
                doodad_id: 4001,
                ..Default::default()
            }],
            ..Default::default()
        });
    data.systems.npcs.doodad.templates.insert(
        4001,
        crate::systems::npcs::doodad::templates::DoodadTemplate {
            id: 4001,
            ..Default::default()
        },
    );
    data
}

fn test_housing_server() -> GameServer {
    test_server_with(test_housing_data())
}

fn house(server: &GameServer, id: u32) -> HouseRef {
    let world = server.world.world(0).unwrap();
    let h = manager::create(server, 300, 0, &world, 0, 0).unwrap();
    {
        let mut d = h.data();
        d.id = id;
        d.account_id = 1001;
        d.unit.owner_id = 1;
        d.unit.name = "Cottage".into();
        d.place_date = manager::now();
        d.protection_end_date = d.place_date + 14 * 24 * 3600;
        d.unit.transform.set_xyz(200.0, 200.0, 50.0);
    }
    assert!(server.systems.housing.manager.insert(&h));
    h
}

#[test]
fn constructor_does_not_call_deps() {
    let manager = HousingManager::default();
    assert_eq!(manager.count(), 0);
    assert!(manager.all().is_empty());
    assert!(manager.by_id(1).is_none());
    assert!(manager.removed_ids().is_empty());
}

#[tokio::test]
async fn first_house_pays_weekly_tax_and_deposit_and_heavy_tax_starts_at_three() {
    let server = test_housing_server();
    let template = server.game_data.systems.housing.template(300).unwrap();
    let (ok, tax) = manager::calculate_building_tax_info(&server, 1001, template, true).unwrap();
    assert!(ok);
    assert_eq!(
        (
            tax.heavy_house_count,
            tax.one_week_tax_count,
            tax.total_tax_to_pay
        ),
        (1, 100_000, 300_000)
    );
    let _first = house(&server, 1);
    let (_, tax) = manager::calculate_building_tax_info(&server, 1001, template, true).unwrap();
    assert_eq!((tax.heavy_house_count, tax.total_tax_to_pay), (2, 300_000));
    let _second = house(&server, 2);
    let (_, tax) = manager::calculate_building_tax_info(&server, 1001, template, true).unwrap();
    assert_eq!(
        (
            tax.heavy_house_count,
            tax.one_week_tax_count,
            tax.total_tax_to_pay
        ),
        (3, 150_000, 350_000)
    );
}

#[tokio::test]
async fn repeated_completion_keeps_one_binding_and_delete_clears_attachment_references() {
    let server = test_housing_server();
    let house = house(&server, 1);
    manager::set_current_step(&server, &house, 1, false);
    assert_eq!(house.data().base_action, 2);
    manager::set_current_step(&server, &house, -1, false);
    let attached = server.systems.housing.manager.attached(house.obj_id);
    assert_eq!(attached.len(), 1);
    manager::spawn_house(&server, &house);
    manager::set_current_step(&server, &house, -1, false);
    assert_eq!(house.data().attached_doodads, vec![attached[0].obj_id]);
    assert_eq!(house.data().base_action, 0);
    manager::delete_house(&server, &house);
    assert!(server
        .systems
        .housing
        .manager
        .attached(house.obj_id)
        .is_empty());
    assert!(house.data().attached_doodads.is_empty());
    assert!(server.world.object(attached[0].obj_id).is_none());
    assert!(server.world.object(house.obj_id).is_none());
}

#[tokio::test]
async fn repeated_or_stale_house_death_keeps_recycled_house_and_its_ids_registered() {
    let server = test_housing_server();
    let old = house(&server, 1);
    manager::spawn_house(&server, &old);
    manager::remove_dead_house(&server, &old);
    manager::remove_dead_house(&server, &old);
    assert_eq!(server.systems.housing.manager.removed_ids(), vec![1]);
    let replacement = house(&server, 1);
    manager::spawn_house(&server, &replacement);
    assert_eq!(replacement.obj_id, old.obj_id); // both ID allocators may recycle immediately
    manager::remove_dead_house(&server, &old);
    let registered = server.systems.housing.manager.by_id(1).unwrap();
    assert!(std::sync::Arc::ptr_eq(&registered, &replacement));
    assert!(server.world.object(replacement.obj_id).is_some());
    let next_tl = server.ids.housing_tl.get_next_id().unwrap();
    assert_ne!(next_tl, u32::from(replacement.data().unit.tl_id));
    let next_object = server.ids.object.get_next_id().unwrap();
    assert_ne!(next_object, replacement.obj_id);
}

#[tokio::test]
async fn invalid_build_step_keeps_house_state_and_reset_clears_previous_base_actions() {
    let server = test_housing_server();
    let house = house(&server, 1);
    manager::set_current_step(&server, &house, 1, false);
    manager::set_current_step(&server, &house, 20, false);
    {
        let h = house.data();
        assert_eq!((h.current_step, h.unit.model_id), (1, 711));
    }
    manager::set_current_step(&server, &house, 0, false);
    assert_eq!(house.data().base_action, 0);
}

#[tokio::test]
async fn startup_date_repair_is_saved_once_and_land_is_scoped_to_instance() {
    let server = test_housing_server();
    let world = server.world.world(0).unwrap();
    let row = manager::HouseRow {
        id: 1,
        account_id: 1001,
        owner: 1,
        template_id: 300,
        current_step: 0,
        name: "Cottage".into(),
        x: 200.0,
        y: 200.0,
        z: 50.0,
        place_date: manager::now(),
        protected_until: manager::now(),
        ..Default::default()
    };
    let house = manager::load_house_row(&server, &world, &row).unwrap();
    assert!(house.data().is_dirty);
    let repaired = manager::save_row(&house).unwrap();
    assert_eq!(repaired.protected_until, row.place_date + 14 * 24 * 3600);
    assert!(manager::house_at_location_in_world(&server, 0, 200.0, 200.0).is_some());
    assert!(manager::house_at_location_in_world(&server, 99, 200.0, 200.0).is_none());
    let restarted = test_housing_server();
    let restarted_world = restarted.world.world(0).unwrap();
    let reloaded = manager::load_house_row(&restarted, &restarted_world, &repaired).unwrap();
    assert!(!reloaded.data().is_dirty);
    assert_eq!(
        reloaded.data().protection_end_date,
        repaired.protected_until
    );
}

#[tokio::test]
async fn tax_payment_rechecks_live_owner_and_refuses_demolished_or_expired_houses() {
    let server = test_housing_server();
    let house = house(&server, 1);
    let before = house.data().protection_end_date;
    assert!(!manager::pay_weekly_tax_for(&server, &house, Some(2)));
    assert_eq!(house.data().protection_end_date, before);
    assert!(manager::pay_weekly_tax_for(&server, &house, Some(1)));
    assert_eq!(house.data().protection_end_date, before + 7 * 24 * 3600);
    house.data().unit.owner_id = 0;
    assert!(!manager::pay_weekly_tax(&server, &house));
    house.data().unit.owner_id = 1;
    house.data().protection_end_date = manager::now() - 1;
    assert!(!manager::pay_weekly_tax_for(&server, &house, Some(1)));
}

#[tokio::test]
async fn beginner_plot_refuses_renewal_bills_and_does_not_charge_a_forged_payment() {
    use super::hooks::{HouseMail, HousingEconomyHooks};
    use crate::data::world_data::Zone;
    use crate::systems::economy::{
        inventory::ItemTxn,
        mail::{self, BaseMail},
    };
    use crate::systems::npcs::subzone::Area;
    use std::sync::{Arc, Mutex};
    #[derive(Default)]
    struct TaxMails(Mutex<Vec<HouseMail>>);
    impl HousingEconomyHooks for TaxMails {
        fn send_mail(&self, _server: &GameServer, mail: HouseMail) -> bool {
            self.0.lock().unwrap().push(mail);
            true
        }
    }
    let mut data = test_housing_data();
    data.features.set(crate::data::Feature::TaxItem, false);
    data.zones.zones.insert(
        crate::test_support::TEST_ZONE,
        Zone {
            id: 42,
            zone_key: crate::test_support::TEST_ZONE,
            ..Default::default()
        },
    );
    data.systems
        .npcs
        .subzone
        .housing_zones
        .entry(0)
        .or_default()
        .insert(
            42,
            vec![Area {
                id: 5,
                points: vec![
                    [180.0, 180.0, 0.0],
                    [220.0, 180.0, 0.0],
                    [220.0, 220.0, 0.0],
                    [180.0, 220.0, 0.0],
                ],
                ..Default::default()
            }],
        );
    data.systems.housing.areas.insert(5, 13);
    data.systems.housing.groups.insert(
        13,
        super::data::HousingGroup {
            id: 13,
            can_extend: false,
            ..Default::default()
        },
    );
    let server = test_server_with(data);
    let house = house(&server, 77);
    house.data().unit.transform.zone_id = crate::test_support::TEST_ZONE;
    house.data().protection_end_date = manager::now() + 3 * 24 * 3600;
    server.names.add_character(1, "Owner", 1001);
    let mails = Arc::new(TaxMails::default());
    server.systems.housing.hooks.install(mails.clone());
    manager::update_tax_info(&server, &house);
    assert!(mails.0.lock().unwrap().is_empty());
    let before = house.data().protection_end_date;
    assert!(!manager::pay_weekly_tax_for(&server, &house, Some(1)));
    assert_eq!(house.data().protection_end_date, before);
    let client = crate::test_support::TestClient::new(&server, 1, 1001);
    let player = crate::test_support::spawn_player(
        &server,
        &client,
        crate::test_support::test_character(1, "Owner", 200.0, 200.0),
        0x100,
    );
    player.data().money = 1000;
    crate::systems::economy::bridges::install(&server);
    let mut bill = BaseMail {
        receiver_name: "Owner".into(),
        mail_type: mail::mail_type::BILLING,
        ..Default::default()
    };
    bill.header.receiver_id = 1;
    bill.header.sender_name = ".houseTax".into();
    bill.header.status = mail::mail_status::UNPAID;
    bill.header.extra = 77;
    bill.body.billing_amount = 500;
    bill.body.recv_date = mail::now();
    let mut txn = ItemTxn::begin(&server);
    let id = mail::send_mail(&mut txn, bill).unwrap();
    assert!(!mail::pay_charge_money(&mut txn, &player, id, false));
    assert_eq!(player.data().money, 1000);
    assert_eq!(house.data().protection_end_date, before);
    drop(txn);
    // The same ordinary house outside the beginner area still supports renewal.
    house.data().unit.transform.set_xyz(300.0, 300.0, 50.0);
    assert!(manager::pay_weekly_tax_for(&server, &house, Some(1)));
}

#[tokio::test]
async fn crop_placement_on_rotated_house_preserves_world_position_and_saves_local_transform() {
    let server = test_housing_server();
    let house = house(&server, 1);
    house.data().unit.transform.rot_z = std::f32::consts::FRAC_PI_2;
    house.data().unit.scale = 2.0;
    manager::spawn_house(&server, &house);
    let client = crate::test_support::TestClient::new(&server, 1, 1001);
    let player = crate::test_support::spawn_player(
        &server,
        &client,
        crate::test_support::test_character(1, "Owner", 201.0, 201.0),
        0x100,
    );
    let doodad = crate::systems::npcs::doodad::manager::create_player_doodad(
        &server,
        &player,
        4001,
        (202.0, 201.0, 51.0),
        0.3,
        1.0,
        0,
        0,
        0,
        0,
        false,
    )
    .unwrap();
    let world = server.world.world(0).unwrap();
    let position = world.world_pos_rot(&crate::world::WorldObject::from(&doodad));
    assert!((position.x - 202.0).abs() < 1e-4);
    assert!((position.y - 201.0).abs() < 1e-4);
    assert!((position.z - 51.0).abs() < 1e-4);
    assert!((position.rot_z - 0.3).abs() < 1e-4);
    let row = crate::systems::npcs::doodad::db::snapshot(&server, &doodad).unwrap();
    assert_eq!(row.house_id, 1);
    assert!((row.x - 0.5).abs() < 1e-4);
    assert!((row.y + 1.0).abs() < 1e-4);
    assert!((row.z - 0.5).abs() < 1e-4);
}

#[tokio::test]
async fn backpack_drop_on_rotated_house_keeps_world_position_after_reload() {
    use crate::data::items::{backpack_kind, ItemTemplate, TemplateKind};
    use crate::models::container::{equip_slot, slot_type};
    use crate::models::item::Item;
    use crate::systems::economy::{bridges, inventory::Inv, item_effects};
    use crate::world::WorldObject;

    let mut data = test_housing_data();
    data.items.templates.insert(
        17683,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(
                17683,
                TemplateKind::Backpack {
                    backpack_type: backpack_kind::TRADE_PACK,
                },
            )
        },
    );
    let server = test_server_with(data);
    let house = house(&server, 1);
    house.data().unit.transform.rot_z = std::f32::consts::FRAC_PI_2;
    house.data().unit.scale = 2.0;
    manager::spawn_house(&server, &house);
    let client = crate::test_support::TestClient::new(&server, 1, 1001);
    let mut character = crate::test_support::test_character(1, "Owner", 202.0, 200.0);
    character.unit.transform.z = 51.0;
    let player = crate::test_support::spawn_player(&server, &client, character, 0x100);
    let item_id = server.ids.item.get_next_id().unwrap() as u64;
    let item = Item::create(
        item_id,
        server.game_data.items.template(17683).unwrap(),
        1,
        0,
        1,
        &server.game_data.items,
        0,
    );
    let equipment = Inv::of(&player).equipment();
    equipment
        .lock()
        .unwrap()
        .add_new_item(item, equip_slot::BACKPACK, &server.game_data.items)
        .unwrap();
    server.items.add_container(equipment);
    bridges::install(&server);

    // Exercise the real equipped-item effect and its production doodad hook.
    item_effects::put_down_backpack(
        &server,
        &player,
        4001,
        Some(item_effects::SkillItem {
            item_id,
            item_template_id: 17683,
        }),
    );
    assert!(Inv::of(&player).equipped(equip_slot::BACKPACK).is_none());
    assert!(Inv::of(&player)
        .get(slot_type::SYSTEM)
        .unwrap()
        .lock()
        .unwrap()
        .get_item_by_id(item_id)
        .is_some());
    let world = server.world.world(0).unwrap();
    let doodad = world
        .all_doodads()
        .into_iter()
        .find(|d| d.data().item_id == item_id)
        .unwrap();
    let position = world.world_pos_rot(&WorldObject::from(&doodad));
    assert!((position.x - 202.0).abs() < 1e-4);
    assert!((position.y - 201.0).abs() < 1e-4);
    assert!((position.z - 51.0).abs() < 1e-4);
    assert!(position.rot_z.abs() < 1e-4); // put-down packs face north in world space
    let row = crate::systems::npcs::doodad::db::snapshot(&server, &doodad).unwrap();
    assert_eq!(
        (row.house_id, row.item_id, row.item_template_id),
        (1, item_id, 17683)
    );
    assert!((row.x - 0.5).abs() < 1e-4);
    assert!((row.y + 1.0).abs() < 1e-4);
    assert!((row.z - 0.5).abs() < 1e-4);

    // Restart loading restores the saved local transform and reattaches to the house.
    let loaded = crate::systems::npcs::doodad::manager::create(
        &server,
        &world,
        0,
        row.template_id,
        None,
        true,
    )
    .unwrap();
    loaded
        .data()
        .transform
        .set_position(row.x, row.y, row.z, row.roll, row.pitch, row.yaw);
    assert!(super::attach_loaded_doodad(
        &server,
        &world,
        row.house_id,
        &loaded
    ));
    let restored = world.world_pos_rot(&WorldObject::from(&loaded));
    assert!((restored.x - 202.0).abs() < 1e-4);
    assert!((restored.y - 201.0).abs() < 1e-4);
    assert!((restored.z - 51.0).abs() < 1e-4);
    assert!(restored.rot_z.abs() < 1e-4);
}

#[tokio::test]
async fn shared_furniture_model_resolves_proficiency_bonus_by_actual_design_once() {
    let mut data = test_housing_data();
    for (design, item, group, bonus) in [(40, 8000, 3, 5), (41, 8001, 7, 9)] {
        data.systems
            .housing
            .add_decoration(super::data::HousingDecoration {
                id: design,
                doodad_id: 4001,
                actability_group_id: group,
                actability_up: bonus,
                ..Default::default()
            })
            .unwrap();
        data.systems
            .housing
            .add_item_housing_decoration(super::data::ItemHousingDecoration {
                id: design,
                item_id: item,
                design_id: design,
                restore: true,
            });
    }
    let server = test_server_with(data);
    let house = house(&server, 1);
    manager::spawn_house(&server, &house);
    let world = server.world.world(0).unwrap();
    for (id, item_template) in [(6000, 8000), (6001, 8001), (6002, 8001)] {
        let mut d = crate::models::doodad::Doodad::new(4001);
        d.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
        d.owner_db_id = 1;
        d.item_template_id = item_template;
        d.attach_point = crate::models::unit::attach_point::NONE;
        d.transform.instance_id = 0;
        d.transform.set_xyz(200.0, 200.0, 50.0);
        let doodad = crate::world::Entity::new(id, d);
        world.spawn(&doodad, &server.view_ctx());
    }
    assert_eq!(manager::act_ability_bonus_from_house(&server, 3, &house), 5);
    assert_eq!(manager::act_ability_bonus_from_house(&server, 7, &house), 9);
    assert_eq!(
        super::act_ability_bonus_at_in_world(&server, 99, 7, 200.0, 200.0),
        0
    );
}

#[tokio::test]
async fn failed_house_creation_releases_allocated_tl_id() {
    use crate::managers::id::{IdManager, IdManagerSpec, IdManagers};
    let mut server = test_housing_server();
    let capacity = crate::managers::id::next_prime(100_000) as u32;
    let object = IdManager::new(IdManagerSpec {
        name: "ExhaustedTestObjects",
        first_id: 1,
        last_id: capacity + 1,
        obj_tables: &[],
        distinct: false,
    });
    object.initialize_with(&(1..=capacity).collect::<Vec<_>>());
    server.ids = std::sync::Arc::new(IdManagers {
        object,
        ..Default::default()
    });
    let first_tl = server.ids.housing_tl.get_next_id().unwrap();
    server.ids.housing_tl.release_id(first_tl);
    let world = server.world.world(0).unwrap();
    assert!(manager::create(&server, 300, 0, &world, 0, 0).is_none());
    assert_eq!(server.ids.housing_tl.get_next_id().unwrap(), first_tl);
}

#[tokio::test]
#[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows housings with a connection-local temporary table"]
async fn private_mysql_housing_rollback_retry_and_concurrent_mutation_preserve_state() {
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;
    let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
        return;
    };
    let options = MySqlConnectOptions::from_str(&url).expect("valid private MySQL URL");
    assert_eq!(
        options.get_port(),
        3317,
        "Only private MySQL3317 is allowed"
    );
    let pool = MySqlPoolOptions::new()
        .max_connections(1)
        .connect_with(options)
        .await
        .unwrap();
    let (_, ddl): (String, String) = sqlx::query_as("SHOW CREATE TABLE housings")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert!(ddl.starts_with("CREATE TABLE"));
    sqlx::query(&ddl.replacen("CREATE TABLE", "CREATE TEMPORARY TABLE", 1))
        .execute(&pool)
        .await
        .unwrap();
    let mut server = test_housing_server();
    server.db = pool.clone();
    let first = house(&server, 61);
    let _second = house(&server, 62);
    let mut tx = pool.begin().await.unwrap();
    assert_eq!(manager::save(&server, &mut tx).await.unwrap(), (2, 0));
    assert!(first.data().is_dirty); // writing inside an uncommitted tx is insufficient
    tx.commit().await.unwrap();
    manager::commit_save(&server);
    assert!(!first.data().is_dirty);

    first.data().unit.name = "Renamed".into();
    first.data().is_dirty = true;
    server.systems.housing.manager.mark_removed(62);
    server.systems.housing.manager.mark_removed(62); // demolition + death queues only once
    let mut tx = pool.begin().await.unwrap();
    assert_eq!(manager::save(&server, &mut tx).await.unwrap(), (1, 1));
    tx.rollback().await.unwrap();
    assert!(first.data().is_dirty);
    assert_eq!(server.systems.housing.manager.removed_ids(), vec![62]);
    let rows = super::db::load_rows(&pool).await.unwrap();
    assert_eq!(rows.len(), 2);
    assert!(rows.iter().all(|r| r.name == "Cottage"));

    let mut tx = pool.begin().await.unwrap();
    assert_eq!(manager::save(&server, &mut tx).await.unwrap(), (1, 1));
    first.data().permission = crate::models::house::housing_permission::PUBLIC;
    tx.commit().await.unwrap();
    manager::commit_save(&server);
    assert!(first.data().is_dirty); // mutation after snapshot must survive acknowledgement
    assert!(server.systems.housing.manager.removed_ids().is_empty());
    let rows = super::db::load_rows(&pool).await.unwrap();
    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0].name, "Renamed");
    assert_eq!(
        rows[0].permission,
        crate::models::house::housing_permission::PRIVATE
    );
    let mut tx = pool.begin().await.unwrap();
    assert_eq!(manager::save(&server, &mut tx).await.unwrap(), (1, 0));
    let expected = manager::save_row(&first).unwrap();
    tx.commit().await.unwrap();
    manager::commit_save(&server);
    assert!(!first.data().is_dirty);
    let rows = super::db::load_rows(&pool).await.unwrap();
    assert_eq!(rows[0], expected);
    sqlx::query("DROP TEMPORARY TABLE housings")
        .execute(&pool)
        .await
        .unwrap();
    pool.close().await;
}

#[tokio::test]
#[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses connection-local temporary housing/mail/item/character tables"]
async fn private_mysql_housing_related_world_save_aborts_character_failure_and_retries() {
    use crate::models::container::{slot_type, ContainerKind, ItemContainer};
    use crate::models::item::Item;
    use crate::systems::economy::{
        inventory::ItemTxn,
        mail::{self, BaseMail},
    };
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;
    let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
        return;
    };
    let options = MySqlConnectOptions::from_str(&url).unwrap();
    assert_eq!(
        options.get_port(),
        3317,
        "Only private MySQL3317 is allowed"
    );
    let pool = MySqlPoolOptions::new()
        .max_connections(1)
        .connect_with(options)
        .await
        .unwrap();
    for table in ["housings", "mails", "items", "item_containers"] {
        let (_, ddl): (String, String) = sqlx::query_as(&format!("SHOW CREATE TABLE `{table}`"))
            .fetch_one(&pool)
            .await
            .unwrap();
        assert!(ddl.starts_with("CREATE TABLE"));
        sqlx::query(&ddl.replacen("CREATE TABLE", "CREATE TEMPORARY TABLE", 1))
            .execute(&pool)
            .await
            .unwrap();
    }
    // Deliberately invalid character schema makes save_character fail at its first write.
    // It shadows the application table and prevents any test from touching player records.
    sqlx::query("CREATE TEMPORARY TABLE characters (id INT PRIMARY KEY)")
        .execute(&pool)
        .await
        .unwrap();
    let mut server = test_housing_server();
    server.db = pool.clone();
    let house = house(&server, 61);
    server.names.add_character(1, "Owner", 1001);
    let container = ItemContainer::new(ContainerKind::Item, 1, slot_type::INVENTORY, 91).into_ref();
    {
        let mut c = container.lock().unwrap();
        c.set_container_size(10);
        for (id, slot) in [(501, 0), (502, 1)] {
            let mut item = Item::with_template(
                1,
                id,
                &crate::data::items::ItemTemplate::plain(9000, 100),
                4,
            );
            item.owner_id = 1;
            item.slot_type = slot_type::INVENTORY;
            item.slot = slot;
            item.dirty = true;
            c.items.push(item);
        }
    }
    server.items.add_container(container.clone());
    let mut bill = BaseMail {
        receiver_name: "Owner".into(),
        title: "Sale receipt".into(),
        mail_type: mail::mail_type::HOUSING_SALE,
        ..Default::default()
    };
    bill.header.receiver_id = 1;
    bill.body.recv_date = mail::now();
    let (first_mail, second_mail) = {
        let mut txn = ItemTxn::begin(&server);
        (
            mail::send_mail(&mut txn, bill.clone()).unwrap(),
            mail::send_mail(&mut txn, bill).unwrap(),
        )
    };
    let mut tx = pool.begin().await.unwrap();
    manager::save(&server, &mut tx).await.unwrap();
    server.systems.economy.mail.save(&mut tx).await.unwrap();
    server.items.save(&mut tx).await.unwrap();
    tx.commit().await.unwrap();
    manager::commit_save(&server);
    server.systems.economy.mail.commit_save();
    server.items.commit_save();
    assert!(!container.lock().unwrap().dirty);
    assert!(!server.items.item_by_id(501).unwrap().dirty);

    house.data().unit.name = "Paid and renamed".into();
    house.data().is_dirty = true;
    {
        let mut c = container.lock().unwrap();
        c.set_container_size(20);
        c.items.retain(|i| i.id != 502);
        c.items[0].count = 3;
        c.items[0].dirty = true;
    }
    server.items.mark_for_db_deletion(502);
    server.systems.economy.mail.with_mail_mut(first_mail, |m| {
        m.body.text = "Updated sale".into();
        m.is_dirty = true;
    });
    assert!(server
        .systems
        .economy
        .mail
        .delete_mail(&server, second_mail));
    let client = crate::test_support::TestClient::new(&server, 1, 1001);
    let player = crate::test_support::spawn_player(
        &server,
        &client,
        crate::test_support::test_character(1, "Owner", 200.0, 200.0),
        0x100,
    );
    player.data().skills.removed.push(777);
    assert!(!server.save.do_save(&server).await);
    assert!(house.data().is_dirty);
    assert!(container.lock().unwrap().dirty);
    assert!(server.items.item_by_id(501).unwrap().dirty);
    assert_eq!(server.items.removed_items(), vec![502]);
    assert!(
        server
            .systems
            .economy
            .mail
            .get_mail_by_id(first_mail)
            .unwrap()
            .is_dirty
    );
    assert!(server
        .systems
        .economy
        .mail
        .state()
        .deleted
        .contains(&second_mail));
    assert_eq!(player.data().skills.removed, vec![777]);
    let name: String = sqlx::query_scalar("SELECT name FROM housings WHERE id=61")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert_eq!(name, "Cottage");
    let counts: Vec<(u64, i32)> =
        sqlx::query_as("SELECT CAST(id AS UNSIGNED),`count` FROM items ORDER BY id")
            .fetch_all(&pool)
            .await
            .unwrap();
    assert_eq!(counts, vec![(501, 4), (502, 4)]);
    let mails: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM mails")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert_eq!(mails, 2);

    // Remove the deliberately failing character and retry the same housing/mail/item state.
    server.world.world(0).unwrap().delete(&player);
    assert!(server.world.try_remove_character(player.obj_id));
    assert!(server.save.do_save(&server).await);
    assert!(!house.data().is_dirty);
    assert!(!container.lock().unwrap().dirty);
    assert!(!server.items.item_by_id(501).unwrap().dirty);
    assert!(server.items.removed_items().is_empty());
    assert!(
        !server
            .systems
            .economy
            .mail
            .get_mail_by_id(first_mail)
            .unwrap()
            .is_dirty
    );
    assert!(server.systems.economy.mail.state().deleted.is_empty());
    let counts: Vec<(u64, i32)> =
        sqlx::query_as("SELECT CAST(id AS UNSIGNED),`count` FROM items ORDER BY id")
            .fetch_all(&pool)
            .await
            .unwrap();
    assert_eq!(counts, vec![(501, 3)]);
    let text: String = sqlx::query_scalar("SELECT `text` FROM mails")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert_eq!(text, "Updated sale");

    // Changes after a SQL snapshot must stay dirty after that snapshot commits.
    container.lock().unwrap().set_container_size(30);
    server.items.update_item(501, |i| {
        i.count = 2;
        i.dirty = true;
    });
    server.systems.economy.mail.with_mail_mut(first_mail, |m| {
        m.body.text = "Snapshot".into();
        m.is_dirty = true;
    });
    let mut tx = pool.begin().await.unwrap();
    server.items.save(&mut tx).await.unwrap();
    server.systems.economy.mail.save(&mut tx).await.unwrap();
    container.lock().unwrap().set_container_size(40);
    server.items.update_item(501, |i| i.count = 1);
    server
        .systems
        .economy
        .mail
        .with_mail_mut(first_mail, |m| m.body.text = "Newer".into());
    tx.commit().await.unwrap();
    server.items.commit_save();
    server.systems.economy.mail.commit_save();
    assert!(container.lock().unwrap().dirty);
    assert!(server.items.item_by_id(501).unwrap().dirty);
    assert!(
        server
            .systems
            .economy
            .mail
            .get_mail_by_id(first_mail)
            .unwrap()
            .is_dirty
    );
    assert!(server.save.do_save(&server).await);
    let count: i32 = sqlx::query_scalar("SELECT `count` FROM items WHERE id=501")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert_eq!(count, 1);
    let text: String = sqlx::query_scalar("SELECT `text` FROM mails")
        .fetch_one(&pool)
        .await
        .unwrap();
    assert_eq!(text, "Newer");
    // A consumed item may never have reached MySQL. A zero-row deletion still commits its
    // acknowledged tombstone instead of retrying it forever on an otherwise idle world.
    server.items.mark_for_db_deletion(999_999);
    assert!(server.save.do_save(&server).await);
    assert!(server.items.removed_items().is_empty());
    for table in [
        "characters",
        "item_containers",
        "items",
        "mails",
        "housings",
    ] {
        sqlx::query(&format!("DROP TEMPORARY TABLE `{table}`"))
            .execute(&pool)
            .await
            .unwrap();
    }
    pool.close().await;
}
