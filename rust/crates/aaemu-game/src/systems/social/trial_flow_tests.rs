//! Crime / trial flow tests (several connections, C->S packets through `handle_frame`).

use std::sync::{Arc, Mutex};

use aaemu_commons::PacketStream;

use crate::data::skills::{BuffKind, BuffTemplate};
use crate::data::world_data::SystemFaction;
use crate::data::GameData;
use crate::models::doodad::{doodad_owner_type, Doodad};
use crate::net::packet::{encode_raw, LEVEL_PLAIN};
use crate::opcodes::{cs, sc};
use crate::server::GameServer;
use crate::systems::social::config::{CourtRoomConfig, JailConfig, JusticeConfig, SpawnPosition};
use crate::systems::social::crime::{buff, crime_kind, CrimeEvent};
use crate::systems::social::hooks::{EvidenceFunc, SocialHooks};
use crate::systems::social::packets::body;
use crate::systems::social::packets::trial::*;
use crate::systems::social::trial::{self, sentence_result, trial_step};
use crate::systems::social::util;
use crate::test_support::*;
use crate::world::{DoodadRef, Entity, PlayerRef};

const NUIA: u32 = 148;
const HARANYA: u32 = 149;
const JAIL_POS: SpawnPosition = SpawnPosition {
    world_id: 0,
    zone_id: TEST_ZONE,
    x: 300.0,
    y: 310.0,
    z: 50.0,
    yaw: 90.0,
};
const HOLDING_CELL: SpawnPosition = SpawnPosition {
    world_id: 0,
    zone_id: TEST_ZONE,
    x: 200.0,
    y: 210.0,
    z: 60.0,
    yaw: 180.0,
};
const DEFENDANT_POS: SpawnPosition = SpawnPosition {
    world_id: 0,
    zone_id: TEST_ZONE,
    x: 150.0,
    y: 160.0,
    z: 70.0,
    yaw: -28.0,
};

/// A test server with the trial buffs, the Nuia / Haranya factions, one Nuian court room
/// (id 1) and one Nuian jail.
fn justice_server() -> Arc<GameServer> {
    justice_server_with(|_| {})
}

fn justice_server_with(configure: impl FnOnce(&mut JusticeConfig)) -> Arc<GameServer> {
    let mut data = GameData::empty();
    for id in [
        buff::PRISONER_NUIAN,
        buff::PRISONER_HARANYAN,
        buff::FORCIBLY_AWAITING_TRIAL,
        buff::JURY,
        buff::TRIAL_DEFENDANT,
        buff::WANTED,
        4832,
        buff::SUSPECTED_USER,
        buff::TRANSFORMING_INTO_PRIME_SUSPECT,
        buff::COURT_HOUSE,
        buff::CANNOT_ESCAPE,
    ] {
        let mut t = BuffTemplate::simple(id, BuffKind::Good);
        t.duration = 1000;
        data.skills.buffs.insert(id, t);
    }
    for (id, mother) in [
        (101, NUIA),
        (102, HARANYA),
        (NUIA, 0),
        (HARANYA, 0),
        (161, 0),
    ] {
        data.factions.add(SystemFaction {
            id,
            mother_id: mother,
            ..SystemFaction::default()
        });
    }
    let s = test_server_with(data);
    let mut justice = JusticeConfig {
        court_rooms: vec![CourtRoomConfig {
            id: 1,
            name: "Nuian Main Court".into(),
            faction: NUIA,
            defendant: DEFENDANT_POS,
            holding_cell: HOLDING_CELL,
        }],
        jails: vec![JailConfig {
            id: 1,
            name: "Nuian Jail".into(),
            faction: NUIA,
            pos: JAIL_POS,
        }],
        ..JusticeConfig::default()
    };
    configure(&mut justice);
    s.config.update(|c| c.justice = justice);
    let s = Arc::new(s);
    s.set_self();
    s.systems.social.trials.load(&s);
    s
}

struct Pc {
    client: TestClient,
    p: PlayerRef,
}

fn player(
    server: &GameServer,
    id: u32,
    name: &str,
    f: impl FnOnce(&mut crate::models::character::Character),
) -> Pc {
    let mut client = TestClient::new(server, id, 1000 + id);
    let mut c = test_character(id, name, 100.0 + id as f32, 100.0);
    c.is_online = true;
    f(&mut c);
    let p = spawn_player(server, &client, c, 0x100 + id);
    client.conn.active_char = Some(p.clone());
    server.names.add_character(id, name, 1000 + id);
    client.drain();
    Pc { client, p }
}

async fn send(server: &GameServer, pc: &mut Pc, opcode: u16, f: impl FnOnce(&mut PacketStream)) {
    let mut s = PacketStream::new();
    f(&mut s);
    server
        .handle_frame(
            &mut pc.client.conn,
            &encode_raw(LEVEL_PLAIN, opcode, &s.into_bytes()),
        )
        .await
        .expect("handled");
}

fn ops(frames: &[(u16, Vec<u8>)]) -> Vec<u16> {
    frames.iter().map(|f| f.0).collect()
}

// Justice teleports also replicate entering/leaving regions. Check the trial protocol here;
// server position and region membership have dedicated regressions below.
fn trial_frames(client: &mut TestClient) -> Vec<(u16, Vec<u8>)> {
    let mut arrest_points = false;
    client
        .drain()
        .into_iter()
        .filter(|(op, _)| {
            if *op == sc::SCCharacterResurrectedPacket {
                arrest_points = true;
            }
            if *op == sc::SCUnitPointsPacket {
                // Arrest explicitly sends points after resurrecting. Region replication
                // additionally sends nearby characters' points when a teleport arrives.
                return std::mem::take(&mut arrest_points);
            }
            !matches!(
                *op,
                sc::SCUnitsRemovedPacket
                    | sc::SCUnitStatePacket
                    | sc::SCCharacterStatePacket
                    | sc::SCTargetChangedPacket
                    | sc::SCDoodadsCreatedPacket
                    | sc::SCDoodadsRemovedPacket
                    | sc::SCGimmicksCreatedPacket
                    | sc::SCGimmicksRemovedPacket
            )
        })
        .collect()
}

fn find(frames: &[(u16, Vec<u8>)], op: u16) -> Vec<Vec<u8>> {
    frames
        .iter()
        .filter(|f| f.0 == op)
        .map(|f| f.1.clone())
        .collect()
}

fn seat(id: u32, x: f32) -> DoodadRef {
    let mut d = Doodad::new(4937);
    d.transform.set_position(x, 500.0, 20.0, 0.0, 0.0, 1.25);
    Entity::new(id, d)
}

fn murder(id: u32, criminal: u32, victim: u32) -> CrimeEvent {
    CrimeEvent {
        id,
        criminal,
        victim,
        reporter: victim,
        crime_kind: crime_kind::MURDER,
        position: (10.0, 20.0, 30.0),
        report_time: 1_700_000_000,
        msg: "bloodstain".into(),
        ..CrimeEvent::default()
    }
}

fn state_bytes(trial_id: u32, state: u8, cur_jury: i32) -> Vec<u8> {
    // The remaining time is checked separately.
    let mut b = body(&SCChangeTrialStatePacket {
        trial_id,
        state,
        cur_jury,
        remain_time: 0,
    });
    b.truncate(9);
    b
}

fn states(frames: &[(u16, Vec<u8>)]) -> Vec<(u8, i32)> {
    find(frames, sc::SCChangeTrialStatePacket)
        .iter()
        .map(|b| (b[4], i32::from_le_bytes([b[5], b[6], b[7], b[8]])))
        .collect()
}

