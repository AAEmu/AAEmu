use std::sync::Arc;

use aaemu_commons::PacketStream;

use super::headquarters::*;
use super::participants;
use super::runtime;
use super::state::SiegeSide;
use crate::data::world_data::{Portal, SystemFaction, Zone};
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::models::house::House;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::housing::data::HousingTemplate;
use crate::systems::skills::data::{BuffTemplate, SkillTemplate};
use crate::systems::skills::effects::{EffectArgs, EffectSource};
use crate::systems::skills::geometry::Target;
use crate::systems::skills::state::{SkillRef, SkillRun};
use crate::systems::skills::types::{CastAction, SkillCastTarget, SkillCaster, SkillObject};
use crate::systems::social::expedition::{member_from_character, Expedition};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{DoodadRef, Entity, HouseRef, Player, PlayerRef, WorldObject};

const GROUP: u32 = 25;

struct Fixture {
    server: GameServer,
    attacker: PlayerRef,
    defender: PlayerRef,
    tower: HouseRef,
    offense: DoodadRef,
    defense: DoodadRef,
    offense_skill: SkillRef,
    defense_skill: SkillRef,
    attacker_client: TestClient,
    defender_client: TestClient,
}

fn fixture(warmup: bool) -> Fixture {
    let mut data = GameData::empty();
    data.systems.npcs.portal = crate::systems::npcs::portal::PortalTables::from_parts(
        vec![Portal {
            id: 99,
            zone_id: TEST_ZONE,
            x: 600.0,
            y: 600.0,
            z: 95.0,
            z_rot: 0.75,
            ..Default::default()
        }],
        Vec::new(),
        Vec::new(),
        Vec::new(),
    )
    .unwrap();
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            zone_key: TEST_ZONE,
            group_id: GROUP,
            ..Default::default()
        },
    );
    data.systems.housing.add_template(HousingTemplate {
        id: 139,
        guard_tower_setting_id: 6,
        ..Default::default()
    });
    for id in [OFFENSE_TELEPORT_SKILL, DEFENSE_TELEPORT_SKILL] {
        data.systems.skills.skills.insert(
            id,
            Arc::new(SkillTemplate {
                id,
                casting_time: 5000,
                target_type: 0,
                ..Default::default()
            }),
        );
    }
    for id in [
        participants::OFFENSE_PARTICIPANT_BUFF,
        participants::DEFENSE_PARTICIPANT_BUFF,
        crate::systems::skills::resurrect::WEAKENED_BODY,
        crate::systems::skills::resurrect::RESPAWN_COOLDOWN,
    ] {
        data.systems.skills.add_buff(BuffTemplate {
            id,
            max_stack: 1,
            system: true,
            dead_applicable: true,
            ..Default::default()
        });
    }
    let server = test_server_with(data);
    let mut attacker_client = TestClient::new(&server, 1, 1001);
    let mut defender_client = TestClient::new(&server, 2, 1002);
    let make_player = |id, name, expedition, client: &TestClient| {
        let mut c = test_character(id, name, 100.0, 100.0);
        c.is_online = true;
        c.unit.hp = 1000;
        c.unit.max_hp = 1000;
        c.unit.max_mp = 2000;
        c.unit.expedition_id = expedition;
        spawn_player(&server, client, c, 0x100 + id)
    };
    let attacker = make_player(1, "Attacker", 20, &attacker_client);
    let defender = make_player(2, "Defender", 10, &defender_client);
    attacker_client.conn.active_char = Some(attacker.clone());
    defender_client.conn.active_char = Some(defender.clone());
    for (expedition, player, buff) in [
        (20, &attacker, participants::OFFENSE_PARTICIPANT_BUFF),
        (10, &defender, participants::DEFENSE_PARTICIPANT_BUFF),
    ] {
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id: expedition,
                owner_id: player.character_id,
                ..Default::default()
            },
            members: vec![member_from_character(&server, expedition, player, true)],
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
        let object = WorldObject::from(player);
        crate::systems::skills::buffs::add_buff_by_id(&server, &object, buff, &object, 0);
    }
    let world = server.world.world(0).unwrap();
    let mut h = House::new(61, 139);
    h.current_step = -1;
    h.unit.transform.zone_id = TEST_ZONE;
    h.unit.transform.set_xyz(100.0, 100.0, 100.0);
    let tower = Entity::new(0x200, h);
    server.systems.housing.manager.insert(&tower);
    world.spawn(&tower, &server.view_ctx());
    let make_hq = |obj, template, phase, x, y, z, yaw| {
        let mut d = Doodad::new(template);
        d.func_group_id = phase;
        d.transform.zone_id = TEST_ZONE;
        d.transform.set_position(x, y, z, 0.0, 0.0, yaw);
        let doodad = Entity::new(obj, d);
        world.spawn(&doodad, &server.view_ctx());
        doodad
    };
    let offense = make_hq(0x300, OFFENSE_HQ_TEMPLATE, 10779, 350.0, 100.0, 123.0, 0.25);
    let defense = make_hq(0x301, DEFENSE_HQ_TEMPLATE, 8173, 120.0, 150.0, 130.0, 0.5);
    let now = chrono::Utc::now().timestamp();
    let (_, identity) = runtime::tower_identity(&server, &tower).unwrap();
    let manager = &server.systems.housing.dominions;
    manager
        .declare_unowned_with(GROUP, identity, 10, now - 1000, || true)
        .unwrap();
    let starts = if warmup { now + 100 } else { now - 500 };
    manager
        .schedule_siege(GROUP, 20, starts, now + 1000, now - 600)
        .unwrap();
    manager
        .register_participant(GROUP, 1, 20, SiegeSide::Attacker, now - 600)
        .unwrap();
    manager
        .register_participant(GROUP, 2, 10, SiegeSide::Defender, now - 600)
        .unwrap();
    let make_skill = |id, player: &PlayerRef, tl| {
        let skill = SkillRun::new(
            server.game_data.systems.skills.skill(id).unwrap().clone(),
            1,
        );
        skill.state().generation = 1;
        skill.state().tl_id = tl;
        skill.state().initial_target = player.obj_id;
        skill
    };
    let offense_skill = make_skill(OFFENSE_TELEPORT_SKILL, &attacker, 5);
    let defense_skill = make_skill(DEFENSE_TELEPORT_SKILL, &defender, 6);
    attacker_client.drain();
    defender_client.drain();
    Fixture {
        server,
        attacker,
        defender,
        tower,
        offense,
        defense,
        offense_skill,
        defense_skill,
        attacker_client,
        defender_client,
    }
}

