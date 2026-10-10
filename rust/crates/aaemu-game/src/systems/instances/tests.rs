//! Byte-exact packet tests, data loader tests (in-memory SQLite + the gated real
//! `compact.sqlite3`) and unit tests of the instances subsystem.

use aaemu_commons::PacketStream;
use rusqlite::Connection;

use super::data::*;
use super::packets::*;
use crate::net::packet::{encode, ServerPacket};
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;

fn body(p: &dyn ServerPacket) -> Vec<u8> {
    let frame = encode(p);
    assert_eq!(&frame[..2], &[0xdd, 0x01]);
    assert_eq!(u16::from_le_bytes([frame[4], frame[5]]), p.opcode());
    frame[6..].to_vec()
}

const ZID: ZoneInstanceId = ZoneInstanceId {
    zone_id: 198,
    instance_id: 0x65,
};

fn zid_bytes(s: &mut PacketStream) {
    s.write_u32(198).write_u32(0x65);
}

#[test]
fn opcodes() {
    assert_eq!(
        SCProcessingInstancePacket { zone_id: 1 }.opcode(),
        sc::SCProcessingInstancePacket
    );
    assert_eq!(
        SCInstantGameRemainTimePacket { remain_time: 1 }.opcode(),
        sc::SCInstantGameUnkPacket
    );
    assert_eq!(
        SCTowerDefWaveStartPacket {
            key: TowerDefKey::default(),
            event_zone_id: 0,
            step: 0
        }
        .opcode(),
        sc::SCTowerDefWaveStartPacket
    );
}

#[test]
fn simple_packets_are_byte_exact() {
    assert_eq!(
        body(&SCProcessingInstancePacket { zone_id: 236 }),
        236i32.to_le_bytes().to_vec()
    );
    assert_eq!(
        body(&SCAppliedToInstantGamePacket {
            battlefield_id: 7,
            corps: 2,
            error_message_id: 0
        }),
        vec![7, 0, 0, 0, 2, 0, 0]
    );
    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u32(22).write_u8(1).write_u64(1);
    assert_eq!(
        body(&SCInviteToInstantGamePacket {
            zone_instance_id: ZID,
            ruleset_id: 22,
            corps: 1,
            qualified_id: 1
        }),
        e.into_bytes()
    );
    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u32(1000).write_u32(21000);
    assert_eq!(
        body(&SCInstantGameStartPacket {
            zone_instance_id: ZID,
            start: 21000,
            now: 1000
        }),
        e.into_bytes()
    );
    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_i8(3).write_u32(0).write_bool(true);
    assert_eq!(
        body(&SCInstantGameKillstreakPacket {
            zone_instance_id: ZID,
            killstreak: 3,
            skill_id: 0,
            enabled: true
        }),
        e.into_bytes()
    );
    assert_eq!(
        body(&SCInstantGameRemainTimePacket { remain_time: 60 }),
        vec![60, 0, 0, 0]
    );
    let mut e = PacketStream::new();
    e.write_bc(0x1234)
        .write_u32(5)
        .write_u32(0)
        .write_bool(false);
    assert_eq!(
        body(&SCSkillCooldownResetPacket {
            unit_id: 0x1234,
            skill_id: 5,
            tag_id: 0,
            gcd: false
        }),
        e.into_bytes()
    );
}

#[test]
fn instant_game_joined_is_byte_exact() {
    let r = GameRuleSet {
        id: 22,
        corps_size: 5,
        corps1_faction_id: 159,
        corps2_faction_id: 160,
        time_opening: 4,
        time_ending: 1,
        time_playing: 15,
        victory_score: 40,
        battlefield_id: 7,
    };
    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u8(0)
        .write_i32(40)
        .write_u16(0)
        .write_u32(0)
        .write_i32(4 * 60_000)
        .write_i32(15 * 60_000)
        .write_i32(60_000)
        .write_u32(159)
        .write_u32(160);
    for _ in 0..24 {
        e.write_u32(0);
    }
    assert_eq!(
        body(&SCInstantGameJoinedPacket {
            zone_instance_id: ZID,
            corps: 0,
            rule_set: r
        }),
        e.into_bytes()
    );
}

fn member(id: u32, name: &str, score: i32, kills: u16, deaths: u16) -> TeamMemberWire {
    TeamMemberWire {
        character_id: id,
        name: name.into(),
        score,
        bonus: 0,
        bonus_set: 0,
        killstreak: 2,
        kills,
        assists: 0,
        deaths,
    }
}

