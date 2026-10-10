//! Phase-2 end-to-end flow without MySQL: two connections select characters (the post-load part
//! of `CSSelectCharacterPacket`), spawn, see each other, move (leaving and re-entering each
//! other's neighbourhood), talk, and one leaves the world. Frames are compared byte for byte
//! where the packet is self-contained, and against the shared builders for `SCUnitStatePacket`.

use std::net::SocketAddr;
use std::time::Duration;

use aaemu_commons::{Marshal, PacketStream};
use sqlx::mysql::MySqlPoolOptions;
use tokio::sync::mpsc;

use crate::config::AppConfig;
use crate::data::skills::{BuffKind, BuffTemplate};
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::models::character::{Character, Inventory};
use crate::models::container::slot_type;
use crate::net::connection::{ConnCommand, GameConnection, GameState};
use crate::opcodes::{cs, pp, sc};
use crate::world::session::{self, SelectDbData};
use crate::world::unit_state::UNIT_STATE_FORCED_BUFFS;
use crate::GameServer;

fn server() -> GameServer {
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1; // nothing listens here
    config.world.motd = "Welcome!".into();
    config.world.logout_message = "Bye".into();
    let db = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    let mut data = GameData::empty();
    data.worlds = WorldTemplates::from_templates(
        vec![WorldTemplate::synthetic(0, "main_world", 2, 2, 100)],
        Vec::new(),
    )
    .unwrap();
    for id in UNIT_STATE_FORCED_BUFFS {
        let mut t = BuffTemplate::simple(id, BuffKind::Good);
        t.duration = 0;
        data.skills.buffs.insert(id, t);
    }
    GameServer::new(config, db, data)
}

struct Client {
    conn: GameConnection,
    out: mpsc::UnboundedReceiver<Vec<u8>>,
    _cmd: mpsc::UnboundedReceiver<ConnCommand>,
}

impl Client {
    fn new(server: &GameServer, id: u32, account: u32) -> Self {
        let (out_tx, out) = mpsc::unbounded_channel();
        let (cmd_tx, cmd) = mpsc::unbounded_channel();
        let peer: SocketAddr = "127.0.0.1:50000".parse().unwrap();
        let (mut conn, handle) = GameConnection::new(id, peer, out_tx, cmd_tx);
        conn.set_account_id(account);
        server.connections.add(handle);
        Self {
            conn,
            out,
            _cmd: cmd,
        }
    }

    /// Drained frames as (opcode, body) (level-1 frames only; level 2 keeps its level in the tag).
    fn drain(&mut self) -> Vec<(u16, Vec<u8>)> {
        let mut v = Vec::new();
        while let Ok(f) = self.out.try_recv() {
            if f[1] == 1 {
                v.push((u16::from_le_bytes([f[4], f[5]]), f[6..].to_vec()));
            } else {
                v.push((0x8000 | u16::from_le_bytes([f[2], f[3]]), f[4..].to_vec()));
            }
        }
        v
    }

    async fn frame(&mut self, server: &GameServer, opcode: u16, body: &[u8]) {
        let mut v = vec![0xDD, 0x01, 0, 0];
        v.extend_from_slice(&opcode.to_le_bytes());
        v.extend_from_slice(body);
        server.handle_frame(&mut self.conn, &v).await.unwrap();
    }
}

fn ops(frames: &[(u16, Vec<u8>)]) -> Vec<u16> {
    frames.iter().map(|f| f.0).collect()
}

fn character(id: u32, name: &str, x: f32, y: f32) -> Character {
    let mut c = Character::new(id, 1000 + id, Default::default());
    c.unit.name = name.into();
    c.race = 1;
    c.gender = 1;
    c.unit.level = 10;
    c.unit.faction_id = 101;
    c.ability1 = 1;
    c.ability2 = 11;
    c.ability3 = 11;
    c.num_inventory_slots = 50;
    c.num_bank_slots = 50;
    c.inventory = Inventory::standalone(id, 50, 50);
    c.unit.transform.x = x;
    c.unit.transform.y = y;
    c.unit.transform.z = 100.0;
    c.unit.transform.zone_id = 100;
    c
}

fn visual_options() -> Vec<u8> {
    vec![0x1F, 1, 2, 3, 4, 5, 6, 1, 0, 0, 0]
}