#[tokio::test]
async fn jury_trial_flow_to_guilty_verdict_and_cleanup() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let (s1, s2) = (seat(0x900, 400.0), seat(0x901, 402.0));
    trials.initialize_doodads(&[
        ("justice.1.seat.1".into(), s1.clone()),
        ("justice.1.seat.2".into(), s2.clone()),
        ("justice.2.seat.1".into(), seat(0x902, 0.0)),
    ]);
    trials.initialize_npc(&[("justice.1.judge".into(), 55)]);
    assert_eq!(trials.court_rooms()[0].jury_seats.len(), 2);
    assert_eq!(trials.court_rooms()[0].judge_spawner, Some(55));

    let mut d = player(&server, 1, "Crook", |c| {
        c.crime_point = 60;
        c.infamy_point = 60;
    });
    let mut j1 = player(&server, 2, "Juror1", |c| c.jury_point = 1);
    let mut j2 = player(&server, 3, "Juror2", |c| c.jury_point = 1);
    let mut arrestor = player(&server, 4, "Guard", |_| {});
    for pc in [&mut d, &mut j1, &mut j2, &mut arrestor] {
        trial_frames(&mut pc.client);
    }
    // Murder of a level-10 victim: 20 * 10 minutes.
    assert!(server.systems.social.crimes.insert(murder(50, 1, 2)));

    // Jury queue: eligible players of the mother faction (the criminal and the guard are not).
    // (`GetAllCharacters` order is unspecified, as in C#.)
    trials.update_jury_queues(&server);
    let mut queue = trials.jury_queue(NUIA);
    let pos = queue.iter().position(|x| *x == 3).unwrap() as u8 + 1;
    queue.sort_unstable();
    assert_eq!(queue, vec![2, 3]);
    send(
        &server,
        &mut j2,
        cs::CSRequestJuryWaitingNumberPacket,
        |_| {},
    )
    .await;
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCJuryWaitingNumberPacket),
        vec![vec![pos, 0, 0, 0]]
    );

    // Arrest.
    let tid = trials
        .arrest_criminal(&server, &d.p, Some(&arrestor.p))
        .expect("trial");
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCCriminalArrestedPacket,
            sc::SCCharacterResurrectedPacket,
            sc::SCUnitPointsPacket,
            sc::SCTeleportUnitPacket,
            sc::SCAskImprisonOrTrialPacket,
            sc::SCTrialWaitStatusPacket,
        ]
    );
    assert_eq!(
        f[0].1,
        body(&SCCriminalArrestedPacket {
            criminal_obj_id: 0x101,
            criminal: "Crook".into(),
            arrestor: "Guard".into()
        })
    );
    assert_eq!(
        f[3].1,
        body(&SCTeleportUnitPacket {
            reason: teleport_reason::LOCKUP,
            error_message: 0,
            x: 200.0,
            y: 210.0,
            z: 60.0,
            z2: std::f32::consts::PI / 180.0 * 180.0,
        })
    );
    assert_eq!(
        f[4].1,
        body(&SCAskImprisonOrTrialPacket {
            crime_value: 60,
            jail_minutes: 200
        })
    );
    assert_eq!(
        f[5].1,
        body(&SCTrialWaitStatusPacket {
            order: 0,
            sentence_time_ms: 200 * 60_000
        })
    );
    // Neighbours see the arrest.
    assert_eq!(
        find(
            &trial_frames(&mut arrestor.client),
            sc::SCCriminalArrestedPacket
        )
        .len(),
        1
    );
    {
        let c = d.p.data();
        assert_eq!(c.arrest_count, 1);
        // CreateTrialCase runs CalculateJailTime before EnterCourtJail: region = Nuian (0).
        assert_eq!((c.offline_guilty_time, c.offline_guilty_region), (200, 0));
    }
    assert!(trials.is_player_in_court(1));

    // The defendant asks for a trial; the next tick moves the case into the court room.
    send(&server, &mut d, cs::CSReplyImprisonOrTrialPacket, |s| {
        s.write_bool(true);
    })
    .await;
    assert_eq!(d.p.data().accept_trial_count, 1);
    let t0 = util::now_ms() + 1;
    trials.update_tick(&server, t0).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCTeleportUnitPacket,
            sc::SCSummonDefendantPacket,
            sc::SCJoinedChatChannelPacket,
            sc::SCJuryWaitStatusPacket,
            sc::SCChangeTrialStatePacket,
            sc::SCTrialInfoPacket,
            sc::SCCrimeRecordsPacket,
            sc::SCCrimeDataPacket,
            sc::SCChangeTrialStatePacket,
            sc::SCForceAttackSetPacket,
        ]
    );
    assert_eq!(
        f[3].1,
        body(&SCJuryWaitStatusPacket {
            count: 0,
            total: 2,
            sentence_time_ms: 200 * 60_000
        })
    );
    assert_eq!(
        f[4].1,
        body(&SCChangeTrialStatePacket {
            trial_id: tid,
            state: trial_step::VERIFY_CRIMINAL_RECORD,
            cur_jury: 0,
            remain_time: 10_000
        })
    );
    assert_eq!(
        f[8].1,
        body(&SCChangeTrialStatePacket {
            trial_id: tid,
            state: trial_step::AWAITING_JURY_SUMMONS,
            cur_jury: 0,
            remain_time: 120_000
        })
    );
    // The crime record carries the online victim's values and the reporter's name.
    let entry = server
        .systems
        .social
        .crimes
        .record_entry(&server, &murder(50, 1, 2));
    assert_eq!(entry.victim_name, "Juror1");
    assert_eq!(entry.victim_faction, 101);
    assert_eq!(entry.reporter_name.as_deref(), Some("Juror1"));
    assert_eq!(
        f[6].1,
        body(&SCCrimeRecordsPacket {
            trial_id: tid,
            unknown1: 0,
            total_count: 1,
            this_count: 1,
            entries: vec![entry]
        })
    );
    // The judge spawner is registered but no npcs hook resolves it: obj id 0.
    assert_eq!(
        f[7].1,
        body(&SCCrimeDataPacket {
            defendant_id: 1,
            defendant_name: "Crook".into(),
            defendant_race: 1,
            defendant_faction: 101,
            trial_id: tid,
            sentence_time_ms: 200 * 60_000,
            judge_id: 0
        })
    );
    assert!(d.p.data().unit.buffs.check_buff(buff::TRIAL_DEFENDANT));
    assert!(d.p.data().unit.buffs.check_buff(buff::CANNOT_ESCAPE));
    let invite = body(&SCInviteJuryPacket {
        defendant_name: "Crook".into(),
        trial: tid,
    });
    assert_eq!(
        find(&trial_frames(&mut j1.client), sc::SCInviteJuryPacket),
        vec![invite.clone()]
    );
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCInviteJuryPacket),
        vec![invite]
    );
    assert!(trial_frames(&mut arrestor.client).is_empty());
    assert_eq!(
        trial::trial_chat(&server, &d.p).map(|c| c.internal_name()),
        Some("Nuian Court".into())
    );

    // Juror 1 accepts.
    send(&server, &mut j1, cs::CSReplyInviteJuryPacket, |s| {
        s.write_bool(true).write_u32(tid);
    })
    .await;
    let f = trial_frames(&mut j1.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCSummonJuryPacket,
            sc::SCTeleportUnitPacket,
            sc::SCJuryBeSeatedPacket,
            sc::SCJoinedChatChannelPacket,
            sc::SCTrialInfoPacket,
            sc::SCCrimeRecordsPacket,
            sc::SCCrimeDataPacket,
            sc::SCJuryWaitStatusPacket,
        ]
    );
    assert_eq!(
        f[0].1,
        body(&SCSummonJuryPacket {
            trial: tid,
            court: 1,
            jury_number: 1
        })
    );
    assert_eq!(
        f[1].1,
        body(&SCTeleportUnitPacket {
            reason: teleport_reason::JURY,
            error_message: 0,
            x: 400.0,
            y: 500.0,
            z: 20.0,
            z2: 1.25
        })
    );
    assert_eq!(
        f[2].1,
        body(&SCJuryBeSeatedPacket {
            is_west: false,
            trial: tid,
            court: 1,
            jury_number: 1
        })
    );
    assert_eq!(
        f[7].1,
        body(&SCJuryWaitStatusPacket {
            count: 1,
            total: 2,
            sentence_time_ms: 200 * 60_000
        })
    );
    {
        let c = j1.p.data();
        let m = c.main_world_position.expect("main world position");
        assert_eq!((m.zone_id, m.x, m.y, m.z), (TEST_ZONE, 102.0, 100.0, 100.0));
        assert_eq!(
            (c.unit.transform.x, c.unit.transform.y, c.unit.transform.z),
            (400.0, 500.0, 20.0)
        );
        assert!(c
            .unit
            .buffs
            .check_buffs(&[buff::COURT_HOUSE, buff::JURY, buff::CANNOT_ESCAPE]));
    }
    assert_eq!(
        find(&trial_frames(&mut d.client), sc::SCJuryWaitStatusPacket),
        vec![body(&SCJuryWaitStatusPacket {
            count: 1,
            total: 2,
            sentence_time_ms: 200 * 60_000
        })]
    );
    assert_eq!(trials.jury_queue(NUIA), vec![3]);
    // A second reply is refused: already a jury member.
    send(&server, &mut j1, cs::CSReplyInviteJuryPacket, |s| {
        s.write_bool(true).write_u32(tid);
    })
    .await;
    assert_eq!(
        ops(&trial_frames(&mut j1.client)),
        vec![sc::SCErrorMsgPacket]
    );

    // Juror 2 accepts: all seats taken -> the summons step ends now.
    send(&server, &mut j2, cs::CSReplyInviteJuryPacket, |s| {
        s.write_bool(true).write_u32(tid);
    })
    .await;
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCJuryBeSeatedPacket),
        vec![body(&SCJuryBeSeatedPacket {
            is_west: false,
            trial: tid,
            court: 1,
            jury_number: 2
        })]
    );
    for pc in [&mut d, &mut j1] {
        assert_eq!(
            find(&trial_frames(&mut pc.client), sc::SCJuryWaitStatusPacket),
            vec![body(&SCJuryWaitStatusPacket {
                count: 2,
                total: 2,
                sentence_time_ms: 200 * 60_000
            })]
        );
    }
    assert!(trials.get_trial(tid).unwrap().current_step_end_ms <= util::now_ms());

    // Tick: ConfirmCriminalRecord (judge message + state to everyone).
    let t1 = util::now_ms() + 1;
    trials.update_tick(&server, t1).await;
    for pc in [&mut d, &mut j1, &mut j2] {
        let f = trial_frames(&mut pc.client);
        assert_eq!(
            ops(&f),
            vec![sc::SCChatMessagePacket, sc::SCChangeTrialStatePacket]
        );
        assert_eq!(
            &f[1].1[..9],
            state_bytes(tid, trial_step::CONFIRM_CRIMINAL_RECORD, 2)
        );
        assert_eq!(
            u32::from_le_bytes([f[1].1[9], f[1].1[10], f[1].1[11], f[1].1[12]]),
            300_000
        );
    }
    // Jury members are no longer queued (they take part in a trial).
    assert!(trials.jury_queue(NUIA).is_empty());

    // Both jurors confirm the records.
    send(&server, &mut j1, cs::CSJuryEndTestimonyPacket, |s| {
        s.write_u32(tid).write_i32(1);
    })
    .await;
    send(&server, &mut j2, cs::CSJuryEndTestimonyPacket, |s| {
        s.write_u32(tid).write_i32(2);
    })
    .await;
    assert_eq!(
        find(&trial_frames(&mut d.client), sc::SCChangeJuryOkCountPacket),
        vec![
            body(&SCChangeJuryOkCountPacket { count: 1, total: 2 }),
            body(&SCChangeJuryOkCountPacket { count: 2, total: 2 })
        ]
    );
    trial_frames(&mut j1.client);
    trial_frames(&mut j2.client);

    // Tick: ClosingStatement; the defendant skips it.
    trials.update_tick(&server, util::now_ms() + 1).await;
    assert_eq!(
        states(&trial_frames(&mut d.client)),
        vec![(trial_step::CLOSING_STATEMENT, 2)]
    );
    send(&server, &mut d, cs::CSSkipFinalStatementPacket, |s| {
        s.write_u32(tid);
    })
    .await;

    // Tick: JuryVerdict (+ the default ruling panel for the defendant).
    trials.update_tick(&server, util::now_ms() + 1).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![sc::SCChangeTrialStatePacket, sc::SCRulingStatusPacket]
    );
    assert_eq!(&f[0].1[..9], state_bytes(tid, trial_step::JURY_VERDICT, 2));
    assert_eq!(
        f[1].1,
        body(&SCRulingStatusPacket {
            count: 0,
            total: 2,
            sentence_type: 0,
            sentence_time: 0
        })
    );
    trial_frames(&mut j1.client);
    trial_frames(&mut j2.client);

    // Votes: guilty (5 = Guilty4, 100% of the jail time).
    send(&server, &mut j1, cs::CSJuryVerdictPacket, |s| {
        s.write_u32(tid).write_i32(1).write_u8(5);
    })
    .await;
    // Seat 2 voted by the wrong player is ignored.
    send(&server, &mut j1, cs::CSJuryVerdictPacket, |s| {
        s.write_u32(tid).write_i32(2).write_u8(0);
    })
    .await;
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCRulingStatusPacket),
        Vec::<Vec<u8>>::new()
    );
    send(&server, &mut j2, cs::CSJuryVerdictPacket, |s| {
        s.write_u32(tid).write_i32(2).write_u8(5);
    })
    .await;
    let ruling = |count| {
        body(&SCRulingStatusPacket {
            count,
            total: 2,
            sentence_type: 0,
            sentence_time: 0,
        })
    };
    assert_eq!(
        find(&trial_frames(&mut d.client), sc::SCRulingStatusPacket),
        vec![ruling(1), ruling(2)]
    );
    assert_eq!(
        find(&trial_frames(&mut j1.client), sc::SCRulingStatusPacket),
        vec![ruling(1), ruling(2)]
    );
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCRulingStatusPacket),
        vec![ruling(2)]
    );

    // Tick: EndTrial, verdict and the guilty result.
    let t_end = util::now_ms() + 1;
    trials.update_tick(&server, t_end).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCRulingStatusPacket,
            sc::SCChangeTrialStatePacket,
            sc::SCLeavedChatChannelPacket,
            sc::SCCrimeChangedPacket,
            sc::SCTeleportUnitPacket,
        ]
    );
    assert_eq!(
        f[0].1,
        body(&SCRulingStatusPacket {
            count: 2,
            total: 2,
            sentence_type: sentence_result::GUILTY4,
            sentence_time: 200 * 60_000
        })
    );
    assert_eq!(
        f[1].1,
        body(&SCChangeTrialStatePacket {
            trial_id: tid,
            state: trial_step::END_TRIAL,
            cur_jury: 2,
            remain_time: 30_000
        })
    );
    // Wanted is gone after the crime points were cleared: crime state 0.
    assert_eq!(
        f[3].1,
        body(&SCCrimeChangedPacket {
            points: 60,
            crime_points: 0,
            infamy_points: 60,
            crime_state: 0
        })
    );
    // Justice teleports consistently use radians.
    assert_eq!(
        f[4].1,
        body(&SCTeleportUnitPacket {
            reason: teleport_reason::JAIL,
            error_message: 0,
            x: 300.0,
            y: 310.0,
            z: 50.0,
            z2: std::f32::consts::FRAC_PI_2
        })
    );
    {
        let c = d.p.data();
        assert_eq!((c.guilty_count, c.crime_point), (1, 0));
        assert_eq!((c.offline_guilty_time, c.offline_guilty_region), (0, 255));
        assert!(c.disabled_set_position);
        let prisoner = c
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .expect("prisoner buff");
        assert_eq!(prisoner.duration, 200 * 60_000);
        assert!(!c.unit.buffs.check_buff(buff::TRIAL_DEFENDANT));
    }
    // The evidence is archived (KeepHistory).
    assert!(server
        .systems
        .social
        .crimes
        .crimes_of_player(1, false)
        .is_empty());
    assert!(server.systems.social.crimes.get(50).unwrap().judgement_time > 0);
    for pc in [&mut j1, &mut j2] {
        let f = trial_frames(&mut pc.client);
        assert_eq!(
            ops(&f),
            vec![sc::SCRulingStatusPacket, sc::SCChangeTrialStatePacket]
        );
        assert!(!pc.p.data().unit.buffs.check_buff(buff::JURY));
    }

    // 30 s later: ruling closed, the jury returns home, the trial is removed.
    trials.update_tick(&server, t_end + 31_000).await;
    assert_eq!(
        ops(&trial_frames(&mut d.client)),
        vec![sc::SCRulingClosedPacket]
    );
    let f = trial_frames(&mut j1.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCRulingClosedPacket,
            sc::SCTeleportUnitPacket,
            sc::SCJuryPointChangedPacket,
            sc::SCLeavedChatChannelPacket,
        ]
    );
    assert_eq!(
        f[1].1,
        body(&SCTeleportUnitPacket {
            reason: teleport_reason::JURY,
            error_message: 0,
            x: 102.0,
            y: 100.0,
            z: 100.0,
            z2: 0.0
        })
    );
    assert_eq!(f[2].1, body(&SCJuryPointChangedPacket { jury_point: 2 }));
    {
        let c = j1.p.data();
        assert_eq!(c.main_world_position, None);
        assert!(c.disabled_set_position);
        assert!(!c.unit.buffs.check_buff(buff::COURT_HOUSE));
    }
    assert!(trials.trial_ids().is_empty());
    assert!(trials.court_rooms()[0].current_trial.is_none());
    assert!(!trials.is_player_in_court(1));
    assert!(!trials.is_player_in_court(2));
    // A late reply is refused.
    send(&server, &mut j2, cs::CSReplyInviteJuryPacket, |s| {
        s.write_bool(true).write_u32(tid);
    })
    .await;
    assert_eq!(
        find(&trial_frames(&mut j2.client), sc::SCErrorMsgPacket).len(),
        1
    );
}

