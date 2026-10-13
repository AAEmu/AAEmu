use std::collections::BTreeMap;
use std::sync::Arc;

use super::fort::*;
use super::state::SiegeSide;
use crate::data::items::ItemTemplate;
use crate::data::world_data::{SystemFaction, Zone};
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::models::house::{housing_permission, House};
use crate::models::item::Item;
use crate::server::GameServer;
use crate::systems::economy::bridges::EconomyForHousing;
use crate::systems::economy::error_message as err;
use crate::systems::economy::inventory::Inv;
use crate::systems::housing::data::{
    GuardTowerSetting, GuardTowerStep, HousingBuildStep, HousingItemHousings, HousingTemplate,
    Taxation,
};
use crate::systems::housing::{manager, ops, packets::CSCreateHousePacket, placement};
use crate::systems::skills::data::BuffTemplate;
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{Entity, HouseRef, PlayerRef};

struct Fixture {
    server: GameServer,
    builder: PlayerRef,
    attacker: PlayerRef,
    tower: HouseRef,
}

fn fixture(max_walls: u32) -> Fixture {
    let mut data = GameData::empty();
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
    let setting = GuardTowerSetting {
        id: 6,
        radius_dominion: 100,
        max_gates: 1,
        max_walls,
        initial_buff_id: 4771,
        steps: BTreeMap::from([
            (
                1,
                GuardTowerStep {
                    step: 1,
                    num_walls: 2,
                    buff_id: 4772,
                    ..Default::default()
                },
            ),
            (
                2,
                GuardTowerStep {
                    step: 2,
                    num_walls: 2,
                    num_gates: 1,
                    buff_id: 4773,
                    ..Default::default()
                },
            ),
        ]),
        ..Default::default()
    };
    data.systems
        .housing
        .castles
        .guard_tower_settings
        .insert(6, setting);
    for (id, category, item) in [
        (121, 3, 15797),
        (120, 2, 15796),
        (26, 4, 16024),
        (135, 14, 16028),
    ] {
        data.systems.housing.add_template(HousingTemplate {
            id,
            category_id: category,
            hp: 1000,
            garden_radius: 1.0,
            heavy_tax: true,
            taxation: Some(Taxation {
                id: 1,
                tax: 0,
                show: false,
            }),
            // Even a mistaken sale flag cannot allow a castle sale.
            is_sellable: true,
            build_steps: BTreeMap::from([(
                0,
                HousingBuildStep {
                    step: 0,
                    skill_id: 14575,
                    num_actions: 1,
                    ..Default::default()
                },
            )]),
            ..Default::default()
        });
        data.systems.housing.add_item_housing(HousingItemHousings {
            item_id: item,
            design_id: id,
            ..Default::default()
        });
        let mut template = ItemTemplate::plain(item, 10);
        template.use_skill_id = FORTIFICATION_BUILD_SKILL;
        data.items.templates.insert(item, template);
    }
    data.systems.housing.add_template(HousingTemplate {
        id: 300,
        category_id: 1,
        hp: 1000,
        garden_radius: 1.0,
        heavy_tax: true,
        taxation: Some(Taxation {
            id: 2,
            tax: 100_000,
            show: true,
        }),
        ..Default::default()
    });
    for id in [
        4771,
        4772,
        4773,
        manager::BUFF_UNTOUCHABLE,
        manager::BUFF_REMOVAL_DEBUFF,
    ] {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            max_stack: 1,
            system: true,
            ..Default::default()
        });
    }
    let server = test_server_with(data);
    for (id, name, account) in [
        (1, "Lord", 1001),
        (2, "Builder", 1002),
        (3, "CapturingLord", 1003),
    ] {
        server.names.add_character(id, name, account);
    }
    server
        .systems
        .housing
        .hooks
        .install(Arc::new(EconomyForHousing));
    let builder_client = TestClient::new(&server, 2, 1002);
    let mut character = test_character(2, "Builder", 100.0, 100.0);
    character.unit.expedition_id = 10;
    character.money = 400_000;
    let builder = spawn_player(
        &server,
        &builder_client,
        character,
        server.ids.object.get_next_id().unwrap(),
    );
    let attacker_client = TestClient::new(&server, 3, 1003);
    let mut character = test_character(3, "CapturingLord", 100.0, 100.0);
    character.unit.expedition_id = 20;
    let attacker = spawn_player(
        &server,
        &attacker_client,
        character,
        server.ids.object.get_next_id().unwrap(),
    );
    for (id, leader, member) in [(10, 1, &builder), (20, 3, &attacker)] {
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id,
                owner_id: leader,
                ..Default::default()
            },
            members: vec![member_from_character(&server, id, member, false)],
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
    }
    let mut house = House::new(500, 139);
    house.current_step = -1;
    house.unit.hp = 1000;
    house.unit.max_hp = 1000;
    house.unit.transform.zone_id = TEST_ZONE;
    house.unit.transform.set_xyz(100.0, 100.0, 100.0);
    let tower = Entity::new(server.ids.object.get_next_id().unwrap(), house);
    server.systems.housing.manager.insert(&tower);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&tower, &server.view_ctx());
    let (_, identity) = super::runtime::tower_identity(&server, &tower).unwrap();
    server
        .systems
        .housing
        .dominions
        .declare_unowned_with(25, identity, 10, manager::now() - 1000, || true)
        .unwrap();
    Fixture {
        server,
        builder,
        attacker,
        tower,
    }
}