/// A `CSMoveUnitPacket` body for a `UnitMoveType` and the move-type bytes the server echoes.
fn unit_move(obj_id: u32, x: f32, y: f32) -> (Vec<u8>, Vec<u8>) {
    let mut mt = PacketStream::new();
    mt.write_u32(1234) // time
        .write_u8(0x02) // flags: moving
        .write_position(x, y, 100.0)
        .write_i16(10)
        .write_i16(-5)
        .write_i16(0)
        .write_i8(1)
        .write_i8(2)
        .write_i8(64)
        .write_i8(0)
        .write_i8(127)
        .write_i8(0)
        .write_u8(1) // stance
        .write_u8(0) // alertness
        .write_u8(0); // actor flags
    let mt = mt.into_bytes();
    let mut body = PacketStream::new();
    body.write_bc(obj_id).write_u8(1).write_raw(&mt);
    (body.into_bytes(), mt)
}

fn removed(ids: &[u32]) -> (u16, Vec<u8>) {
    let mut s = PacketStream::new();
    s.write_u16(ids.len() as u16);
    for &i in ids {
        s.write_bc(i);
    }
    (sc::SCUnitsRemovedPacket, s.into_bytes())
}

fn points(obj_id: u32, hp: i32, mp: i32) -> (u16, Vec<u8>) {
    let mut s = PacketStream::new();
    s.write_bc(obj_id).write_i32(hp * 100).write_i32(mp * 100);
    (sc::SCUnitPointsPacket, s.into_bytes())
}

fn unit_state_of(server: &GameServer, p: &crate::world::PlayerRef) -> (u16, Vec<u8>) {
    // The buffs were already added by the first write, so a rebuild is identical apart from the
    // elapsed time of the buffs (0 here, their start time is "now").
    let mut c = p.data().clone();
    let body = crate::world::unit_state::unit_state(
        &mut c,
        &server.game_data,
        chrono::Utc::now().timestamp_millis(),
    )
    .body;
    (sc::SCUnitStatePacket, body)
}

/// Compares two `SCUnitStatePacket` bodies, ignoring the 4-byte `elapsedTime` of each buff.
fn same_unit_state(a: &(u16, Vec<u8>), b: &(u16, Vec<u8>)) -> bool {
    a.0 == b.0 && a.1.len() == b.1.len() && {
        let n = a.1.len();
        // two good buffs at the end: each NetBuff is 47 bytes, elapsed at offset 23
        let tail = 3 + 2 * 47;
        let (ha, hb) = (&a.1[..n - tail], &b.1[..n - tail]);
        let mut ta = a.1[n - tail..].to_vec();
        let mut tb = b.1[n - tail..].to_vec();
        for t in [&mut ta, &mut tb] {
            for k in 0..2 {
                let o = 1 + k * 47 + 23;
                t[o..o + 4].fill(0);
            }
        }
        ha == hb && ta == tb
    }
}