#[tokio::test]
async fn plead_guilty_path() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let mut d = player(&server, 1, "Crook", |c| {
        c.crime_point = 55;
        c.infamy_point = 100;
    });
    assert!(server.systems.social.crimes.insert(CrimeEvent {
        crime_kind: crime_kind::THEFT,
        ..murder(7, 1, 99)
    }));
    // No arrestor: the region comes from OfflineGuiltyRegion (0 = Nuian).
    let tid = trials.arrest_criminal(&server, &d.p, None).expect("trial");
    let f = trial_frames(&mut d.client);
    assert_eq!(f[0].0, sc::SCCharacterResurrectedPacket);
    // Theft of an offline victim: 8 minutes.
    assert_eq!(
        find(&f, sc::SCAskImprisonOrTrialPacket),
        vec![body(&SCAskImprisonOrTrialPacket {
            crime_value: 55,
            jail_minutes: 8
        })]
    );
    assert_eq!(d.p.data().arrest_count, 0);

    send(&server, &mut d, cs::CSReplyImprisonOrTrialPacket, |s| {
        s.write_bool(false);
    })
    .await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCChangeTrialStatePacket,
            sc::SCCrimeChangedPacket,
            sc::SCTeleportUnitPacket
        ]
    );
    assert_eq!(&f[0].1[..9], state_bytes(tid, trial_step::PLEAD_GUILTY, 0));
    assert_eq!(
        f[1].1,
        body(&SCCrimeChangedPacket {
            points: 55,
            crime_points: 0,
            infamy_points: 100,
            crime_state: 0
        })
    );
    {
        let c = d.p.data();
        assert_eq!(c.accept_guilty_count, 1);
        assert_eq!(
            c.unit
                .buffs
                .effect_from_buff_id(buff::PRISONER_NUIAN)
                .unwrap()
                .duration,
            8 * 60_000
        );
    }
    trials.update_tick(&server, util::now_ms()).await;
    assert_eq!(
        ops(&trial_frames(&mut d.client)),
        vec![sc::SCRulingClosedPacket]
    );
    assert!(trials.trial_ids().is_empty());
    // CSCancelTrial on a finished trial: the C# NRE is logged.
    send(&server, &mut d, cs::CSCancelTrialPacket, |s| {
        s.write_u32(tid);
    })
    .await;
    assert!(trial_frames(&mut d.client).is_empty());
}