fn member_bytes(s: &mut PacketStream, m: &TeamMemberWire) {
    s.write_u32(m.character_id)
        .write_str(&m.name)
        .write_i32(m.score)
        .write_i32(m.bonus)
        .write_u32(m.bonus_set)
        .write_u16(m.killstreak)
        .write_u16(m.kills)
        .write_u16(m.assists)
        .write_u16(m.deaths);
}

#[test]
fn instant_game_end_and_scores_are_byte_exact() {
    let a = member(1, "Alpha", 30, 1, 0);
    let b = member(2, "Beta", 60, 2, 1);
    let c = member(3, "Gamma", 0, 0, 3);
    let r1 = TeamResultWire {
        state: 2,
        faction_id: 159,
        members: vec![a.clone(), b.clone()],
    };
    let r2 = TeamResultWire {
        state: 1,
        faction_id: 160,
        members: vec![c.clone()],
    };
    assert_eq!(
        (r1.score(), r1.total_kills(), r1.total_deaths()),
        (90, 3, 1)
    );
    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u8(8);
    e.write_u8(2)
        .write_u32(159)
        .write_i32(90)
        .write_i16(3)
        .write_i16(1)
        .write_u8(2);
    member_bytes(&mut e, &a);
    member_bytes(&mut e, &b);
    e.write_u8(1)
        .write_u32(160)
        .write_i32(0)
        .write_i16(0)
        .write_i16(3)
        .write_u8(1);
    member_bytes(&mut e, &c);
    assert_eq!(
        body(&SCInstantGameEndPacket {
            zone_instance_id: ZID,
            reason: 8,
            corps1: r1,
            corps2: r2
        }),
        e.into_bytes()
    );

    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u8(0)
        .write_i32(30)
        .write_i32(90)
        .write_i32(0)
        .write_str("Beta");
    assert_eq!(
        body(&SCInstantGameAddPointPacket {
            zone_instance_id: ZID,
            corps: 0,
            points: 30,
            score1: 90,
            score2: 0,
            char_name: "Beta".into()
        }),
        e.into_bytes()
    );

    let mut e = PacketStream::new();
    zid_bytes(&mut e);
    e.write_u32(1)
        .write_u32(3)
        .write_u8(0)
        .write_u8(1)
        .write_i8(2)
        .write_str("Alpha")
        .write_str("Gamma")
        .write_i32(3)
        .write_i32(0);
    assert_eq!(
        body(&SCInstantGameKillPacket {
            zone_instance_id: ZID,
            killer_id: 1,
            victim_id: 3,
            killer_corps: 0,
            victim_corps: 1,
            killer_killstreak: 2,
            killer_name: "Alpha".into(),
            victim_name: "Gamma".into(),
            killer_corps_kills: 3,
            victim_corps_deaths: 0
        }),
        e.into_bytes()
    );
}

#[test]
fn tower_def_packets_are_byte_exact() {
    let key = TowerDefKey {
        tower_def_id: 12,
        zone_group_id: 5,
    };
    assert_eq!(
        body(&SCTowerDefStartPacket {
            key,
            event_zone_id: 129
        }),
        vec![12, 0, 0, 0, 5, 0, 129, 0, 0, 0]
    );
    assert_eq!(
        body(&SCTowerDefEndPacket {
            key,
            event_zone_id: 129
        }),
        vec![12, 0, 0, 0, 5, 0, 129, 0, 0, 0]
    );
    assert_eq!(
        body(&SCTowerDefWaveStartPacket {
            key,
            event_zone_id: 129,
            step: 3
        }),
        vec![12, 0, 0, 0, 5, 0, 129, 0, 0, 0, 3, 0, 0, 0]
    );
    let info = TowerDefInfo {
        key,
        zone_id: 129,
        spot_id: 2,
        target_obj_id: 0x0A0B0C,
        position: (100.5, 2000.25, 30.0),
        current_step: 4,
    };
    let mut e = PacketStream::new();
    e.write_i32(1)
        .write_u32(12)
        .write_u16(5)
        .write_u32(129)
        .write_u32(2)
        .write_bc(0x0A0B0C)
        .write_i64(((100.5f32 * 4096.0) as i64) << 32)
        .write_i64(((2000.25f32 * 4096.0) as i64) << 32)
        .write_f32(30.0)
        .write_u32(4);
    assert_eq!(
        body(&SCTowerDefListPacket { infos: vec![info] }),
        e.into_bytes()
    );
}

