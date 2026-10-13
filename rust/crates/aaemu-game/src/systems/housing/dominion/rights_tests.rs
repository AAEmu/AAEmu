use std::sync::Arc;

use super::rights::*;
use super::state::SiegeSide;
use super::{fort, runtime};
use crate::data::world_data::{SystemFaction, Zone};
use crate::data::GameData;
use crate::models::house::House;
use crate::server::GameServer;
use crate::systems::economy::error_message as err;
use crate::systems::housing::data::{GuardTowerSetting, HousingTemplate, SiegeZone};
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{Entity, HouseRef, PlayerRef};

const GROUP: u32 = 25;
const SETTING: u32 = 6;

struct Fixture {
    server: GameServer,
    leader: PlayerRef,
    member: PlayerRef,
    attacker: PlayerRef,
    attacker_member: PlayerRef,
    outsider: PlayerRef,
    tower: HouseRef,
    ordinary: HouseRef,
    _clients: Vec<TestClient>,
}

fn fixture() -> Fixture {
    let mut data = GameData::empty();
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            zone_key: TEST_ZONE,
            group_id: GROUP,
            ..Default::default()
        },
    );
    data.systems.housing.castles.siege_zones.insert(
        GROUP,
        SiegeZone {
            zone_group_id: GROUP,
            ..Default::default()
        },
    );
    data.systems.housing.castles.guard_tower_settings.insert(
        SETTING,
        GuardTowerSetting {
            id: SETTING,
            radius_dominion: 100,
            ..Default::default()
        },
    );
    for (id, category, setting) in [(139, 1, SETTING), (300, 1, 0), (121, 3, 0)] {
        data.systems.housing.add_template(HousingTemplate {
            id,
            category_id: category,
            guard_tower_setting_id: setting,
            hp: 1000,
            ..Default::default()
        });
    }
    let server = test_server_with(data);
    let mut clients = Vec::new();
    let mut players = Vec::new();
    for (id, name, expedition) in [
        (1, "Lord", 10),
        (2, "Member", 10),
        (3, "CapturingLord", 20),
        (4, "AttackerMember", 20),
        (5, "Outsider", 0),
    ] {
        let client = TestClient::new(&server, id, 1000 + id);
        let mut c = test_character(id, name, 100.0, 100.0);
        c.unit.expedition_id = expedition;
        server.names.add_character(id, name, 1000 + id);
        players.push(spawn_player(&server, &client, c, 0x100 + id));
        clients.push(client);
    }
    for (id, owner, members) in [(10, 1, &players[..2]), (20, 3, &players[2..4])] {
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id,
                owner_id: owner,
                ..Default::default()
            },
            members: members
                .iter()
                .map(|p| member_from_character(&server, id, p, p.character_id == owner))
                .collect(),
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
    }
    let make_house = |id, design, owner, x| {
        let mut h = House::new(id, design);
        h.unit.tl_id = id as u16;
        h.current_step = -1;
        h.unit.hp = 1000;
        h.unit.max_hp = 1000;
        h.unit.owner_id = owner;
        h.account_id = if owner == 0 { 0 } else { 1000 + owner };
        h.unit.transform.zone_id = TEST_ZONE;
        h.unit.transform.set_xyz(x, 100.0, 100.0);
        let house = Entity::new(0x1000 + id, h);
        assert!(server.systems.housing.manager.insert(&house));
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&house, &server.view_ctx());
        house
    };
    let tower = make_house(500, 139, 0, 100.0);
    let ordinary = make_house(600, 300, 2, 150.0);
    let (_, identity) = runtime::tower_identity(&server, &tower).unwrap();
    server
        .systems
        .housing
        .dominions
        .declare_unowned_with(GROUP, identity, 10, 1000, || true)
        .unwrap();
    Fixture {
        server,
        leader: players[0].clone(),
        member: players[1].clone(),
        attacker: players[2].clone(),
        attacker_member: players[3].clone(),
        outsider: players[4].clone(),
        tower,
        ordinary,
        _clients: clients,
    }
}

#[tokio::test]
async fn placement_requires_current_guild_inside_bound_and_allows_other_guild_outside() {
    let f = fixture();
    for player in [&f.leader, &f.member] {
        assert_eq!(
            placement_allowed(&f.server, player, 0, 150.0, 100.0, 0.0),
            Ok(())
        );
    }
    for player in [&f.attacker, &f.outsider] {
        assert_eq!(
            placement_allowed(&f.server, player, 0, 150.0, 100.0, 0.0),
            Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
        );
        assert_eq!(
            placement_allowed(&f.server, player, 0, 200.0, 100.0, 0.0),
            Ok(())
        );
        assert_eq!(
            placement_allowed(&f.server, player, 0, 250.0, 100.0, 0.0),
            Ok(())
        );
        assert_eq!(
            placement_allowed(&f.server, player, 0, 199.99, 100.0, 0.0),
            Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
        );
    }
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, f32::NAN, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_INVALID_AREA)
    );
    f.outsider.data().unit.expedition_id = 10;
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 150.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
}

#[tokio::test]
async fn unclaimed_or_missing_authority_locks_province_even_outside_castle() {
    let f = fixture();
    f.server
        .systems
        .housing
        .dominions
        .restore(Vec::new())
        .unwrap();
    for x in [150.0, 250.0] {
        assert_eq!(
            placement_allowed(&f.server, &f.member, 0, x, 100.0, 0.0),
            Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)
        );
    }
    let (_, identity) = runtime::tower_identity(&f.server, &f.tower).unwrap();
    f.server
        .systems
        .housing
        .dominions
        .register_tower(GROUP, identity)
        .unwrap();
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, 250.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)
    );
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
}