#[tokio::test]
async fn not_guilty_verdict_with_one_juror() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    trials.initialize_doodads(&[("justice.1.seat.1".into(), seat(0x900, 400.0))]);
    let mut d = player(&server, 1, "Crook", |c| {
        c.crime_point = 70;
        c.infamy_point = 500;
    });
    let mut j = player(&server, 2, "Juror", |c| c.jury_point = 3);
    trials.update_jury_queues(&server);
    let tid = trials.arrest_criminal(&server, &d.p, None).unwrap();
    trials.reply_imprison_or_trial(&server, &d.p, true, util::now_ms());
    trials.update_tick(&server, util::now_ms() + 1).await;
    assert!(trials.process_trial_invite_reply(&server, &j.p, true, tid, util::now_ms()));
    trials.update_tick(&server, util::now_ms() + 1).await; // ConfirmCriminalRecord
    trials.jury_end_testimony(&j.p, tid, 1, util::now_ms());
    trials.update_tick(&server, util::now_ms() + 1).await; // ClosingStatement
                                                           // Not skipped: the closing statement runs its minute.
    trials.update_tick(&server, util::now_ms() + 61_000).await; // JuryVerdict
    trial_frames(&mut d.client);
    trial_frames(&mut j.client);
    trials.jury_verdict(&j.p, tid, 1, 1, util::now_ms());
    let t = util::now_ms() + 1;
    trials.update_tick(&server, t).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCRulingStatusPacket, // the juror's vote count
            sc::SCRulingStatusPacket,
            sc::SCChangeTrialStatePacket,
            sc::SCCrimeChangedPacket,
        ]
    );
    assert_eq!(
        f[1].1,
        body(&SCRulingStatusPacket {
            count: 1,
            total: 1,
            sentence_type: sentence_result::NOT_GUILTY,
            sentence_time: 0
        })
    );
    assert_eq!(
        f[3].1,
        body(&SCCrimeChangedPacket {
            points: 70,
            crime_points: 0,
            infamy_points: 430,
            crime_state: 0
        })
    );
    assert_eq!(d.p.data().not_guilty_count, 1);
    assert!(!d.p.data().unit.buffs.check_buff(buff::TRIAL_DEFENDANT));
    // The defendant stays in the court chat until the cleanup.
    trials.update_tick(&server, t + 31_000).await;
    assert_eq!(
        ops(&trial_frames(&mut d.client)),
        vec![sc::SCRulingClosedPacket, sc::SCLeavedChatChannelPacket]
    );
    assert_eq!(j.p.data().jury_point, 4);
    assert!(trials.trial_ids().is_empty());
}

#[tokio::test]
async fn trial_without_jury_is_cancelled_with_the_default_sentence() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    trials.initialize_doodads(&[("justice.1.seat.1".into(), seat(0x900, 400.0))]);
    let mut d = player(&server, 1, "Crook", |c| c.crime_point = 50);
    let tid = trials.arrest_criminal(&server, &d.p, None).unwrap();
    trials.reply_imprison_or_trial(&server, &d.p, true, util::now_ms());
    // Nobody queued: the summons wait is skipped in the same tick.
    trials.update_tick(&server, util::now_ms() + 1).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        states(&f),
        vec![
            (trial_step::VERIFY_CRIMINAL_RECORD, 0),
            (trial_step::AWAITING_JURY_SUMMONS, 0),
            (trial_step::CONFIRM_CRIMINAL_RECORD, 0)
        ]
    );
    // Next tick: no active jury -> cancelled -> guilty with the default sentence.
    trials.update_tick(&server, util::now_ms() + 1).await;
    let f = trial_frames(&mut d.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCChangeTrialStatePacket,
            sc::SCLeavedChatChannelPacket,
            sc::SCCrimeChangedPacket,
            sc::SCTeleportUnitPacket,
            sc::SCRulingClosedPacket,
        ]
    );
    assert_eq!(&f[0].1[..9], state_bytes(tid, trial_step::END_TRIAL, 0));
    // No evidence: 0 minutes -> the 10 s minimum.
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        10_000
    );
    assert!(trials.trial_ids().is_empty());
}

#[tokio::test]
async fn login_with_offline_guilty_time_goes_to_jail() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let d = player(&server, 1, "Escapee", |c| {
        c.jury_point = 1;
        c.offline_guilty_time = 12;
        c.offline_guilty_region = 0;
    });
    let mut d = d;
    trial::handle_player_login(&server, &d.p);
    assert!(trials.jury_queue(NUIA).is_empty());
    let f = trial_frames(&mut d.client);
    assert!(ops(&f).contains(&sc::SCTeleportUnitPacket));
    assert_eq!(d.p.data().offline_guilty_time, 0);
    assert_eq!(d.p.data().guilty_count, 1);
    assert!(d.p.data().unit.buffs.check_buff(buff::PRISONER_NUIAN));

    // Jail enforcement also applies without jury eligibility.
    let mut e = player(&server, 2, "Other", |c| c.offline_guilty_time = 12);
    trial::handle_player_login(&server, &e.p);
    assert!(ops(&trial_frames(&mut e.client)).contains(&sc::SCTeleportUnitPacket));
    assert_eq!(e.p.data().offline_guilty_time, 0);
    assert_eq!(
        e.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        12 * 60_000
    );
}

