use super::participants::*;
use super::runtime;
use super::state::SiegeSide;
use crate::data::world_data::{SystemFaction, Zone};
use crate::data::GameData;
use crate::models::house::House;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::housing::data::HousingTemplate;
use crate::systems::skills::data::BuffTemplate;
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{Entity, HouseRef, PlayerRef, WorldObject};

const GROUP: u32 = 25;
const NOW: i64 = 2000;

struct Fixture {
    server: GameServer,
    attacker: PlayerRef,
    defender: PlayerRef,
    tower: HouseRef,
    attacker_client: TestClient,
    defender_client: TestClient,
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
    data.zones.zones.insert(
        TEST_ZONE + 1,
        Zone {
            zone_key: TEST_ZONE + 1,
            group_id: GROUP + 1,
            ..Default::default()
        },
    );
    data.systems.housing.add_template(HousingTemplate {
        id: 139,
        guard_tower_setting_id: 6,
        ..Default::default()
    });
    for id in [
        OFFENSE_PARTICIPANT_BUFF,
        DEFENSE_PARTICIPANT_BUFF,
        OFFENSE_COMMANDER_BUFF,
        DEFENSE_COMMANDER_BUFF,
        9999,
    ] {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            max_stack: 1,
            system: true,
            dead_applicable: true,
            remove_on_death: false,
            ..Default::default()
        });
    }
    let server = test_server_with(data);
    crate::systems::skills::install_hooks(&server);
    let mut attacker_client = TestClient::new(&server, 1, 1001);
    let mut defender_client = TestClient::new(&server, 2, 1002);
    let make_player = |id, name, expedition, client: &TestClient| {
        let mut c = test_character(id, name, 100.0, 100.0);
        c.unit.expedition_id = expedition;
        c.is_online = true;
        spawn_player(&server, client, c, 0x100 + id)
    };
    let attacker = make_player(1, "Attacker", 20, &attacker_client);
    let defender = make_player(2, "Defender", 10, &defender_client);
    for (id, player) in [(20, &attacker), (10, &defender)] {
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id,
                owner_id: player.character_id,
                ..Default::default()
            },
            members: vec![member_from_character(&server, id, player, true)],
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
    }
    let mut h = House::new(61, 139);
    h.current_step = -1;
    h.unit.transform.zone_id = TEST_ZONE;
    h.unit.transform.set_xyz(105.0, 100.0, 100.0);
    let tower = Entity::new(0x200, h);
    server.systems.housing.manager.insert(&tower);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&tower, &server.view_ctx());
    let (_, identity) = runtime::tower_identity(&server, &tower).unwrap();
    let authority = &server.systems.housing.dominions;
    authority
        .declare_unowned_with(GROUP, identity, 10, 1000, || true)
        .unwrap();
    authority
        .schedule_siege(GROUP, 20, 2000, 3000, 1500)
        .unwrap();
    authority
        .register_participant(GROUP, 1, 20, SiegeSide::Attacker, 1500)
        .unwrap();
    authority
        .register_participant(GROUP, 2, 10, SiegeSide::Defender, 1500)
        .unwrap();
    attacker_client.drain();
    defender_client.drain();
    Fixture {
        server,
        attacker,
        defender,
        tower,
        attacker_client,
        defender_client,
    }
}