fn give(f: &Fixture, template: u32, slot: i32) -> u64 {
    let id = u64::from(f.server.ids.item.get_next_id().unwrap());
    let item = Item::create(
        id,
        f.server.game_data.items.template(template).unwrap(),
        1,
        0,
        1,
        &f.server.game_data.items,
        0,
    );
    let container = Inv::of(&f.builder).get(slot_type::INVENTORY).unwrap();
    container
        .lock()
        .unwrap()
        .add_new_item(item, slot, &f.server.game_data.items)
        .unwrap();
    f.server.items.add_container(container);
    id
}

fn place(f: &Fixture, template: u32, item: u32, x: f32, slot: i32) -> Option<HouseRef> {
    let id = give(f, item, slot);
    let p = CSCreateHousePacket {
        design_id: template,
        item_id: id,
        x,
        y: 100.0,
        z: 100.0,
        z_rot: 0.0,
        money_amount: 0,
        ht: 0,
        auto_use_aa_point: false,
    };
    ops::build(&f.server, &f.builder, 1002, &p);
    f.server
        .systems
        .housing
        .manager
        .all()
        .into_iter()
        .find(|house| {
            house.data().unit.transform.x == x && house.data().unit.template_id == template
        })
}

#[tokio::test]
async fn guild_member_places_for_current_lord_without_taxes_and_foreign_guild_cannot_build() {
    let f = fixture(50);
    let wall = place(&f, 121, 15797, 120.0, 0).unwrap();
    {
        let h = wall.data();
        assert_eq!(
            (
                h.unit.owner_id,
                h.account_id,
                h.co_owner_id,
                h.unit.expedition_id
            ),
            (1, 1001, 1, 10)
        );
        assert_eq!(
            (h.tax, h.protection_end_date, h.permission),
            (0, FORT_PROTECTION_END, housing_permission::GUILD)
        );
    }
    assert_eq!(f.builder.data().money, 400_000);
    assert_eq!(
        Inv::of(&f.builder).items_count_in(slot_type::INVENTORY, 15797, -1),
        0
    );
    assert!(f.server.systems.economy.mail.state().all.is_empty());
    assert!(!ops::craft_building(&f.server, &f.attacker, &wall, 14575));
    assert_eq!(wall.data().current_step, 0);
    assert!(ops::craft_building(&f.server, &f.builder, &wall, 14575));
    assert_eq!(wall.data().current_step, -1);
    let template = f.server.game_data.systems.housing.template(121).unwrap();
    assert_eq!(
        placement::validate_house(&f.server, &f.attacker, template, 130.0, 100.0, 100.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    assert!(!ops::set_for_sale(
        &f.server,
        &wall,
        100_000,
        0,
        Some(&f.builder)
    ));
    wall.data().sell_price = 100_000;
    let tl = wall.data().unit.tl_id;
    assert!(!ops::buy_house(&f.server, tl, 100_000, &f.attacker));
    wall.data().protection_end_date = 0;
    manager::check_housing_taxes(&f.server);
    assert_eq!(wall.data().unit.owner_id, 1);
    assert!(!manager::pay_weekly_tax(&f.server, &wall));
    let (_, tax) = manager::calculate_building_tax_info(
        &f.server,
        1001,
        f.server.game_data.systems.housing.template(300).unwrap(),
        true,
    )
    .unwrap();
    assert_eq!((tax.heavy_house_count, tax.total_tax_to_pay), (1, 300_000));
}

#[tokio::test]
async fn native_radius_and_pending_fort_caps_reject_before_consuming_design() {
    let f = fixture(1);
    let template = f.server.game_data.systems.housing.template(121).unwrap();
    assert!(state_at(&f.server, 0, 200.0, 100.0).is_none());
    assert!(state_at(&f.server, 1, 120.0, 100.0).is_none());
    let wall = place(&f, 121, 15797, 120.0, 0).unwrap();
    assert_eq!(wall.data().current_step, 0);
    assert!(place(&f, 120, 15796, 130.0, 1).is_none());
    assert_eq!(
        Inv::of(&f.builder).items_count_in(slot_type::INVENTORY, 15796, -1),
        1
    );
    assert_eq!(
        placement_owner(&f.server, &f.builder, template, 130.0, 100.0),
        Err(err::EXCEED_MAX_DOMINION_GATE_OR_WALL)
    );
    // Native counters continue reserving the slot until the object leaves the manager.
    wall.data().unit.owner_id = 0;
    wall.data().unit.hp = 0;
    assert_eq!(
        placement_owner(&f.server, &f.builder, template, 130.0, 100.0),
        Err(err::EXCEED_MAX_DOMINION_GATE_OR_WALL)
    );
    assert!(place(&f, 26, 16024, 140.0, 2).is_some());
}

#[tokio::test]
async fn completed_alive_defences_select_one_authored_refuge_buff_and_death_removes_it() {
    let f = fixture(50);
    let wall = place(&f, 121, 15797, 120.0, 0).unwrap();
    let tower = place(&f, 120, 15796, 130.0, 1).unwrap();
    let state = f.server.systems.housing.dominions.get(25).unwrap();
    refresh_protection_buff(&f.server, &state);
    assert!(f.tower.data().unit.buffs.check_buff(4771));
    assert!(!f.tower.data().unit.buffs.check_buff(4772));
    for house in [&wall, &tower] {
        assert!(ops::craft_building(&f.server, &f.builder, house, 14575));
    }
    assert!(!f.tower.data().unit.buffs.check_buff(4771));
    assert!(f.tower.data().unit.buffs.check_buff(4772));
    let gate = place(&f, 135, 16028, 140.0, 2).unwrap();
    assert!(ops::craft_building(&f.server, &f.builder, &gate, 14575));
    assert!(!f.tower.data().unit.buffs.check_buff(4772));
    assert!(f.tower.data().unit.buffs.check_buff(4773));
    wall.data().unit.hp = 0;
    manager::remove_dead_house(&f.server, &wall);
    assert!(f.tower.data().unit.buffs.check_buff(4771));
    assert!(!f.tower.data().unit.buffs.check_buff(4773));
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 1);
    manager::remove_dead_house(&f.server, &wall);
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 1);
}