async fn summoned_trial(count: u32) -> (Arc<GameServer>, Pc, Vec<Pc>, u32) {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let links: Vec<_> = (1..=count)
        .map(|i| {
            (
                format!("justice.1.seat.{i}"),
                seat(0x900 + i, 400.0 + i as f32),
            )
        })
        .collect();
    trials.initialize_doodads(&links);
    let mut defendant = player(&server, 1, "Defendant", |c| {
        c.crime_point = 50;
        c.infamy_point = 50;
    });
    let mut jurors: Vec<_> = (0..count)
        .map(|i| player(&server, i + 2, &format!("Juror{i}"), |c| c.jury_point = 1))
        .collect();
    server.systems.social.crimes.insert(murder(50, 1, 99)); // offline victim: 20 minutes
    trials.update_jury_queues(&server);
    let tid = trials.arrest_criminal(&server, &defendant.p, None).unwrap();
    send(
        &server,
        &mut defendant,
        cs::CSReplyImprisonOrTrialPacket,
        |s| {
            s.write_bool(true);
        },
    )
    .await;
    trials.update_tick(&server, util::now_ms() + 1).await;
    for juror in &mut jurors {
        send(&server, juror, cs::CSReplyInviteJuryPacket, |s| {
            s.write_bool(true).write_u32(tid);
        })
        .await;
    }
    trials.update_tick(&server, util::now_ms() + 1).await;
    assert_eq!(
        trials.get_trial(tid).unwrap().step,
        trial_step::CONFIRM_CRIMINAL_RECORD
    );
    defendant.client.drain();
    for juror in &mut jurors {
        juror.client.drain();
    }
    (server, defendant, jurors, tid)
}

async fn open_verdict(server: &GameServer, defendant: &mut Pc, jurors: &mut [Pc], tid: u32) {
    for (i, juror) in jurors.iter_mut().enumerate() {
        send(server, juror, cs::CSJuryEndTestimonyPacket, |s| {
            s.write_u32(tid).write_i32(i as i32 + 1);
        })
        .await;
    }
    server
        .systems
        .social
        .trials
        .update_tick(server, util::now_ms() + 1)
        .await;
    send(server, defendant, cs::CSSkipFinalStatementPacket, |s| {
        s.write_u32(tid);
    })
    .await;
    server
        .systems
        .social
        .trials
        .update_tick(server, util::now_ms() + 1)
        .await;
    assert_eq!(
        server.systems.social.trials.get_trial(tid).unwrap().step,
        trial_step::JURY_VERDICT
    );
    defendant.client.drain();
    for juror in jurors {
        juror.client.drain();
    }
}

#[tokio::test]
async fn jury_can_only_confirm_its_own_seat_once() {
    let (server, mut d, mut jury, tid) = summoned_trial(2).await;
    for caller in [&mut d, &mut jury[0]] {
        send(&server, caller, cs::CSJuryEndTestimonyPacket, |s| {
            s.write_u32(tid).write_i32(2);
        })
        .await;
    }
    assert!(!server.systems.social.trials.get_trial(tid).unwrap().jury[&2].confirm_testimony);
    send(&server, &mut jury[0], cs::CSJuryEndTestimonyPacket, |s| {
        s.write_u32(tid).write_i32(1);
    })
    .await;
    assert_eq!(
        find(&d.client.drain(), sc::SCChangeJuryOkCountPacket),
        vec![body(&SCChangeJuryOkCountPacket { count: 1, total: 2 })]
    );
    send(&server, &mut jury[0], cs::CSJuryEndTestimonyPacket, |s| {
        s.write_u32(tid).write_i32(1);
    })
    .await;
    assert!(find(&d.client.drain(), sc::SCChangeJuryOkCountPacket).is_empty());
}

#[tokio::test]
async fn invalid_spoofed_and_repeated_votes_cannot_change_the_verdict() {
    let (server, mut d, mut jury, tid) = summoned_trial(1).await;
    open_verdict(&server, &mut d, &mut jury, tid).await;
    for sentence in [0, 7, 255] {
        send(&server, &mut jury[0], cs::CSJuryVerdictPacket, |s| {
            s.write_u32(tid).write_i32(1).write_u8(sentence);
        })
        .await;
    }
    send(&server, &mut d, cs::CSJuryVerdictPacket, |s| {
        s.write_u32(tid).write_i32(1).write_u8(1);
    })
    .await;
    assert_eq!(
        server.systems.social.trials.get_trial(tid).unwrap().jury[&1].selected_sentence,
        -1
    );
    for sentence in [5, 1] {
        send(&server, &mut jury[0], cs::CSJuryVerdictPacket, |s| {
            s.write_u32(tid).write_i32(1).write_u8(sentence);
        })
        .await;
    }
    server
        .systems
        .social
        .trials
        .update_tick(&server, util::now_ms() + 1)
        .await;
    assert_eq!(d.p.data().guilty_count, 1);
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        20 * 60_000
    );
    for op in [cs::CSReplyImprisonOrTrialPacket, cs::CSCancelTrialPacket] {
        send(&server, &mut d, op, |s| {
            if op == cs::CSCancelTrialPacket {
                s.write_u32(tid);
            } else {
                s.write_bool(false);
            }
        })
        .await;
    }
    assert_eq!(d.p.data().accept_guilty_count, 0);
    assert_eq!(d.p.data().guilty_count, 1);
}

#[tokio::test]
async fn all_six_jury_choices_apply_the_expected_sentence() {
    for (sentence, minutes) in [(1, 0), (2, 4), (3, 10), (4, 16), (5, 20), (6, 24)] {
        let (server, mut d, mut jury, tid) = summoned_trial(1).await;
        open_verdict(&server, &mut d, &mut jury, tid).await;
        send(&server, &mut jury[0], cs::CSJuryVerdictPacket, |s| {
            s.write_u32(tid).write_i32(1).write_u8(sentence);
        })
        .await;
        let end = util::now_ms() + 1;
        server.systems.social.trials.update_tick(&server, end).await;
        assert_eq!(
            find(&d.client.drain(), sc::SCRulingStatusPacket).last(),
            Some(&body(&SCRulingStatusPacket {
                count: 1,
                total: 1,
                sentence_type: sentence,
                sentence_time: minutes * 60_000,
            }))
        );
        {
            let c = d.p.data();
            if minutes == 0 {
                assert_eq!(c.not_guilty_count, 1);
                assert!(!c.unit.buffs.check_buff(buff::PRISONER_NUIAN));
                assert!(!c.unit.buffs.check_buff(buff::CANNOT_ESCAPE));
            } else {
                assert_eq!(
                    c.unit
                        .buffs
                        .effect_from_buff_id(buff::PRISONER_NUIAN)
                        .unwrap()
                        .duration,
                    minutes * 60_000
                );
                assert_eq!(
                    (c.unit.transform.x, c.unit.transform.y, c.unit.transform.z),
                    (JAIL_POS.x, JAIL_POS.y, JAIL_POS.z)
                );
            }
        }
        server
            .systems
            .social
            .trials
            .update_tick(&server, end + 31_000)
            .await;
        assert!(server.systems.social.trials.trial_ids().is_empty());
        assert_eq!(jury[0].p.data().main_world_position, None);
        assert!(!jury[0].p.data().unit.buffs.check_buff(buff::CANNOT_ESCAPE));
    }
}