#[tokio::test]
async fn active_roster_projects_exact_sides_and_native_buff_packets_once() {
    let mut f = fixture();
    sync_player_at(&f.server, &f.attacker, NOW - 1);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    assert!(f.attacker_client.drain().is_empty());
    sync_player_at(&f.server, &f.attacker, NOW);
    sync_player_at(&f.server, &f.defender, NOW);
    assert!(f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(DEFENSE_PARTICIPANT_BUFF));
    assert!(f
        .defender
        .data()
        .unit
        .buffs
        .check_buff(DEFENSE_PARTICIPANT_BUFF));
    assert!(!f
        .defender
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    assert!(f
        .attacker_client
        .drain()
        .iter()
        .any(|p| p.0 == sc::SCBuffCreatedPacket));
    f.defender_client.drain();
    sync_player_at(&f.server, &f.attacker, NOW + 1);
    sync_player_at(&f.server, &f.defender, NOW + 1);
    assert!(f.attacker_client.drain().is_empty());
    assert!(f.defender_client.drain().is_empty());
}

#[tokio::test]
async fn unregister_end_and_disconnect_remove_participation_without_touching_other_buffs() {
    let mut f = fixture();
    let player = WorldObject::from(&f.attacker);
    crate::systems::skills::buffs::add_buff_by_id(&f.server, &player, 9999, &player, 0);
    sync_player_at(&f.server, &f.attacker, NOW);
    f.attacker_client.drain();
    f.server
        .systems
        .housing
        .dominions
        .unregister_participant(GROUP, 1)
        .unwrap();
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    assert!(f.attacker.data().unit.buffs.check_buff(9999));
    assert!(f
        .attacker_client
        .drain()
        .iter()
        .any(|p| p.0 == sc::SCBuffRemovedPacket));
    sync_player_at(&f.server, &f.defender, NOW);
    sync_player_at(&f.server, &f.defender, 3000);
    assert!(!f
        .defender
        .data()
        .unit
        .buffs
        .check_buff(DEFENSE_PARTICIPANT_BUFF));
    sync_player_at(&f.server, &f.defender, NOW);
    clear_player(&f.server, &f.defender);
    assert!(!f
        .defender
        .data()
        .unit
        .buffs
        .check_buff(DEFENSE_PARTICIPANT_BUFF));
    sync_player_at(&f.server, &f.defender, NOW);
    f.defender.data().is_online = false;
    sync_player_at(&f.server, &f.defender, NOW);
    assert!(!f
        .defender
        .data()
        .unit
        .buffs
        .check_buff(DEFENSE_PARTICIPANT_BUFF));
}

#[tokio::test]
async fn dead_members_keep_original_buff_but_stale_membership_zone_or_instance_cannot() {
    let f = fixture();
    f.attacker.data().unit.hp = 0;
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    f.attacker.data().unit.expedition_id = 10;
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    f.attacker.data().unit.expedition_id = 20;
    f.attacker.data().unit.transform.zone_id = TEST_ZONE + 1;
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    f.attacker.data().unit.transform.zone_id = TEST_ZONE;
    f.attacker.data().unit.transform.instance_id = 99;
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    f.attacker.data().unit.transform.instance_id = 0;
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    f.server
        .expeditions
        .with_mut(20, |e| e.is_disbanded = true)
        .unwrap();
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
}

#[tokio::test]
async fn replaced_tower_and_persisted_commander_buffs_fail_closed() {
    let f = fixture();
    let object = WorldObject::from(&f.attacker);
    for id in [
        OFFENSE_COMMANDER_BUFF,
        DEFENSE_COMMANDER_BUFF,
        DEFENSE_PARTICIPANT_BUFF,
    ] {
        crate::systems::skills::buffs::add_buff_by_id(&f.server, &object, id, &object, 0);
    }
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
    for id in [
        OFFENSE_COMMANDER_BUFF,
        DEFENSE_COMMANDER_BUFF,
        DEFENSE_PARTICIPANT_BUFF,
    ] {
        assert!(!f.attacker.data().unit.buffs.check_buff(id));
    }
    let world = f.server.world.world(0).unwrap();
    world.delete(&f.tower);
    let replacement = Entity::new(f.tower.obj_id, f.tower.data().clone());
    world.spawn(&replacement, &f.server.view_ctx());
    sync_player_at(&f.server, &f.attacker, NOW);
    assert!(!f
        .attacker
        .data()
        .unit
        .buffs
        .check_buff(OFFENSE_PARTICIPANT_BUFF));
}

#[test]
#[ignore = "requires the read-only original 1.2 compact database"]
fn real_participant_buffs_grant_exact_teleports_and_survive_death() {
    let path = std::env::var("AAEMU_COMPACT_DB").expect("AAEMU_COMPACT_DB");
    let conn =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    for (buff, skill) in [
        (OFFENSE_PARTICIPANT_BUFF, 17046),
        (DEFENSE_PARTICIPANT_BUFF, 20950),
    ] {
        let count: i32 = conn
            .query_row(
                "SELECT count(*) FROM buff_skills WHERE buff_id=?1 AND skill_id=?2",
                [buff, skill],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(count, 1);
        let values: (i32, String, String, String) = conn
            .query_row(
                "SELECT duration, system, remove_on_death, dead_applicable FROM buffs WHERE id=?1",
                [buff],
                |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?, r.get(3)?)),
            )
            .unwrap();
        assert_eq!(values, (0, "t".into(), "f".into(), "t".into()));
    }
}