#[test]
fn client_packets_decode() {
    let mut s = PacketStream::from_bytes(vec![7, 0, 0, 0, 2]);
    assert_eq!(
        CSApplyToInstantGamePacket::read(&mut s).unwrap(),
        CSApplyToInstantGamePacket {
            instance_id: 7,
            corps: 2
        }
    );
    let mut b = vec![1];
    b.extend_from_slice(&5u64.to_le_bytes());
    let mut s = PacketStream::from_bytes(b);
    assert_eq!(
        CSJoinInstantGamePacket::read(&mut s).unwrap(),
        CSJoinInstantGamePacket {
            join: true,
            qualified_id: 5
        }
    );
    let mut s = PacketStream::from_bytes(9u64.to_le_bytes().to_vec());
    assert_eq!(
        CSEnteredInstantGameWorldPacket::read(&mut s)
            .unwrap()
            .qualified_id,
        9
    );
    let mut b = Vec::new();
    b.extend_from_slice(&3u16.to_le_bytes());
    b.extend_from_slice(&(-1i32).to_le_bytes());
    b.extend_from_slice(&0x65u32.to_le_bytes());
    let mut s = PacketStream::from_bytes(b);
    assert_eq!(
        CSUnknownInstancePacket::read(&mut s).unwrap(),
        CSUnknownInstancePacket {
            kind: 3,
            zone_id: -1,
            inst_id: 0x65
        }
    );
    let mut s = PacketStream::from_bytes(Vec::new());
    assert!(CSInstanceLoadedPacket::read(&mut s).is_ok());
    assert!(CSCancelInstantGamePacket::read(&mut s).is_ok());
    assert!(CSLeaveInstantGamePacket::read(&mut s).is_ok());
}

// ---------------------------------------------------------------------------------------------
// Data loaders
// ---------------------------------------------------------------------------------------------

fn schema() -> Connection {
    let c = Connection::open_in_memory().unwrap();
    c.execute_batch(
        "CREATE TABLE indun_actions (id INT, zone_group_id INT, name TEXT, detail_id INT, detail_type TEXT, next_action_id TEXT);
         CREATE TABLE indun_action_change_doodad_phases (id INT, doodad_almighty_id INT, doodad_func_group_id INT);
         CREATE TABLE indun_action_remove_tagged_npcs (id INT, tag_id INT);
         CREATE TABLE indun_action_set_room_cleareds (id INT, indun_room_id INT);
         CREATE TABLE indun_events (id INT, zone_group_id INT, name TEXT, condition_id INT, condition_type TEXT, start_action_id INT);
         CREATE TABLE indun_event_doodad_spawneds (id INT, doodad_almighty_id INT, doodad_func_group_id INT);
         CREATE TABLE indun_event_no_alive_ch_in_rooms (id INT, room_id INT);
         CREATE TABLE indun_event_npc_combat_endeds (id INT, npc_id INT);
         CREATE TABLE indun_event_npc_combat_starteds (id INT, npc_id INT);
         CREATE TABLE indun_event_npc_killeds (id INT, npc_id INT);
         CREATE TABLE indun_event_npc_spawneds (id INT, npc_id INT);
         CREATE TABLE indun_zones (zone_group_id INT, name TEXT, COMMENT TEXT, level_min INT, level_max INT, max_players INT, pvp TEXT, has_graveyard TEXT, item_id INT, restore_item_time INT, party_only TEXT, client_driven TEXT, select_channel TEXT);
         CREATE TABLE indun_rooms (id INT, zone_group_id INT, name TEXT, shape_id INT, shape_type TEXT);
         CREATE TABLE indun_room_spheres (id INT, center_doodad_id INT, radius INT);
         CREATE TABLE localized_texts (tbl_name TEXT, tbl_column_name TEXT, idx INT, en_us TEXT);
         INSERT INTO indun_actions VALUES (100, 76, 'a', 73, 'IndunActionChangeDoodadPhase', '94');
         INSERT INTO indun_actions VALUES (94, 76, 'b', 5, 'IndunActionRemoveTaggedNpc', NULL);
         INSERT INTO indun_actions VALUES (95, 76, 'c', 6, 'IndunActionSetRoomCleared', '');
         INSERT INTO indun_actions VALUES (96, 76, 'd', 0, 'NpcSpawnerSpawnEffect', '0');
         INSERT INTO indun_action_change_doodad_phases VALUES (73, 6499, 17355);
         INSERT INTO indun_action_remove_tagged_npcs VALUES (5, 1234);
         INSERT INTO indun_action_set_room_cleareds VALUES (6, 12);
         INSERT INTO indun_events VALUES (10, 66, 'k', 3, 'IndunEventNpcKilled', 14);
         INSERT INTO indun_events VALUES (11, 66, 'r', 3, 'IndunEventNoAliveChInRoom', 26);
         INSERT INTO indun_events VALUES (12, 66, 's', 4, 'IndunEventNpcSpawned', 22);
         INSERT INTO indun_events VALUES (13, 76, 'd', 10, 'IndunEventDoodadSpawned', 100);
         INSERT INTO indun_event_npc_killeds VALUES (3, 777);
         INSERT INTO indun_event_no_alive_ch_in_rooms VALUES (3, 12);
         INSERT INTO indun_event_npc_spawneds VALUES (4, 888);
         INSERT INTO indun_event_doodad_spawneds VALUES (10, 6499, 17355);
         INSERT INTO indun_zones VALUES (49, 'mirage', 'c', 1, 55, 190, 'f', 'f', NULL, 0, 'f', 'f', 'f');
         INSERT INTO indun_zones VALUES (55, 'nach', 'c', 50, 55, 10, 't', 't', 27965, 3600, 't', 'f', 'f');
         INSERT INTO indun_zones VALUES (70, 'lib', 'c', 50, 55, 190, 'f', 't', NULL, 0, 'f', 'f', 't');
         INSERT INTO indun_rooms VALUES (12, 75, 'room', 12, 'IndunRoomSphere');
         INSERT INTO indun_room_spheres VALUES (12, 7692, 50);
         INSERT INTO localized_texts VALUES ('indun_zones', 'name', 55, 'Nachashgar');",
    )
    .unwrap();
    c
}