#[tokio::test]
async fn the_most_common_jury_sentence_wins() {
    let (server, mut d, mut jury, tid) = summoned_trial(3).await;
    open_verdict(&server, &mut d, &mut jury, tid).await;
    for (i, sentence) in [1, 5, 5].into_iter().enumerate() {
        send(&server, &mut jury[i], cs::CSJuryVerdictPacket, |s| {
            s.write_u32(tid).write_i32(i as i32 + 1).write_u8(sentence);
        })
        .await;
    }
    server
        .systems
        .social
        .trials
        .update_tick(&server, util::now_ms() + 1)
        .await;
    assert_eq!(d.p.data().guilty_count, 1);
    assert_eq!(
        server
            .systems
            .social
            .trials
            .get_trial(tid)
            .unwrap()
            .jail_time,
        20
    );
}

#[tokio::test]
async fn a_guilty_plea_in_a_holding_cell_cannot_free_an_occupied_court() {
    let (server, d, mut jury, tid) = summoned_trial(1).await;
    let trials = &server.systems.social.trials;
    let mut other = player(&server, 10, "Other", |c| c.crime_point = 50);
    let other_tid = trials.arrest_criminal(&server, &other.p, None).unwrap();
    send(&server, &mut other, cs::CSReplyImprisonOrTrialPacket, |s| {
        s.write_bool(false);
    })
    .await;
    trials.update_tick(&server, util::now_ms() + 1).await;
    assert!(trials.get_trial(other_tid).is_none());
    assert_eq!(trials.court_rooms()[0].current_trial, Some(tid));
    trials.cancel_trial(&server, &d.p, tid, util::now_ms());
    trials.update_tick(&server, util::now_ms() + 1).await;
    let c = jury[0].p.data();
    assert!(c.main_world_position.is_none());
    assert!(!c
        .unit
        .buffs
        .check_buffs(&[buff::COURT_HOUSE, buff::JURY, buff::CANNOT_ESCAPE]));
    assert_eq!(c.unit.transform.x, 102.0);
    assert_eq!(c.jury_point, 1);
    drop(c);
    assert!(ops(&jury[0].client.drain()).contains(&sc::SCRulingClosedPacket));
}

#[tokio::test]
async fn disconnecting_defendant_closes_the_case_and_enforces_the_saved_sentence() {
    let (server, d, jury, tid) = summoned_trial(1).await;
    d.p.data().is_online = false;
    super::session::on_online_changed(&server, &d.p, false);
    assert!(server.systems.social.trials.get_trial(tid).is_none());
    assert_eq!(d.p.data().offline_guilty_time, 20);
    assert_eq!(jury[0].p.data().unit.transform.x, 102.0);
    assert!(jury[0].p.data().main_world_position.is_none());
    assert_eq!(jury[0].p.data().jury_point, 1);
    assert!(server.systems.social.trials.court_rooms()[0]
        .current_trial
        .is_none());
    // NotifyInGame after reconnect must enforce the pending sentence even without jury points.
    d.p.data().is_online = true;
    trial::handle_player_login(&server, &d.p);
    assert_eq!(d.p.data().guilty_count, 1);
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        20 * 60_000
    );
    trial::handle_player_login(&server, &d.p);
    assert_eq!(d.p.data().guilty_count, 1);
}

#[tokio::test]
async fn disconnecting_a_juror_vacates_the_seat_without_awarding_attendance() {
    let (server, _d, jury, tid) = summoned_trial(2).await;
    jury[0].p.data().is_online = false;
    super::session::on_online_changed(&server, &jury[0].p, false);
    let t = server.systems.social.trials.get_trial(tid).unwrap();
    assert_eq!(t.active_jury_count(), 1);
    assert!(t.jury[&1].jury_member.is_none());
    assert_eq!(jury[0].p.data().unit.transform.x, 102.0);
    assert_eq!(jury[0].p.data().jury_point, 1);
    assert!(!jury[0].p.data().unit.buffs.check_buff(buff::CANNOT_ESCAPE));
}

#[tokio::test]
async fn restored_prisoner_keeps_remaining_online_time_and_can_finish_the_sentence() {
    let server = justice_server();
    crate::systems::skills::install_hooks(&server);
    let mut prisoner = player(&server, 1, "Prisoner", |_| {});
    let now = util::now_ms();
    let row = crate::db_character::SavedBuffRow {
        buff_id: buff::PRISONER_NUIAN,
        ab_level: 1,
        time_left: 5_000,
        charge: 0,
        real_time: false,
        saved_at_ms: now - 86_400_000,
    };
    crate::db_character::apply_active_buffs(&server, &mut prisoner.p.data(), &[row], now);
    crate::systems::dispatch_buff_events(&server, &crate::world::WorldObject::from(&prisoner.p));
    trial::handle_player_login(&server, &prisoner.p);
    let buff = prisoner
        .p
        .data()
        .unit
        .buffs
        .effect_from_buff_id(buff::PRISONER_NUIAN)
        .unwrap()
        .clone();
    assert_eq!(buff.duration, 5_000);
    assert_eq!(prisoner.p.data().guilty_count, 0);
    assert_eq!(prisoner.p.data().unit.transform.x, JAIL_POS.x);
    assert!(ops(&prisoner.client.drain()).contains(&sc::SCTeleportUnitPacket));
    // Run the scheduled completion without waiting five seconds in the test.
    crate::systems::skills::buffs::dispel_task(
        &server,
        prisoner.p.obj_id,
        buff.index,
        buff.task_gen,
    );
    assert!(!prisoner
        .p
        .data()
        .unit
        .buffs
        .check_buff(buff::PRISONER_NUIAN));
}

#[tokio::test]
async fn arrest_is_idempotent_and_justice_teleports_update_server_regions() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let d = player(&server, 1, "Wanted", |c| c.crime_point = 50);
    let guard = player(&server, 2, "Guard", |_| {});
    let tid = trials
        .arrest_criminal(&server, &d.p, Some(&guard.p))
        .unwrap();
    assert_eq!(
        trials.arrest_criminal(&server, &d.p, Some(&guard.p)),
        Some(tid)
    );
    assert_eq!(trials.trial_ids(), vec![tid]);
    assert_eq!(d.p.data().arrest_count, 1);
    assert!(d
        .p
        .data()
        .unit
        .buffs
        .check_buff(buff::FORCIBLY_AWAITING_TRIAL));
    assert_eq!(d.p.data().unit.transform.x, HOLDING_CELL.x);
    assert_eq!(d.p.region(), Some((0, 3, 3)));
    trials.reply_imprison_or_trial(&server, &d.p, false, util::now_ms());
    assert_eq!(d.p.data().unit.transform.x, JAIL_POS.x);
    assert_eq!(d.p.region(), Some((0, 4, 4)));
    assert!(!d
        .p
        .data()
        .unit
        .buffs
        .check_buff(buff::FORCIBLY_AWAITING_TRIAL));
    trials.reply_imprison_or_trial(&server, &d.p, false, util::now_ms());
    assert_eq!(d.p.data().accept_guilty_count, 1);
}

#[tokio::test]
async fn jury_invites_require_an_invitation_the_right_faction_and_current_eligibility() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    trials.initialize_doodads(&[("justice.1.seat.1".into(), seat(0x900, 400.0))]);
    let d = player(&server, 1, "Defendant", |c| c.crime_point = 50);
    let invited = player(&server, 2, "Invited", |c| c.jury_point = 1);
    let foreign = player(&server, 3, "Foreign", |c| {
        c.jury_point = 1;
        c.unit.faction_id = 102;
    });
    trials.update_jury_queues(&server);
    let tid = trials.arrest_criminal(&server, &d.p, None).unwrap();
    trials.reply_imprison_or_trial(&server, &d.p, true, util::now_ms());
    trials.update_tick(&server, util::now_ms() + 1).await;
    let late = player(&server, 4, "Uninvited", |c| c.jury_point = 1);
    trials.update_jury_queues(&server);
    for p in [&foreign.p, &late.p] {
        assert!(!trials.process_trial_invite_reply(&server, p, true, tid, util::now_ms()));
    }
    invited.p.data().crime_point = 50; // queue is stale until the next tick
    assert!(!trials.process_trial_invite_reply(&server, &invited.p, true, tid, util::now_ms()));
    assert_eq!(trials.get_trial(tid).unwrap().active_jury_count(), 0);
}