fn args(player: &PlayerRef, skill: &SkillRef) -> EffectArgs<'static> {
    EffectArgs {
        caster: Some(WorldObject::from(player)),
        caster_caster: SkillCaster::Unit(player.obj_id),
        target: Target::Obj(WorldObject::from(player)),
        target_caster: SkillCastTarget::Unit(player.obj_id),
        cast: CastAction::Skill {
            skill_id: skill.id,
            tl_id: skill.tl_id(),
        },
        source: EffectSource::from_skill(skill.clone()),
        skill_object: &SkillObject::None,
    }
}

fn bind_both(f: &Fixture) {
    f.server
        .systems
        .housing
        .headquarters
        .bind(&f.server, GROUP, SiegeSide::Attacker, &f.offense)
        .unwrap();
    f.server
        .systems
        .housing
        .headquarters
        .bind(&f.server, GROUP, SiegeSide::Defender, &f.defense)
        .unwrap();
}

#[tokio::test]
async fn binds_real_hqs_with_exact_side_phase_province_and_original_distance() {
    let f = fixture(false);
    bind_both(&f); // Exactly250m is valid, including a different elevation.
    let state = f.server.systems.housing.dominions.get(GROUP).unwrap();
    let hq = f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &state, SiegeSide::Attacker)
        .unwrap();
    assert_eq!(hq.obj_id, f.offense.obj_id);
    assert_eq!(
        (hq.position.x, hq.position.y, hq.position.z),
        (350.0, 100.0, 123.0)
    );
    assert_eq!(
        f.server.systems.housing.headquarters.bind(
            &f.server,
            GROUP,
            SiegeSide::Attacker,
            &f.defense
        ),
        Err(HqError::InvalidHeadquarters)
    );
    f.offense.data().transform.x = 349.99;
    assert_eq!(
        f.server.systems.housing.headquarters.bind(
            &f.server,
            GROUP,
            SiegeSide::Attacker,
            &f.offense
        ),
        Err(HqError::TooClose)
    );
    f.offense.data().transform.x = 350.0;
    f.offense.data().func_group_id = 10581; // Still a declaration-stage backpack, not its HQ.
    assert_eq!(
        f.server.systems.housing.headquarters.bind(
            &f.server,
            GROUP,
            SiegeSide::Attacker,
            &f.offense
        ),
        Err(HqError::InvalidHeadquarters)
    );
    f.offense.data().func_group_id = 10582;
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .bind(&f.server, GROUP, SiegeSide::Attacker, &f.offense)
        .is_ok());
    f.offense.data().transform.x = 10_000.0; // Outside the synthetic province.
    assert_eq!(
        f.server.systems.housing.headquarters.bind(
            &f.server,
            GROUP,
            SiegeSide::Attacker,
            &f.offense
        ),
        Err(HqError::InvalidHeadquarters)
    );
}