#[test]
fn indun_data_loads() {
    let c = schema();
    let d = IndunData::load(&c, "en_us").unwrap();
    assert_eq!(d.actions.len(), 4);
    assert_eq!(
        d.action(100).unwrap().kind,
        IndunActionKind::ChangeDoodadPhases {
            doodad_almighty_id: 6499,
            doodad_func_group_id: 17355
        }
    );
    assert_eq!(d.action(100).unwrap().next_action_id, 94);
    assert_eq!(d.action(94).unwrap().next_action_id, 0);
    assert_eq!(d.action(95).unwrap().next_action_id, 0);
    assert_eq!(
        d.action(94).unwrap().kind,
        IndunActionKind::RemoveTaggedNpcs { tag_id: 1234 }
    );
    assert_eq!(
        d.action(95).unwrap().kind,
        IndunActionKind::SetRoomCleareds { indun_room_id: 12 }
    );
    assert_eq!(d.action(96).unwrap().kind, IndunActionKind::NpcSpawner);
    // Events in the C# query order (doodad, room, combat end, combat start, killed, spawned).
    let ev: Vec<u32> = d.indun_events(66).iter().map(|e| e.id).collect();
    assert_eq!(ev, vec![11, 10, 12]);
    assert_eq!(d.indun_events(76).len(), 1);
    assert!(d.indun_events(1).is_empty());
    assert_eq!(d.event_by_id(12).unwrap().start_action_id, 22);
    let mirage = d.dungeon_zone(49).unwrap();
    assert_eq!(mirage.enter_count, 1000);
    assert!(!mirage.pvp && !mirage.party_only);
    assert_eq!(mirage.to_string(), "mirage");
    let nach = d.dungeon_zone(55).unwrap();
    assert_eq!(nach.enter_count, 3);
    assert_eq!(nach.item_id, 27965);
    assert!(nach.party_only);
    assert_eq!(nach.to_string(), "Nachashgar");
    assert_eq!(d.dungeon_zone(70).unwrap().enter_count, 1000);
    assert_eq!(
        d.room(12).copied(),
        Some(IndunRoom {
            id: 12,
            doodad_id: 7692,
            radius: 50,
            zone_group_id: 75
        })
    );
}