#[tokio::test]
async fn sparse_jury_seats_are_usable() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    trials.initialize_doodads(&[("justice.1.seat.3".into(), seat(0x900, 400.0))]);
    let d = player(&server, 1, "Defendant", |c| c.crime_point = 50);
    let j = player(&server, 2, "Juror", |c| c.jury_point = 1);
    trials.update_jury_queues(&server);
    let tid = trials.arrest_criminal(&server, &d.p, None).unwrap();
    trials.reply_imprison_or_trial(&server, &d.p, true, util::now_ms());
    trials.update_tick(&server, util::now_ms() + 1).await;
    assert!(trials.process_trial_invite_reply(&server, &j.p, true, tid, util::now_ms()));
    assert!(trials.get_trial(tid).unwrap().jury[&3]
        .jury_member
        .is_some());
}

#[tokio::test]
async fn undecided_jury_times_out_to_the_default_sentence() {
    let (server, mut d, mut jury, tid) = summoned_trial(1).await;
    open_verdict(&server, &mut d, &mut jury, tid).await;
    server
        .systems
        .social
        .trials
        .update_tick(&server, util::now_ms() + 61_000)
        .await;
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        10 * 60_000
    );
}

#[tokio::test]
async fn haranyan_sentences_keep_their_region_including_for_pirates_on_reconnect() {
    let server = justice_server_with(|j| {
        j.court_rooms[0].id = 3;
        j.court_rooms[0].faction = HARANYA;
        j.jails[0].faction = HARANYA;
    });
    let trials = &server.systems.social.trials;
    let d = player(&server, 1, "Defendant", |c| {
        c.unit.faction_id = 102;
        c.crime_point = 50;
    });
    server.systems.social.crimes.insert(murder(50, 1, 99));
    trials.arrest_criminal(&server, &d.p, None).unwrap();
    assert_eq!(d.p.data().offline_guilty_region, 1);
    d.p.data().is_online = false;
    super::session::on_online_changed(&server, &d.p, false);
    {
        let mut c = d.p.data();
        c.unit.faction_id = 161;
        c.is_online = true;
    }
    trial::handle_player_login(&server, &d.p);
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_HARANYAN)
            .unwrap()
            .duration,
        20 * 60_000
    );
    assert!(!d.p.data().unit.buffs.check_buff(buff::PRISONER_NUIAN));
}

#[tokio::test]
async fn concurrent_reports_only_count_a_piece_of_evidence_once() {
    let server = justice_server();
    let hooks = Arc::new(FakeHooks::default());
    server.systems.social.hooks.install(hooks.clone());
    let criminal = player(&server, 1, "Criminal", |_| {});
    let a = player(&server, 2, "WitnessA", |_| {});
    let b = player(&server, 3, "WitnessB", |_| {});
    let mut doodad = Doodad::new(878);
    doodad.owner_type = doodad_owner_type::CHARACTER;
    doodad.owner_id = 1;
    doodad.data = 2;
    let evidence = Entity::new(0x900, doodad);
    let crimes = &server.systems.social.crimes;
    let (a, b) = tokio::join!(
        crimes.report_crime(&server, &a.p, &evidence, 1, 2, 3, "first"),
        crimes.report_crime(&server, &b.p, &evidence, 1, 2, 3, "second"),
    );
    assert_ne!(a.is_some(), b.is_some());
    assert_eq!(crimes.crimes_of_player(1, false).len(), 1);
    assert_eq!(criminal.p.data().crime_point, 25);
    assert_eq!(criminal.p.data().evidence_reported_count, 1);
    assert_eq!(hooks.phases.lock().unwrap().len(), 1);
}

#[tokio::test]
async fn arrest_from_an_instance_loads_the_main_world_and_moves_membership() {
    let server = justice_server();
    let d = player(&server, 1, "Defendant", |c| c.crime_point = 50);
    let main = server.world.world(0).unwrap();
    let instance = server.world.create_world_instance(
        Arc::new(crate::data::world_templates::WorldTemplate::synthetic(
            1,
            "test_instance",
            2,
            2,
            TEST_ZONE,
        )),
        42,
    );
    main.delete(&d.p);
    {
        let mut c = d.p.data();
        c.unit.transform.instance_id = 42;
        c.unit.transform.world_id = 1;
    }
    instance.spawn(&d.p, &server.view_ctx());
    let mut d = d;
    d.client.drain();
    server
        .systems
        .social
        .trials
        .arrest_criminal(&server, &d.p, None)
        .unwrap();
    let frames = d.client.drain();
    let load = frames
        .iter()
        .position(|f| f.0 == sc::SCLoadInstancePacket)
        .unwrap();
    let teleport = frames
        .iter()
        .position(|f| f.0 == sc::SCTeleportUnitPacket)
        .unwrap();
    assert!(load < teleport);
    assert_eq!(d.p.data().unit.transform.instance_id, 0);
    assert!(instance.character_by_obj_id(d.p.obj_id).is_none());
    assert!(main.character_by_obj_id(d.p.obj_id).is_some());
    assert_eq!(d.p.region(), Some((0, 3, 3)));
}

#[tokio::test]
async fn sentence_duration_cannot_overflow_the_prison_buff_or_wire() {
    let (server, mut d, mut jury, tid) = summoned_trial(1).await;
    d.p.data().infamy_point = i32::MAX;
    server
        .systems
        .social
        .trials
        .add_new_evidence(&server, &murder(51, 1, 99));
    open_verdict(&server, &mut d, &mut jury, tid).await;
    send(&server, &mut jury[0], cs::CSJuryVerdictPacket, |s| {
        s.write_u32(tid).write_i32(1).write_u8(6);
    })
    .await;
    server
        .systems
        .social
        .trials
        .update_tick(&server, util::now_ms() + 1)
        .await;
    let max_minutes = i32::MAX / 60_000;
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        max_minutes * 60_000
    );
    let ruling = find(&d.client.drain(), sc::SCRulingStatusPacket);
    assert_eq!(
        ruling.last(),
        Some(&body(&SCRulingStatusPacket {
            count: 1,
            total: 1,
            sentence_type: sentence_result::GUILTY5,
            sentence_time: max_minutes * 60_000,
        }))
    );
}

#[tokio::test]
async fn even_a_zero_minute_pending_case_survives_disconnect() {
    let server = justice_server();
    let d = player(&server, 1, "Defendant", |c| c.crime_point = 50);
    server
        .systems
        .social
        .trials
        .arrest_criminal(&server, &d.p, None)
        .unwrap();
    d.p.data().is_online = false;
    super::session::on_online_changed(&server, &d.p, false);
    assert_eq!(d.p.data().offline_guilty_time, 1);
    d.p.data().is_online = true;
    trial::handle_player_login(&server, &d.p);
    assert_eq!(
        d.p.data()
            .unit
            .buffs
            .effect_from_buff_id(buff::PRISONER_NUIAN)
            .unwrap()
            .duration,
        60_000
    );
}

#[test]
#[ignore = "requires AAEMU_COMPACT_DB"]
fn real_justice_buffs_preserve_prison_sentences() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = crate::data::skills::SkillData::load(&conn).unwrap();
    for id in [buff::PRISONER_NUIAN, buff::PRISONER_HARANYAN] {
        let template = data.buff(id).unwrap();
        assert_eq!(template.kind, BuffKind::Bad);
        assert!(!template.real_time);
        let mut b = crate::models::buff::Buff::new(template.clone(), Default::default(), None, 0);
        b.duration = 10_000;
        assert!(b.should_persist(9_000));
        assert!(!b.should_persist(10_000));
    }
    for id in [
        buff::FORCIBLY_AWAITING_TRIAL,
        buff::TRIAL_DEFENDANT,
        buff::JURY,
    ] {
        assert_eq!(data.buff(id).unwrap().duration, 0);
    }
}

#[tokio::test]
async fn trial_chat_of_a_player_without_court() {
    let server = justice_server();
    let pirate = player(&server, 1, "Pirate", |c| c.unit.faction_id = 161);
    // The C# NullReferenceException: logged, no channel.
    assert!(trial::trial_chat(&server, &pirate.p).is_none());
    let h = player(&server, 2, "H", |c| c.unit.faction_id = 102);
    assert_eq!(
        trial::trial_chat(&server, &h.p).map(|c| c.internal_name()),
        Some("Haranyan Court".into())
    );
}