#[tokio::test]
async fn canonical_participant_self_cast_sends_original_teleport_and_uses_existing_ack() {
    let mut f = fixture(false);
    bind_both(&f);
    let origin = f.attacker.data().unit.transform;
    teleport_effect(&f.server, &args(&f.attacker, &f.offense_skill));
    let packets = f.attacker_client.drain();
    let mut expected = PacketStream::new();
    expected
        .write_u8(TELEPORT_REASON_OFFENSE_HQ)
        .write_i16(0)
        .write_position(350.0, 100.0, 123.0)
        .write_f32(0.25);
    assert_eq!(
        packets,
        vec![(sc::SCTeleportUnitPacket, expected.into_bytes())]
    );
    assert!(f.attacker.data().disabled_set_position);
    assert_eq!(f.attacker.data().unit.transform, origin);
    crate::systems::npcs::portal::packets::CSTeleportEndedPacket {
        x: 350.0,
        y: 100.0,
        z: 123.0,
        ori: [0; 16],
    }
    .handle(&f.server, &mut f.attacker_client.conn)
    .await
    .unwrap();
    assert!(!f.attacker.data().disabled_set_position);
    teleport_effect(&f.server, &args(&f.defender, &f.defense_skill));
    let packets = f.defender_client.drain();
    let teleport = packets
        .iter()
        .find(|p| p.0 == sc::SCTeleportUnitPacket)
        .unwrap();
    let mut expected = PacketStream::new();
    expected
        .write_u8(TELEPORT_REASON_DEFENSE_HQ)
        .write_i16(0)
        .write_position(120.0, 150.0, 130.0)
        .write_f32(0.5);
    assert_eq!(teleport.1, expected.into_bytes());
}

#[tokio::test]
async fn denied_forged_cancelled_unaccepted_or_nonself_sources_never_teleport() {
    let mut f = fixture(false);
    bind_both(&f);
    let original = args(&f.attacker, &f.offense_skill);
    let forged = SkillRun::new(Arc::new(f.offense_skill.template.as_ref().clone()), 1);
    forged.state().generation = 1;
    forged.state().initial_target = f.attacker.obj_id;
    forged.state().tl_id = 5;
    let mut rejected = original.clone();
    rejected.source.skill = Some(forged);
    assert!(!teleport_effect_inner(&f.server, &rejected));
    rejected = original.clone();
    rejected.source.is_trigger = true;
    assert!(!teleport_effect_inner(&f.server, &rejected));
    rejected = original.clone();
    rejected.source.buff = f
        .server
        .game_data
        .systems
        .skills
        .buff(participants::OFFENSE_PARTICIPANT_BUFF)
        .cloned();
    assert!(!teleport_effect_inner(&f.server, &rejected));
    rejected = original.clone();
    rejected.target = Target::Obj(WorldObject::from(&f.defender));
    assert!(!teleport_effect_inner(&f.server, &rejected));
    rejected = original.clone();
    rejected.cast = CastAction::Skill {
        skill_id: OFFENSE_TELEPORT_SKILL,
        tl_id: 9,
    };
    assert!(!teleport_effect_inner(&f.server, &rejected));
    f.offense_skill.state().generation = 0;
    assert!(!teleport_effect_inner(&f.server, &original));
    f.offense_skill.state().generation = 1;
    f.offense_skill.state().cancelled = true;
    assert!(!teleport_effect_inner(&f.server, &original));
    f.offense_skill.state().cancelled = false;
    f.offense_skill.state().initial_target = f.defender.obj_id;
    assert!(!teleport_effect_inner(&f.server, &original));
    assert!(!f.attacker.data().disabled_set_position);
    assert!(f.attacker_client.drain().is_empty());
}