#[test]
fn battlefield_data_loads_with_json_quirks() {
    let c = Connection::open_in_memory().unwrap();
    c.execute_batch(
        "CREATE TABLE battle_fields (id INT, name TEXT, zone_key INT);
         CREATE TABLE game_rule_sets (id INT, corps_size INT, corps1_id INT, corps2_id INT, time_opening INT, time_ending INT, time_playing INT, victory_score INT, battle_field_id INT);
         INSERT INTO battle_fields VALUES (7, 'camp', 198), (11, 'arena', 302), (1, 'qa', 198);
         INSERT INTO game_rule_sets VALUES (17, 2, 159, 160, 4, 1, 15, 0, 1);
         INSERT INTO game_rule_sets VALUES (26, 2, 159, 160, 4, 1, 15, 0, 1);
         INSERT INTO game_rule_sets VALUES (28, 1, 159, 160, 2, 1, 10, 60, 11);",
    )
    .unwrap();
    let json = r#"[
        // comment
        { "BattlefieldId": 11, "Corps1Spawn": { "ZoneId": 302, "X": 575, "Y": 606, "Z": 266 },
          "Corps2Spawn": { "zoneid": 302, "x": 575, "y": 406, "z": 266, "RotationZ": -5 } },
        { "BattlefieldId": 99, "Corps1Spawn": null },
        { "BattlefieldId": 7, "Corps1Spawn": { "X": 1 } },
    ]"#;
    let d = BattlefieldData::load(&c, Some(json)).unwrap();
    let arena = d.battlefield(11).unwrap();
    assert_eq!(arena.zone_key, 302);
    let s = arena.spawns.unwrap();
    assert_eq!(s.corps1_spawn.unwrap().y, 606.0);
    assert_eq!(s.corps2_spawn.unwrap().rotation_z, -5);
    assert_eq!(arena.rule_set.unwrap().id, 28);
    // The last rule set row of a battlefield wins.
    assert_eq!(d.battlefield(1).unwrap().rule_set.unwrap().id, 26);
    // Note (C#): the unknown battlefield 99 ends the JSON loop: 7 gets no spawns.
    assert!(d.battlefield(7).unwrap().spawns.is_none());
    assert!(d.battlefield(7).unwrap().rule_set.is_none());
    // A missing / empty file is an error.
    assert!(BattlefieldData::load(&c, None).is_err());
    assert!(BattlefieldData::load(&c, Some("  ")).is_err());
}

#[test]
fn tower_def_data_loads_and_stops_on_orphans() {
    let c = Connection::open_in_memory().unwrap();
    c.execute_batch(
        "CREATE TABLE tower_defs (id INT, tod REAL, first_wave_after REAL, target_npc_spawner_id INT, kill_npc_id INT, kill_npc_count INT, force_end_time REAL, tod_day_interval INT);
         CREATE TABLE tower_def_progs (id INT, tower_def_id INT, cond_to_next_time REAL, cond_comp_by_and TEXT);
         CREATE TABLE tower_def_prog_spawn_targets (id INT, tower_def_prog_id INT, spawn_target_id INT, spawn_target_type TEXT, despawn_on_next_step TEXT);
         CREATE TABLE tower_def_prog_kill_targets (id INT, tower_def_prog_id INT, kill_target_id INT, kill_target_type TEXT, kill_count INT);
         INSERT INTO tower_defs VALUES (12, 18.0, 0.0, 13638, NULL, NULL, 4800.0, 1);
         INSERT INTO tower_def_progs VALUES (13, 12, 60.0, 't');
         INSERT INTO tower_def_progs VALUES (14, 99, 60.0, 't');
         INSERT INTO tower_def_progs VALUES (15, 12, 60.0, 'f');
         INSERT INTO tower_def_prog_spawn_targets VALUES (10, 13, 9893, 'NpcSpawner', 'f');
         INSERT INTO tower_def_prog_kill_targets VALUES (10, 13, 8181, 'Npc', 1);
         INSERT INTO tower_def_prog_kill_targets VALUES (11, 77, 8181, 'Npc', 1);
         INSERT INTO tower_def_prog_kill_targets VALUES (12, 13, 8182, 'Npc', 2);",
    )
    .unwrap();
    let d = TowerDefData::load(&c).unwrap();
    let t = &d.tower_defs[&12];
    assert_eq!(t.target_npc_spawn_id, 13638);
    assert_eq!(t.kill_npc_id, 0);
    assert_eq!(t.force_end_time, 4800.0);
    // Note (C#): the orphan prog 14 ends the loop: 15 is not loaded.
    assert_eq!(t.progs, vec![13]);
    let p = &d.progs[&13];
    assert!(p.cond_comp_by_and);
    assert_eq!(p.spawn_targets.len(), 1);
    assert_eq!(p.spawn_targets[0].spawn_target_id, 9893);
    assert!(!p.spawn_targets[0].despawn_on_next_step);
    // The orphan kill target 11 ends that loop too.
    assert_eq!(p.kill_targets.len(), 1);
    assert_eq!(p.kill_targets[0].kill_target_id, 8181);
}