#[tokio::test]
async fn capture_transfers_only_forts_and_returns_design_to_new_guild_leader() {
    let f = fixture(50);
    let wall = place(&f, 121, 15797, 120.0, 0).unwrap();
    let mut ordinary = House::new(600, 300);
    ordinary.unit.tl_id = 650;
    ordinary.unit.owner_id = 2;
    ordinary.account_id = 1002;
    ordinary.unit.transform.zone_id = TEST_ZONE;
    ordinary.unit.transform.set_xyz(150.0, 100.0, 100.0);
    let ordinary = Entity::new(f.server.ids.object.get_next_id().unwrap(), ordinary);
    assert!(f.server.systems.housing.manager.insert(&ordinary));
    f.server
        .world
        .world(0)
        .unwrap()
        .spawn(&ordinary, &f.server.view_ctx());
    let manager = &f.server.systems.housing.dominions;
    let identity = manager.get(25).unwrap().tower;
    let now = manager::now();
    manager
        .schedule_siege(25, 20, now - 400, now + 1000, now - 500)
        .unwrap();
    manager
        .register_participant(25, 3, 20, SiegeSide::Attacker, now - 400)
        .unwrap();
    manager
        .mark_tower_destroyed(25, identity, now - 400)
        .unwrap();
    let handle = manager
        .begin_engraving(25, identity, 3, 20, (now - 350) * 1000)
        .unwrap();
    let state = manager.complete_engraving(&handle, 20, now * 1000).unwrap();
    transfer_captured(&f.server, &state);
    {
        let h = wall.data();
        assert_eq!(
            (h.unit.owner_id, h.account_id, h.unit.expedition_id),
            (3, 1003, 20)
        );
    }
    assert!(wall.data().is_dirty);
    assert_eq!(ordinary.data().unit.owner_id, 2);
    assert!(!allowed_to_interact(&f.server, &wall, &f.builder));
    assert!(allowed_to_interact(&f.server, &wall, &f.attacker));
    ops::demolish(&f.server, Some(&f.attacker), &wall, false, false);
    let mail_state = f.server.systems.economy.mail.state();
    let mail = mail_state.all.iter().next().unwrap().1;
    assert_eq!(mail.header.receiver_id, 3);
    assert_eq!(mail.body.copper_coins, 0);
    let item = f.server.items.item_by_id(mail.body.attachments[0]).unwrap();
    assert_eq!((item.template_id, item.owner_id), (15797, 3));
}