#[tokio::test]
async fn ordinary_noncastle_province_is_unaffected_by_missing_dominion() {
    let mut f = fixture();
    Arc::get_mut(&mut f.server.game_data)
        .unwrap()
        .systems
        .housing
        .castles
        .siege_zones
        .clear();
    f.server
        .systems
        .housing
        .dominions
        .restore(Vec::new())
        .unwrap();
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 150.0, 100.0, 0.0),
        Ok(())
    );
}

#[tokio::test]
async fn current_lord_demolishes_only_retained_ordinary_property_inside_strict_bound() {
    let f = fixture();
    let original = f.ordinary.data().clone();
    assert!(can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    for player in [&f.member, &f.attacker, &f.outsider] {
        assert!(!can_demolish_ordinary(&f.server, player, &f.ordinary));
    }
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.tower));
    assert_eq!(*f.ordinary.data(), original);
    f.ordinary.data().unit.transform.x = 200.0;
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    f.ordinary.data().unit.transform.x = 199.99;
    assert!(can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    f.ordinary.data().unit.template_id = 121;
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
}

#[tokio::test]
async fn capture_changes_lord_rights_without_transferring_property_owner_or_account() {
    let f = fixture();
    let original = f.ordinary.data().clone();
    let authority = &f.server.systems.housing.dominions;
    let identity = authority.get(GROUP).unwrap().tower;
    authority
        .schedule_siege(GROUP, 20, 1100, 3000, 1000)
        .unwrap();
    authority
        .register_participant(GROUP, 3, 20, SiegeSide::Attacker, 1100)
        .unwrap();
    authority
        .mark_tower_destroyed(GROUP, identity, 1100)
        .unwrap();
    let handle = authority
        .begin_engraving(GROUP, identity, 3, 20, 1_100_000)
        .unwrap();
    let state = authority
        .complete_engraving(&handle, 20, 1_500_000)
        .unwrap();
    fort::transfer_captured(&f.server, &state);
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    assert!(can_demolish_ordinary(&f.server, &f.attacker, &f.ordinary));
    assert!(!can_demolish_ordinary(
        &f.server,
        &f.attacker_member,
        &f.ordinary
    ));
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, 150.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    assert_eq!(
        placement_allowed(&f.server, &f.attacker_member, 0, 150.0, 100.0, 0.0),
        Ok(())
    );
    assert_eq!(*f.ordinary.data(), original);
}

#[tokio::test]
async fn stale_tower_or_house_handle_and_wrong_instance_never_grant_rights() {
    let f = fixture();
    let world = f.server.world.world(0).unwrap();
    f.server
        .world
        .create_world_instance(world.template.clone(), 99);
    assert_eq!(
        placement_allowed(&f.server, &f.member, 99, 150.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_INVALID_AREA)
    );
    f.leader.data().unit.transform.instance_id = 99;
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    f.leader.data().unit.transform.instance_id = 0;
    world.remove_object(f.tower.obj_id);
    let replacement = Entity::new(f.tower.obj_id, f.tower.data().clone());
    world.add_object(&replacement);
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, 250.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)
    );
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    world.remove_object(replacement.obj_id);
    world.add_object(&f.tower);
    world.remove_object(f.ordinary.obj_id);
    let replacement = Entity::new(f.ordinary.obj_id, f.ordinary.data().clone());
    world.add_object(&replacement);
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &replacement));
}

#[tokio::test]
async fn placement_checks_garden_overlap_and_preserves_strict_circle_boundary() {
    let f = fixture();
    // The center is outside the radius100 castle but the square overlaps its interior.
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 200.5, 100.0, 1.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, 200.5, 100.0, 1.0),
        Ok(())
    );
    // Mere contact with the circle is allowed; moving the garden inward is denied.
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 201.0, 100.0, 1.0),
        Ok(())
    );
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 200.99, 100.0, 1.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 171.0, 171.0, 1.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    assert_eq!(
        placement_allowed(&f.server, &f.outsider, 0, 200.0, 200.0, 1.0),
        Ok(())
    );
    for invalid in [-1.0, f32::NAN, f32::INFINITY] {
        assert_eq!(
            placement_allowed(&f.server, &f.member, 0, 150.0, 100.0, invalid),
            Err(err::HOUSE_CANNOT_LOCATE_INVALID_AREA)
        );
    }
}

#[tokio::test]
async fn stale_membership_or_disbanded_guild_does_not_authorize_castle_property() {
    let f = fixture();
    f.leader.data().unit.expedition_id = 20;
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    f.leader.data().unit.expedition_id = 10;
    f.member.data().unit.expedition_id = 20;
    assert_eq!(
        placement_allowed(&f.server, &f.member, 0, 150.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE)
    );
    f.server
        .expeditions
        .with_mut(10, |guild| guild.is_disbanded = true)
        .unwrap();
    assert!(!can_demolish_ordinary(&f.server, &f.leader, &f.ordinary));
    assert_eq!(
        placement_allowed(&f.server, &f.leader, 0, 250.0, 100.0, 0.0),
        Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)
    );
}