#[test]
fn zone_order_keeps_row_order() {
    let c = Connection::open_in_memory().unwrap();
    c.execute_batch(
        "CREATE TABLE zones (id INT, zone_key INT, group_id INT);
         INSERT INTO zones VALUES (1, 300, 55), (2, 100, 1), (3, 200, 55);",
    )
    .unwrap();
    let z = ZoneOrder::load(&c).unwrap();
    assert_eq!(z.zone_keys_in_group(55), vec![300, 200]);
    assert!(z.zone_keys_in_group(9).is_empty());
}

#[test]
fn entry_attempts_are_limited_within_four_hours() {
    let m = super::indun::IndunManager::default();
    let zone = IndunZone {
        zone_group_id: 55,
        enter_count: 3,
        ..IndunZone::default()
    };
    // Checking does not add an entry.
    assert!(m.check_entry_attempt_count(1, 55, &zone, false));
    assert_eq!(m.entry_count(1, 55), 0);
    for _ in 0..3 {
        assert!(m.check_entry_attempt_count(1, 55, &zone, true));
    }
    assert_eq!(m.entry_count(1, 55), 3);
    assert!(!m.check_entry_attempt_count(1, 55, &zone, true));
    assert!(!m.check_entry_attempt_count(1, 55, &zone, false));
    // Other players / zone groups are separate.
    assert!(m.check_entry_attempt_count(2, 55, &zone, true));
    assert!(m.check_entry_attempt_count(1, 56, &zone, true));
}

/// The real 1.2 tables (`AAEMU_COMPACT_DB=... cargo test -p aaemu-game real_instances_data --
/// --ignored`); `battlefields.json` is read from `<db dir>/../battlefields.json`.
#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_instances_data() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
    let app = std::path::Path::new(&path)
        .parent()
        .unwrap()
        .parent()
        .unwrap();
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], app);
    let d = super::Data::load(&crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: app,
        files: &files,
        language: "en_us",
    })
    .unwrap();
    let indun = &d.indun;
    eprintln!(
        "actions {} events {} zones {} rooms {} battlefields {} tower defs {} progs {}",
        indun.actions.len(),
        indun.events.values().map(Vec::len).sum::<usize>(),
        indun.zones.len(),
        indun.rooms.len(),
        d.battlefields.battlefields.len(),
        d.tower_defs.tower_defs.len(),
        d.tower_defs.progs.len()
    );
    assert_eq!(indun.actions.len(), 104);
    assert_eq!(indun.events.values().map(Vec::len).sum::<usize>(), 70);
    assert_eq!(indun.zones.len(), 20);
    assert_eq!(indun.rooms.len(), 13);
    // Every chained / started action exists.
    for a in indun.actions.values() {
        if a.next_action_id > 0 {
            assert!(indun.action(a.next_action_id).is_some(), "{a:?}");
        }
    }
    let mirage = indun.dungeon_zone(49).unwrap();
    assert_eq!(mirage.max_players, 190);
    assert_eq!(mirage.enter_count, 1000);
    let nach = indun.dungeon_zone(55).unwrap();
    assert!(nach.party_only);
    assert_eq!(nach.item_id, 27965);
    assert_eq!(nach.enter_count, 3);
    assert_eq!(d.battlefields.battlefields.len(), 7);
    let arena = d.battlefields.battlefield(11).unwrap();
    assert_eq!(arena.zone_key, 302);
    assert_eq!(arena.rule_set.unwrap().id, 28);
    assert!(arena.spawns.unwrap().corps1_spawn.is_some());
    let camp = d.battlefields.battlefield(7).unwrap();
    assert_eq!(camp.rule_set.unwrap().corps_size, 5);
    assert_eq!(camp.spawns.unwrap().corps2_spawn.unwrap().x, 1229.0);
    assert!(!d.tower_defs.tower_defs.is_empty());
    assert!(d.tower_defs.tower_defs[&12].target_npc_spawn_id == 13638);
    assert_eq!(d.zone_order.zone_keys_in_group(45), vec![236]);
    assert_eq!(d.zone_order.zone_keys_in_group(31), vec![198]);
}