#[tokio::test]
async fn persisted_dead_fort_waits_for_returns_without_releasing_ids_and_resumes_once() {
    use crate::systems::housing::hooks::HousingEconomyHooks;
    struct RejectReturns;
    impl HousingEconomyHooks for RejectReturns {}
    let original = fixture(50);
    let wall = place(&original, 121, 15797, 120.0, 0).unwrap();
    wall.data().unit.hp = 0;
    let row = manager::save_row(&wall).unwrap();
    assert_eq!(row.hp, Some(0));
    let restarted = fixture(50);
    let world = restarted.server.world.world(0).unwrap();
    let restored = manager::load_house_row(&restarted.server, &world, &row).unwrap();
    manager::spawn_house(&restarted.server, &restored);
    assert_eq!(restored.data().unit.hp, 0);
    let next_object = restarted.server.ids.object.get_next_id().unwrap();
    restarted.server.ids.object.release_id(next_object);
    let next_tl = restarted.server.ids.housing_tl.get_next_id().unwrap();
    restarted.server.ids.housing_tl.release_id(next_tl);
    restarted
        .server
        .systems
        .housing
        .hooks
        .install(Arc::new(RejectReturns));
    manager::resume_dead_houses(&restarted.server);
    assert!(restarted.server.systems.housing.manager.contains(row.id));
    assert_eq!(restored.data().unit.hp, 0);
    assert!(world.house(restored.obj_id).is_some());
    assert!(restarted
        .server
        .systems
        .housing
        .manager
        .removed_ids()
        .is_empty());
    assert!(restarted.server.systems.economy.mail.state().all.is_empty());
    assert_eq!(
        restarted.server.ids.object.get_next_id().unwrap(),
        next_object
    );
    restarted.server.ids.object.release_id(next_object);
    assert_eq!(
        restarted.server.ids.housing_tl.get_next_id().unwrap(),
        next_tl
    );
    restarted.server.ids.housing_tl.release_id(next_tl);
    // A destroyed lodestone remains registered for an uninterrupted, resumed siege.
    restarted.tower.data().unit.hp = 0;
    restarted
        .server
        .systems
        .housing
        .hooks
        .install(Arc::new(EconomyForHousing));
    manager::resume_dead_houses(&restarted.server);
    assert!(!restarted.server.systems.housing.manager.contains(row.id));
    assert!(world.house(restored.obj_id).is_none());
    assert!(restarted.server.systems.housing.manager.contains(500));
    assert_eq!(restarted.tower.data().unit.hp, 0);
    assert_eq!(restarted.server.systems.economy.mail.state().all.len(), 1);
    manager::resume_dead_houses(&restarted.server);
    assert_eq!(restarted.server.systems.economy.mail.state().all.len(), 1);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn original_fort_designs_and_guard_stages_are_not_prototypes() {
    let path = std::env::var("AAEMU_COMPACT_DB").unwrap();
    let conn =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data =
        crate::systems::housing::data::HousingData::load_from(&conn, "en_us", Vec::new()).unwrap();
    let mut found = 0;
    for template in data.templates() {
        if let Some((item, _)) = design(template) {
            found += 1;
            assert_eq!(data.item_id_by_design(template.id, |id| id == item), item);
            assert_eq!(
                conn.query_row("SELECT use_skill_id FROM items WHERE id=?", [item], |row| {
                    row.get::<_, u32>(0)
                })
                .unwrap(),
                FORTIFICATION_BUILD_SKILL
            );
            assert_eq!(template.tax_or_zero(), 0);
            assert!(!template.is_sellable);
        }
    }
    assert_eq!(found, 9);
    assert!(design(data.template(269).unwrap()).is_none());
    assert!(design(data.template(86).unwrap()).is_none());
    let setting = &data.castles.guard_tower_settings[&1];
    assert_eq!(
        protection_buff(
            setting,
            FortCounts {
                walls: 32,
                gates: 1
            }
        ),
        4771
    );
    assert_eq!(
        protection_buff(
            setting,
            FortCounts {
                walls: 33,
                gates: 0
            }
        ),
        4772
    );
    assert_eq!(
        protection_buff(
            setting,
            FortCounts {
                walls: 42,
                gates: 0
            }
        ),
        4774
    );
    assert_eq!(
        protection_buff(
            setting,
            FortCounts {
                walls: 42,
                gates: 1
            }
        ),
        5392
    );
    assert_eq!(
        protection_buff(
            setting,
            FortCounts {
                walls: 45,
                gates: 1
            }
        ),
        5393
    );
}