#[tokio::test]
async fn audience_join_and_leave() {
    let server = justice_server();
    let trials = &server.systems.social.trials;
    let mut bench = Doodad::new(5137);
    bench.transform.set_xyz(104.0, 100.0, 100.0);
    trials.initialize_doodads(&[("justice.1.spectator".into(), Entity::new(0x950, bench))]);
    let mut a = player(&server, 1, "Watcher", |_| {});
    let mut far = player(&server, 20, "Far", |_| {});
    trial_frames(&mut a.client);
    // Too far away (> 5 m): refused.
    send(&server, &mut far, cs::CSJoinTrialAudiencePacket, |s| {
        s.write_u32(5137);
    })
    .await;
    assert!(trial_frames(&mut far.client).is_empty());
    send(&server, &mut a, cs::CSJoinTrialAudiencePacket, |s| {
        s.write_u32(5137);
    })
    .await;
    let f = trial_frames(&mut a.client);
    assert_eq!(
        ops(&f),
        vec![
            sc::SCTrialAudienceJoinedPacket,
            sc::SCJoinedChatChannelPacket
        ]
    );
    assert_eq!(
        f[0].1,
        body(&SCTrialAudienceJoinedPacket {
            trial_id: 0,
            bc: 0x101,
            audience_name: "Watcher".into()
        })
    );
    assert_eq!(
        find(
            &trial_frames(&mut far.client),
            sc::SCTrialAudienceJoinedPacket
        )
        .len(),
        1
    );
    send(&server, &mut a, cs::CSLeaveTrialAudiencePacket, |_| {}).await;
    assert_eq!(
        ops(&trial_frames(&mut a.client)),
        vec![sc::SCTrialAudienceLeftPacket, sc::SCLeavedChatChannelPacket]
    );
    send(&server, &mut a, cs::CSNaviOpenBountyPacket, |s| {
        s.write_bc(0x77);
    })
    .await;
    assert_eq!(
        trial_frames(&mut a.client),
        vec![(sc::SCBountyListPacket, vec![0, 0, 0, 0, 0])]
    );
}

/// (owner, template, position, item template, custom data) of a created evidence doodad.
type CreatedDoodad = (u32, u32, (f32, f32, f32), u32, i32);

/// Fake npcs / skills services for the crime report path.
#[derive(Default)]
struct FakeHooks {
    phases: Mutex<Vec<(u32, u32, i32)>>,
    created: Mutex<Vec<CreatedDoodad>>,
}

impl SocialHooks for FakeHooks {
    fn evidence_func(&self, _server: &GameServer, _doodad: &DoodadRef) -> Option<EvidenceFunc> {
        Some(EvidenceFunc {
            crime_kind: crime_kind::MURDER,
            crime_value: 25,
            next_phase: 7,
        })
    }

    fn doodad_change_phase(
        &self,
        _server: &GameServer,
        doodad: &DoodadRef,
        caster: &PlayerRef,
        next_phase: i32,
    ) {
        self.phases
            .lock()
            .unwrap()
            .push((doodad.obj_id, caster.character_id, next_phase));
    }

    fn create_player_doodad(
        &self,
        _server: &GameServer,
        owner: &PlayerRef,
        template_id: u32,
        pos: (f32, f32, f32),
        _rot_z: f32,
        item_template_id: u32,
        custom_data: i32,
    ) -> Option<DoodadRef> {
        self.created.lock().unwrap().push((
            owner.character_id,
            template_id,
            pos,
            item_template_id,
            custom_data,
        ));
        None
    }
}

#[tokio::test]
async fn crime_report_through_hooks() {
    let server = justice_server();
    let hooks = Arc::new(FakeHooks::default());
    server.systems.social.hooks.install(hooks.clone());
    let mut criminal = player(&server, 1, "Crook", |_| {});
    let mut reporter = player(&server, 2, "Witness", |_| {});
    let victim = player(&server, 3, "Victim", |_| {});

    // Evidence created on a kill (large bloodstain at the victim).
    server.systems.social.crimes.generate_evidence_from_kill(
        &server,
        &crate::world::WorldObject::from(&criminal.p),
        Some(&criminal.p),
        Some(&victim.p),
    );
    assert_eq!(
        hooks.created.lock().unwrap().clone(),
        vec![(1, 878, (103.0, 100.0, 100.0), 0, 3)]
    );

    // A bloodstain owned by the criminal, victim 3, in the world.
    let mut d = Doodad::new(878);
    d.owner_type = doodad_owner_type::CHARACTER;
    d.owner_id = 1;
    d.data = 3;
    d.plant_time = 1_600_000_000;
    d.transform.set_xyz(110.0, 105.0, 100.0);
    let stain = Entity::new(0x800, d);
    let world = server.world.world(0).unwrap();
    world.spawn(&stain, &server.view_ctx());
    trial_frames(&mut criminal.client);
    trial_frames(&mut reporter.client);

    // The criminal cannot report its own crime.
    send(&server, &mut criminal, cs::CSReportCrimePacket, |s| {
        s.write_bc(0x800)
            .write_u32(1)
            .write_i32(2)
            .write_u32(3)
            .write_str("me");
    })
    .await;
    assert!(trial_frames(&mut criminal.client).is_empty());
    assert!(hooks.phases.lock().unwrap().is_empty());

    // An arrested criminal also receives the new record.
    let tid = server
        .systems
        .social
        .trials
        .arrest_criminal(&server, &criminal.p, None)
        .unwrap();
    trial_frames(&mut criminal.client);

    send(&server, &mut reporter, cs::CSReportCrimePacket, |s| {
        s.write_bc(0x800)
            .write_u32(20001)
            .write_i32(4)
            .write_u32(55)
            .write_str("I saw it");
    })
    .await;
    let crimes = server.systems.social.crimes.crimes_of_player(1, false);
    assert_eq!(crimes.len(), 1);
    let e = &crimes[0];
    assert_eq!(
        (
            e.criminal,
            e.victim,
            e.reporter,
            e.crime_kind,
            e.crime_time,
            e.used_skill_id,
            e.doodad_next_func_group,
            e.doodad_func_id,
            e.msg.as_str()
        ),
        (
            1,
            3,
            2,
            crime_kind::MURDER,
            1_600_000_000,
            20001,
            4,
            55,
            "I saw it"
        )
    );
    assert_eq!(e.position, (110.0, 105.0, 100.0));
    let f = trial_frames(&mut criminal.client);
    assert_eq!(
        ops(&f),
        vec![sc::SCCrimeChangedPacket, sc::SCCrimeRecordsPacket]
    );
    assert_eq!(
        f[0].1,
        body(&SCCrimeChangedPacket {
            points: 25,
            crime_points: 25,
            infamy_points: 25,
            crime_state: 2
        })
    );
    let entry = server.systems.social.crimes.record_entry(&server, e);
    assert_eq!(entry.victim_name, "Victim");
    assert_eq!(entry.reporter_name.as_deref(), Some("Witness"));
    assert_eq!(
        f[1].1,
        body(&SCCrimeRecordsPacket {
            trial_id: tid,
            unknown1: 0,
            total_count: 1,
            this_count: 1,
            entries: vec![entry]
        })
    );
    assert_eq!(criminal.p.data().evidence_reported_count, 1);
    // Jail time recalculated: one murder of a level-10 victim.
    assert_eq!(
        server
            .systems
            .social
            .trials
            .get_trial(tid)
            .unwrap()
            .jail_time,
        200
    );
    assert_eq!(hooks.phases.lock().unwrap().clone(), vec![(0x800, 2, 7)]);
    assert_eq!(server.systems.social.crimes.pending_ids().0, vec![e.id]);

    // Bot reports.
    let crimes = &server.systems.social.crimes;
    assert!(crimes.report_bot(&server, &criminal.p, &reporter.p, "bot", 0));
    assert!(!crimes.report_bot(&server, &criminal.p, &reporter.p, "bot", 0));
    assert!(criminal
        .p
        .data()
        .unit
        .buffs
        .check_buff(buff::SUSPECTED_USER));
    assert_eq!(criminal.p.data().reported_as_bot_count, 1);
    assert_eq!(reporter.p.data().bot_reported_count, 1);
    // No tag data: the expiry finds no prime suspect buff.
    assert!(!crimes.report_bot_expired(&server, &criminal.p));
    assert!(crimes.report_bot_expired(&server, &reporter.p));
}