#[tokio::test]
async fn wrong_side_missing_buff_stale_membership_dead_or_other_instance_cannot_teleport() {
    let mut f = fixture(false);
    bind_both(&f);
    let offense = args(&f.attacker, &f.offense_skill);
    let wrong = SkillRun::new(f.defense_skill.template.clone(), 1);
    wrong.state().generation = 1;
    wrong.state().initial_target = f.attacker.obj_id;
    wrong.state().tl_id = 7;
    assert!(!teleport_effect_inner(
        &f.server,
        &args(&f.attacker, &wrong)
    ));
    crate::systems::skills::buffs::remove_buff(
        &f.server,
        &WorldObject::from(&f.attacker),
        participants::OFFENSE_PARTICIPANT_BUFF,
    );
    assert!(!teleport_effect_inner(&f.server, &offense));
    participants::sync_player(&f.server, &f.attacker);
    f.attacker.data().unit.hp = 0;
    assert!(!teleport_effect_inner(&f.server, &offense));
    f.attacker.data().unit.hp = 1000;
    f.attacker.data().unit.transform.instance_id = 99;
    assert!(!teleport_effect_inner(&f.server, &offense));
    f.attacker.data().unit.transform.instance_id = 0;
    f.server
        .expeditions
        .with_mut(20, |e| e.members.clear())
        .unwrap();
    assert!(!teleport_effect_inner(&f.server, &offense));
    assert!(!f.attacker.data().disabled_set_position);
    assert!(!f
        .attacker_client
        .drain()
        .iter()
        .any(|p| p.0 == sc::SCTeleportUnitPacket));
}

#[tokio::test]
async fn warmup_finished_generation_and_restart_require_a_valid_new_binding() {
    let f = fixture(true);
    bind_both(&f);
    assert!(!teleport_effect_inner(
        &f.server,
        &args(&f.attacker, &f.offense_skill)
    ));
    let now = chrono::Utc::now().timestamp();
    let old = f.server.systems.housing.dominions.get(GROUP).unwrap();
    f.server
        .systems
        .housing
        .dominions
        .cancel_siege(GROUP, now)
        .unwrap();
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &old, SiegeSide::Attacker)
        .is_none());
    f.server
        .systems
        .housing
        .dominions
        .schedule_siege(GROUP, 20, now + 10, now + 1000, now)
        .unwrap();
    let next = f.server.systems.housing.dominions.get(GROUP).unwrap();
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &next, SiegeSide::Attacker)
        .is_none());
    assert_eq!(f.server.systems.housing.headquarters.prune(&f.server), 2);
    assert_eq!(f.server.systems.housing.headquarters.prune(&f.server), 0);
    bind_both(&f);
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &next, SiegeSide::Attacker)
        .is_some());
    let restarted = Headquarters::default();
    assert!(restarted
        .snapshot(&f.server, &next, SiegeSide::Attacker)
        .is_none());
    f.server.systems.housing.headquarters.clear_zone(GROUP);
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &next, SiegeSide::Attacker)
        .is_none());
}

#[tokio::test]
async fn moved_reused_deleted_or_stale_tower_headquarters_never_become_a_destination() {
    let mut f = fixture(false);
    bind_both(&f);
    let state = f.server.systems.housing.dominions.get(GROUP).unwrap();
    f.offense.data().transform.x += 1.0;
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &state, SiegeSide::Attacker)
        .is_none());
    assert!(!teleport_effect_inner(
        &f.server,
        &args(&f.attacker, &f.offense_skill)
    ));
    assert!(!f.attacker.data().disabled_set_position);
    assert!(!f
        .attacker_client
        .drain()
        .iter()
        .any(|p| p.0 == sc::SCTeleportUnitPacket));
    f.offense.data().transform.x -= 1.0;
    let world = f.server.world.world(0).unwrap();
    world.remove_object(f.offense.obj_id);
    let replacement = Entity::new(f.offense.obj_id, f.offense.data().clone());
    world.add_object(&replacement);
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &state, SiegeSide::Attacker)
        .is_none());
    world.remove_object(replacement.obj_id);
    world.add_object(&f.offense);
    f.offense.data().phase.deleted = true;
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &state, SiegeSide::Attacker)
        .is_none());
    f.offense.data().phase.deleted = false;
    world.remove_object(f.tower.obj_id);
    assert!(f
        .server
        .systems
        .housing
        .headquarters
        .snapshot(&f.server, &state, SiegeSide::Defender)
        .is_none());
}