#[tokio::test]
async fn two_players_see_each_other_move_talk_and_leave() {
    let server = server();
    let mut a = Client::new(&server, 1, 1001);
    let mut b = Client::new(&server, 2, 1002);
    a.conn.characters = vec![character(1, "Alpha", 100.0, 100.0)];
    b.conn.characters = vec![character(2, "Beta", 120.0, 110.0)];

    // ---- select (post-load part)
    let c = a.conn.characters[0].clone();
    session::select_loaded(&server, &mut a.conn, c, SelectDbData::default()).unwrap();
    let burst = a.drain();
    let mut want = vec![
        sc::SCCharacterStatePacket,
        sc::SCCharacterGamePointsPacket,
        sc::SCCharacterInvenInitPacket,
    ];
    want.extend([sc::SCCharacterInvenContentsPacket; 10]);
    want.extend([
        sc::SCActionSlotsPacket,
        sc::SCQuestsPacket,
        sc::SCCompletedQuestsPacket,
        sc::SCActabilityPacket,
        sc::SCCountUnreadMailPacket,
        0x007, // new SCFactionListPacket() (hard-coded type id in C#)
        0x008, // new SCFactionRelationListPacket()
        sc::SCExpeditionRolePolicyListPacket,
        sc::SCResponseUIDataPacket,
        sc::SCResponseUIDataPacket,
        sc::SCResponseUIDataPacket,
    ]);
    assert_eq!(ops(&burst), want);
    let pa = a.conn.active_char.clone().unwrap();
    assert_eq!(pa.obj_id, 0x100);
    // SCCharacterState starts with instance id, guid and rwd, then Character.Write.
    let state = &burst[0].1;
    assert_eq!(state[..6], [0, 0, 0, 0, 16, 0]);
    let mut char_bytes = PacketStream::new();
    pa.data().write(&mut char_bytes);
    assert_eq!(state[26..26 + char_bytes.len()], *char_bytes.as_bytes());
    // Bag contents: 5 chunks of 10 empty slots each, chunk index 0..4.
    let (op, body) = &burst[3];
    assert_eq!(*op, sc::SCCharacterInvenContentsPacket);
    let mut want_body = vec![slot_type::INVENTORY, 1, 0];
    want_body.extend([0u8; 40]);
    assert_eq!(*body, want_body);
    assert_eq!(burst[7].1[..3], [slot_type::INVENTORY, 1, 4]);
    assert_eq!(burst[8].1[..3], [slot_type::BANK, 1, 0]);
    // SCQuests / SCCompletedQuests with no entries; the UI options are empty strings.
    assert_eq!(burst[14].1, vec![0, 0, 0, 0]);
    assert_eq!(burst[15].1, vec![0, 0, 0, 0]);
    assert_eq!(burst[16].1, vec![1, 0]); // SCActability(last, 0 entries)
    assert_eq!(burst[17].1, vec![0; 16]);
    assert_eq!(burst[18].1, vec![0]);
    assert_eq!(burst[19].1, vec![0, 0]);
    assert_eq!(burst[20].1, vec![0]);
    assert_eq!(burst[21].1, vec![1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]);
    assert!(!pa
        .data()
        .unit
        .buffs
        .check_buff(crate::models::buff::BUFF_LOGGED_ON)); // no template

    let c = b.conn.characters[0].clone();
    session::select_loaded(&server, &mut b.conn, c, SelectDbData::default()).unwrap();
    b.drain();
    let pb = b.conn.active_char.clone().unwrap();
    assert_eq!(pb.obj_id, 0x101);

    // ---- spawn: SCUnitState(self) + SCDetailedTimeOfDay
    a.frame(&server, cs::CSSpawnCharacterPacket, &visual_options())
        .await;
    let f = a.drain();
    assert_eq!(
        ops(&f),
        vec![sc::SCUnitStatePacket, sc::SCDetailedTimeOfDayPacket]
    );
    assert_eq!(a.conn.state, GameState::World);
    assert!(same_unit_state(&f[0], &unit_state_of(&server, &pa)));
    // NetUnit header: bc objId, name, BaseUnitType.Character, character id.
    assert_eq!(f[0].1[..3], [0, 1, 0]);
    assert_eq!(f[0].1[3..10], [5, 0, b'A', b'l', b'p', b'h', b'a']);
    assert_eq!(f[0].1[10], 0);
    assert_eq!(f[0].1[11..15], 1u32.to_le_bytes());
    let mut tod = PacketStream::new();
    tod.write_f32(12.0)
        .write_f32(0.001_666_6)
        .write_f32(0.0)
        .write_f32(24.0);
    assert_eq!(f[1].1, tod.into_bytes());

    b.frame(&server, cs::CSSpawnCharacterPacket, &visual_options())
        .await;
    b.drain();

    // ---- NotifyInGame: A alone in the world, joins its chat channels.
    a.frame(&server, cs::CSNotifyInGamePacket, &[]).await;
    let joined = |t: i16, sub: i16, f: u32| {
        let mut s = PacketStream::new();
        s.write_i16(t).write_i16(sub).write_u32(f).write_str("");
        (sc::SCJoinedChatChannelPacket, s.into_bytes())
    };
    assert_eq!(
        a.drain(),
        vec![
            joined(1, 0, 0),   // zone (group 0)
            joined(6, 0, 148), // nation (Nuian)
            joined(0, 0, 0),   // faction 101 has no mother id here -> NullChannel
        ]
    );
    a.frame(&server, cs::CSNotifyInGameCompletedPacket, &[])
        .await;
    assert_eq!(a.drain(), vec![(sc::SCOnOffSnowPacket, vec![0])]);

    // ---- B enters next to A: each learns about the other.
    b.frame(&server, cs::CSNotifyInGamePacket, &[]).await;
    let fb = b.drain();
    assert_eq!(fb.len(), 2 + 3);
    assert!(same_unit_state(&fb[0], &unit_state_of(&server, &pa)));
    assert_eq!(fb[1], points(pa.obj_id, 0, 0));
    let fa = a.drain();
    assert_eq!(fa.len(), 2);
    assert!(same_unit_state(&fa[0], &unit_state_of(&server, &pb)));
    assert_eq!(fa[1], points(pb.obj_id, 0, 0));
    let world = server.world.world(0).unwrap();
    assert_eq!(world.region_object_ids((1, 1)), vec![0x100, 0x101]);

    // ---- A moves inside the neighbourhood: B and A get SCOneUnitMovement; first move -> MOTD.
    let (body, mt) = unit_move(pa.obj_id, 110.0, 105.0);
    a.frame(&server, cs::CSMoveUnitPacket, &body).await;
    let mut one = PacketStream::new();
    one.write_bc(pa.obj_id).write_u8(1).write_raw(&mt);
    let one = (sc::SCOneUnitMovementPacket, one.into_bytes());
    assert_eq!(b.drain(), vec![one.clone()]);
    let fa = a.drain();
    assert_eq!(fa.len(), 2);
    assert_eq!(fa[0].0, sc::SCChatMessagePacket); // MOTD
    assert_eq!(fa[1], one);
    assert!((pa.data().unit.transform.x - 110.0).abs() < 0.01);
    assert_eq!(
        pa.data().unit.transform.rot_z,
        crate::models::movement::convert_direction_to_radian(64) as f32
    );

    // ---- A runs far away: both forget each other.
    let (body, mt) = unit_move(pa.obj_id, 1000.0, 1000.0);
    a.frame(&server, cs::CSMoveUnitPacket, &body).await;
    let mut one = PacketStream::new();
    one.write_bc(pa.obj_id).write_u8(1).write_raw(&mt);
    let one = (sc::SCOneUnitMovementPacket, one.into_bytes());
    assert_eq!(b.drain(), vec![one.clone(), removed(&[pa.obj_id])]);
    assert_eq!(a.drain(), vec![one, removed(&[pb.obj_id])]);
    assert_eq!(world.region_object_ids((15, 15)), vec![0x100]);
    assert_eq!(world.region_object_ids((1, 1)), vec![0x101]);

    // ... and back: they see each other again.
    let (body, _) = unit_move(pa.obj_id, 100.0, 100.0);
    a.frame(&server, cs::CSMoveUnitPacket, &body).await;
    let fa = a.drain();
    assert_eq!(fa.len(), 3);
    assert_eq!(fa[0].0, sc::SCOneUnitMovementPacket);
    assert!(same_unit_state(&fa[1], &unit_state_of(&server, &pb)));
    assert_eq!(fa[2], points(pb.obj_id, 0, 0));
    let fb = b.drain();
    assert_eq!(fb.len(), 2); // the movement itself was broadcast from the far region
    assert!(same_unit_state(&fb[0], &unit_state_of(&server, &pa)));
    assert_eq!(fb[1], points(pa.obj_id, 0, 0));

    // ---- B says something: A (neighbour) and B (self) get it.
    let mut chat = PacketStream::new();
    chat.write_i16(0)
        .write_i16(0)
        .write_i32(0)
        .write_str("")
        .write_str("hello")
        .write_u8(0)
        .write_i32(1);
    b.frame(&server, cs::CSSendChatMessagePacket, chat.as_bytes())
        .await;
    let mut want = PacketStream::new();
    want.write_i16(0)
        .write_i16(101)
        .write_u32(101)
        .write_bc(pb.obj_id)
        .write_u32(2)
        .write_u8(0)
        .write_u8(1)
        .write_u32(101)
        .write_str("Beta")
        .write_str("hello")
        .write_i32(1)
        .write_i32(0);
    let msg = (sc::SCChatMessagePacket, want.into_bytes());
    assert_eq!(a.drain(), vec![msg.clone()]);
    assert_eq!(b.drain(), vec![msg]);

    // ---- A goes back to character select: logout timer, then LeaveWorldTask.
    a.frame(&server, cs::CSLeaveWorldPacket, &[1]).await;
    assert_eq!(
        a.drain(),
        vec![(sc::SCPrepareLeaveWorldPacket, vec![0x10, 0x27, 0, 0, 1, 0])]
    );
    assert!(a.conn.leave_task.is_some());
    a.conn.leave_task.take().unwrap().abort();
    let active = a.conn.active_char.clone();
    session::leave_world_task(&server, Some(&mut a.conn), 1, active).await;
    let left = |t: i16, sub: i16, f: u32| {
        let mut s = PacketStream::new();
        s.write_i16(t).write_i16(sub).write_u32(f);
        (sc::SCLeavedChatChannelPacket, s.into_bytes())
    };
    let mut cs_state = PacketStream::new();
    cs_state.write_i32(0);
    assert_eq!(
        a.drain(),
        vec![
            left(6, 0, 148), // nation
            left(1, 0, 0),   // zone
            // RemoveVisibleObject: own region twice (C# quirk), then the other neighbours.
            removed(&[pb.obj_id]),
            removed(&[pb.obj_id]),
            (sc::SCLeaveWorldGrantedPacket, vec![1]),
            (0x8000 | pp::ChangeStatePacket, cs_state.into_bytes()),
        ]
    );
    assert_eq!(
        b.drain(),
        vec![removed(&[pa.obj_id]), removed(&[pa.obj_id])]
    );
    assert_eq!(a.conn.state, GameState::Lobby);
    assert!(server.world.character_by_obj_id(pa.obj_id).is_none());
    assert_eq!(world.region_object_ids((1, 1)), vec![0x101]);
    // The obj id is kept for the next session of the same character.
    assert_eq!(server.world.used_obj_id(1), Some(0x100));

    // B now moves alone: nobody else is told.
    let (body, _) = unit_move(pb.obj_id, 121.0, 111.0);
    b.frame(&server, cs::CSMoveUnitPacket, &body).await;
    assert_eq!(
        ops(&b.drain()),
        vec![sc::SCChatMessagePacket, sc::SCOneUnitMovementPacket]
    );
    assert!(a.drain().is_empty());
}