#[test]
#[ignore = "requires original read-only 1.2 compact database"]
fn real_hq_templates_phases_and_five_second_self_skill_effects_are_authored() {
    let conn = rusqlite::Connection::open_with_flags(
        std::env::var("AAEMU_COMPACT_DB").unwrap(),
        rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
    )
    .unwrap();
    for (template, phases) in [
        (OFFENSE_HQ_TEMPLATE, [10582, 10779]),
        (DEFENSE_HQ_TEMPLATE, [8173, 8238]),
    ] {
        for phase in phases {
            let owner: u32 = conn
                .query_row(
                    "SELECT doodad_almighty_id FROM doodad_func_groups WHERE id=?1",
                    [phase],
                    |r| r.get(0),
                )
                .unwrap();
            assert_eq!(owner, template);
        }
    }
    for (skill, effect) in [
        (OFFENSE_TELEPORT_SKILL, 17517),
        (DEFENSE_TELEPORT_SKILL, 28036),
    ] {
        let values: (i32, i32) = conn
            .query_row(
                "SELECT casting_time, target_type_id FROM skills WHERE id=?1",
                [skill],
                |r| Ok((r.get(0)?, r.get(1)?)),
            )
            .unwrap();
        assert_eq!(values, (5000, 0));
        let actual: u32 = conn
            .query_row(
                "SELECT actual_id FROM effects WHERE id=?1 AND actual_type='SpecialEffect'",
                [effect],
                |r| r.get(0),
            )
            .unwrap();
        let special: i32 = conn
            .query_row(
                "SELECT special_effect_type_id FROM special_effects WHERE id=?1",
                [actual],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(special, 65);
    }
}

#[tokio::test]
async fn resurrect_packet_uses_registered_side_hq_and_preserves_existing_revive_flow() {
    let mut f = fixture(false);
    bind_both(&f);
    for (player, client, x, y, z, yaw) in [
        (
            &f.attacker,
            &mut f.attacker_client,
            350.0,
            100.0,
            123.0,
            0.25,
        ),
        (
            &f.defender,
            &mut f.defender_client,
            120.0,
            150.0,
            130.0,
            0.5,
        ),
    ] {
        {
            let mut c = player.data();
            c.unit.hp = 0;
            c.unit.mp = 0;
            c.unit.is_under_water = true;
            c.breath = 1;
        }
        let portal = respawn_portal(&f.server, player).unwrap();
        assert_eq!((portal.x, portal.y, portal.z, portal.z_rot), (x, y, z, yaw));
        crate::systems::skills::resurrect::CSResurrectCharacterPacket { in_place: false }
            .handle(&f.server, &mut client.conn)
            .await
            .unwrap();
        let packets = client.drain();
        let mut expected = PacketStream::new();
        expected
            .write_bc(player.obj_id)
            .write_position(x, y, z)
            .write_f32(yaw);
        let expected = expected.into_bytes();
        assert!(packets
            .iter()
            .any(|p| p.0 == sc::SCCharacterResurrectedPacket && p.1 == expected));
        let c = player.data();
        assert_eq!((c.unit.hp, c.unit.mp), (100, 200));
        assert!(!c.unit.is_under_water);
        assert!(c
            .unit
            .buffs
            .check_buff(crate::systems::skills::resurrect::WEAKENED_BODY));
        assert!(c
            .unit
            .buffs
            .check_buff(crate::systems::skills::resurrect::RESPAWN_COOLDOWN));
    }
}

#[tokio::test]
async fn unbound_ordinary_and_in_place_resurrection_preserve_the_existing_fallback() {
    let mut f = fixture(false);
    f.attacker.data().unit.hp = 0;
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
    crate::systems::skills::resurrect::CSResurrectCharacterPacket { in_place: false }
        .handle(&f.server, &mut f.attacker_client.conn)
        .await
        .unwrap();
    let packets = f.attacker_client.drain();
    let mut expected = PacketStream::new();
    expected
        .write_bc(f.attacker.obj_id)
        .write_position(600.0, 600.0, 95.0)
        .write_f32(0.75);
    let expected = expected.into_bytes();
    assert!(packets
        .iter()
        .any(|p| p.0 == sc::SCCharacterResurrectedPacket && p.1 == expected));
    bind_both(&f);
    assert_eq!(
        crate::systems::npcs::portal::closest_return_portal(&f.server, &f.defender).x,
        600.0
    );
    {
        let mut c = f.defender.data();
        c.unit.hp = 0;
        c.skill_char.resurrect_hp_percent = 60;
        c.skill_char.resurrect_mp_percent = 80;
    }
    crate::systems::skills::resurrect::CSResurrectCharacterPacket { in_place: true }
        .handle(&f.server, &mut f.defender_client.conn)
        .await
        .unwrap();
    let packets = f.defender_client.drain();
    let mut expected = PacketStream::new();
    expected
        .write_bc(f.defender.obj_id)
        .write_position(100.0, 100.0, 100.0)
        .write_f32(0.0);
    let expected = expected.into_bytes();
    assert!(packets
        .iter()
        .any(|p| p.0 == sc::SCCharacterResurrectedPacket && p.1 == expected));
    let c = f.defender.data();
    assert_eq!((c.unit.hp, c.unit.mp), (600, 1600));
    assert!(!c
        .unit
        .buffs
        .check_buff(crate::systems::skills::resurrect::RESPAWN_COOLDOWN));
}

#[tokio::test]
async fn living_resurrect_packets_cannot_bypass_hq_cast_or_change_hp_mp_and_position() {
    let mut f = fixture(false);
    bind_both(&f);
    {
        let mut c = f.attacker.data();
        c.unit.hp = 731;
        c.unit.mp = 927;
    }
    let origin = f.attacker.data().unit.transform;
    for in_place in [false, true] {
        crate::systems::skills::resurrect::CSResurrectCharacterPacket { in_place }
            .handle(&f.server, &mut f.attacker_client.conn)
            .await
            .unwrap();
        assert!(f.attacker_client.drain().is_empty());
        let c = f.attacker.data();
        assert_eq!((c.unit.hp, c.unit.mp), (731, 927));
        assert_eq!(c.unit.transform, origin);
        assert!(!c.disabled_set_position);
        assert!(!c
            .unit
            .buffs
            .check_buff(crate::systems::skills::resurrect::WEAKENED_BODY));
        assert!(!c
            .unit
            .buffs
            .check_buff(crate::systems::skills::resurrect::RESPAWN_COOLDOWN));
    }
}

#[tokio::test]
async fn respawn_denies_living_unregistered_stale_or_other_instance_members() {
    let f = fixture(false);
    bind_both(&f);
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
    f.attacker.data().unit.hp = 0;
    assert!(respawn_portal(&f.server, &f.attacker).is_some());
    f.attacker.data().unit.transform.instance_id = 99;
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
    f.attacker.data().unit.transform.instance_id = 0;
    f.attacker.data().is_online = false;
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
    f.attacker.data().is_online = true;
    f.server
        .systems
        .housing
        .dominions
        .unregister_participant(GROUP, f.attacker.character_id)
        .unwrap();
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
    f.defender.data().unit.hp = 0;
    f.server
        .expeditions
        .with_mut(10, |e| e.members.clear())
        .unwrap();
    assert!(respawn_portal(&f.server, &f.defender).is_none());
}

#[tokio::test]
async fn replaced_player_with_same_numeric_identity_cannot_use_retained_cast_or_respawn() {
    let f = fixture(false);
    bind_both(&f);
    let original = WorldObject::from(&f.attacker);
    assert!(runtime::is_live_object(&f.server, &original));
    assert!(runtime::is_live_object(&f.server, &original.clone()));
    let replacement = Player::new(f.attacker.data().clone(), f.attacker_client.conn.handle());
    let world = f.server.world.world(0).unwrap();
    world.remove_object(f.attacker.obj_id);
    world.add_object(&replacement);
    assert!(!runtime::is_live_object(&f.server, &original));
    assert!(runtime::is_live_object(
        &f.server,
        &WorldObject::from(&replacement)
    ));
    assert!(!teleport_effect_inner(
        &f.server,
        &args(&f.attacker, &f.offense_skill)
    ));
    f.attacker.data().unit.hp = 0;
    assert!(respawn_portal(&f.server, &f.attacker).is_none());
}