/// Character creation with synthetic templates. MySQL is down, so `SaveDirectlyToDatabase`
/// fails and the C# answers `SCCharacterCreationFailedPacket(Failed)` after building the whole
/// character (its starting items stay in the item manager, as in C#).
#[tokio::test]
async fn create_character_builds_the_starting_kit_then_fails_to_save() {
    use crate::data::character_templates::{AbilityItems, AbilitySupplyItem, CharacterTemplate};
    use crate::data::items::{EquipTemplate, GradeTemplate, ItemTemplate, TemplateKind};
    use crate::data::skills::{DefaultSkill, SkillTemplate};
    use crate::managers::character::CreateCharacterRequest;

    let mut s = server();
    let data = std::sync::Arc::get_mut(&mut s.game_data).unwrap();
    data.characters.templates.insert(
        16 + 1,
        CharacterTemplate {
            race: 1,
            gender: 1,
            faction_id: 101,
            num_inventory_slot: 50,
            num_bank_slot: 50,
            items: [7001, 0, 0, 0, 0, 0, 0],
            ..CharacterTemplate::default()
        },
    );
    let mut kit = AbilityItems {
        ability: 1,
        ..AbilityItems::default()
    };
    kit.items.shirt = (6001, 0);
    kit.supplies.push(AbilitySupplyItem {
        id: 8001,
        amount: 25,
        grade: 0,
    });
    data.characters.ability_items.insert(1, kit);
    data.characters
        .ability_items
        .insert(0, AbilityItems::default());
    data.items.config.durability_const = 1.0;
    data.items.grades.insert(
        0,
        GradeTemplate {
            grade: 0,
            grade_order: 0,
            durability: 1.0,
        },
    );
    data.items.templates.insert(
        6001,
        ItemTemplate {
            kind: TemplateKind::Armor(EquipTemplate {
                slot_type_id: 3, // Chest
                coverage: 10,
                durability_ratio: 1.0,
                durability_multiplier: 100,
                ..EquipTemplate::default()
            }),
            ..ItemTemplate::plain(6001, 1)
        },
    );
    data.items.templates.insert(
        7001,
        ItemTemplate {
            kind: TemplateKind::BodyPart {
                slot_type_id: 23, // Face
                model_id: 1,
            },
            ..ItemTemplate::plain(7001, 1)
        },
    );
    data.items
        .templates
        .insert(8001, ItemTemplate::plain(8001, 10));
    data.skills.add_skill(SkillTemplate {
        id: 100,
        ability_id: 0,
        ..SkillTemplate::default()
    });
    data.skills.add_skill(SkillTemplate {
        id: 200,
        ability_id: 1,
        ability_level: 1,
        auto_learn: true,
        need_learn: true,
        show: true,
        ..SkillTemplate::default()
    });
    data.skills.default_skills.push(DefaultSkill {
        skill_id: 100,
        slot: 1,
        add_to_slot: true,
    });
    data.skills.finish();

    let mut a = Client::new(&s, 1, 1001);
    let req = CreateCharacterRequest {
        name: "  nEWbie ".into(),
        race: 1,
        gender: 1,
        body_items: [0; 7],
        model_params: Default::default(),
        ability1: 1,
        ability2: 11,
        ability3: 11,
        level: 1,
    };
    crate::managers::character::create(&s, &mut a.conn, req.clone())
        .await
        .unwrap();
    assert_eq!(
        a.drain(),
        vec![(sc::SCCharacterCreationFailedPacket, vec![3])]
    );
    // The id and the name were released again.
    assert!(s.names.no_names_registered());
    assert!(a.conn.characters.is_empty());
    // Starting kit: chest armor (with its MaxDurability) + face body part, supplies in stacks.
    let eq = s
        .items
        .container_for_character(1, slot_type::EQUIPMENT, 0, &s.ids.container);
    {
        let eq = eq.lock().unwrap();
        let chest = eq.get_item_by_slot(2).unwrap();
        assert_eq!(chest.template_id, 6001);
        assert_eq!(chest.details.durability, 10);
        assert_eq!(eq.get_item_by_slot(19).unwrap().template_id, 7001);
    }
    let bag = s
        .items
        .container_for_character(1, slot_type::INVENTORY, 0, &s.ids.container);
    let mut counts: Vec<(i32, i32)> = bag
        .lock()
        .unwrap()
        .items
        .iter()
        .map(|i| (i.slot, i.count))
        .collect();
    counts.sort();
    assert_eq!(counts, vec![(0, 10), (1, 10), (2, 5)]);

    // Invalid names are refused before anything is created.
    let bad = CreateCharacterRequest {
        name: "bad name".into(),
        ..req
    };
    crate::managers::character::create(&s, &mut a.conn, bad)
        .await
        .unwrap();
    assert_eq!(
        a.drain(),
        vec![(sc::SCCharacterCreationFailedPacket, vec![5])]
    );
}

#[tokio::test]
async fn delete_and_restore_responses() {
    let s = server();
    let mut a = Client::new(&s, 1, 1001);
    // Unknown character: status 4 / 0, no DB access.
    crate::managers::character::set_restore_character(&s, &mut a.conn, 77)
        .await
        .unwrap();
    assert_eq!(
        a.drain(),
        vec![(
            sc::SCCancelCharacterDeleteResponsePacket,
            vec![77, 0, 0, 0, 4]
        )]
    );
    // Known character: the response is sent before the UPDATE (which fails here).
    a.conn.characters = vec![character(5, "Five", 0.0, 0.0)];
    assert!(
        crate::managers::character::set_restore_character(&s, &mut a.conn, 5)
            .await
            .is_err()
    );
    assert_eq!(
        a.drain(),
        vec![(
            sc::SCCancelCharacterDeleteResponsePacket,
            vec![5, 0, 0, 0, 3]
        )]
    );
}

#[tokio::test]
async fn time_of_day_subscription() {
    let s = server();
    let a = Client::new(&s, 1, 1001);
    let mut a = a;
    let id = s.time.subscribe(&a.conn.handle());
    assert_eq!(a.drain()[0].0, sc::SCDetailedTimeOfDayPacket);
    s.time.tick();
    let f = a.drain();
    assert_eq!(f[0].0, sc::SCTimeOfDayPacket);
    let t = f32::from_le_bytes(f[0].1[..4].try_into().unwrap());
    assert!((t - (12.0 + 3600.0 * 0.001_666_6 * 10.0 / 3600.0)).abs() < 1e-4);
    s.time.unsubscribe(id);
    s.time.tick();
    assert!(a.drain().is_empty());
}
