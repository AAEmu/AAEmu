//! Game server client (C2G / G2C + the proxy channel) for the 1.2 protocol.
//!
//! Every frame payload is `[0xDD][level]...`: level 1 (game) carries `[hash][count][u16 opcode]`,
//! level 2 (proxy / CryNetwork) just `[u16 opcode]`. 1.2 traffic is neither encrypted nor
//! compressed.

use std::collections::{HashMap, HashSet};
use std::time::{Duration, Instant};

use aaemu_commons::net::FrameCodec;
use aaemu_commons::PacketStream;
use aaemu_game::opcodes::{cs, name_of, pp, sc};
use anyhow::{anyhow, bail, Context};
use futures::{SinkExt, StreamExt};
use tokio::net::tcp::OwnedWriteHalf;
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio_util::codec::{FramedRead, FramedWrite};

use crate::record::{Channel, Dir, Recorder};

pub const LEVEL_GAME: u8 = 1;
pub const LEVEL_PROXY: u8 = 2;
/// `CompressedGamePackets` (`DD04`).
pub const LEVEL_COMPRESSED: u8 = 4;

/// A received G2C / proxy packet.
#[derive(Debug)]
pub struct Packet {
    pub level: u8,
    pub opcode: u16,
    pub body: Vec<u8>,
}

impl Packet {
    pub fn name(&self) -> &'static str {
        packet_name(Dir::S2C, self.level, self.opcode)
    }
    pub fn is(&self, level: u8, opcode: u16) -> bool {
        self.level == level && self.opcode == opcode
    }
    pub fn is_sc(&self, opcode: u16) -> bool {
        self.is(LEVEL_GAME, opcode)
    }
    pub fn stream(&self) -> PacketStream {
        PacketStream::from_bytes(self.body.clone())
    }
}

/// C# constant name of a game opcode.
pub fn packet_name(dir: Dir, level: u8, opcode: u16) -> &'static str {
    let table = match (dir, level) {
        (_, LEVEL_PROXY) => pp::NAMES,
        (Dir::S2C, _) => sc::NAMES,
        (Dir::C2S, _) => cs::NAMES,
    };
    name_of(table, opcode).unwrap_or("Unknown")
}

/// A character of `SCCharacterListPacket` / `SCCreateCharacterResponsePacket`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CharInfo {
    pub id: u32,
    pub name: String,
}

/// A unit seen in `SCUnitStatePacket`.
#[derive(Clone, Debug, PartialEq)]
pub struct UnitInfo {
    pub obj_id: u32,
    pub name: String,
    pub unit_type: u8,
    /// Character id (type 0) or NPC template id (type 1).
    pub id: u32,
    pub pos: (f32, f32, f32),
    /// Transport-layer id of a mate (type 5, `SCUnitStatePacket`); 0 for every other unit.
    pub tl_id: u16,
}

/// The last `SCMateSpawnedPacket`: the owner's own view of the summoned mate.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MateSpawn {
    pub tl_id: u16,
    pub db_id: u32,
    pub item_id: u64,
    pub user_state: u8,
}

/// One item of an `SCItemTaskSuccessPacket` task.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TaskItem {
    /// `ItemAction` of the task (5 create, 9 update detail, ...).
    pub task: u8,
    /// Template id (0 for tasks that do not carry it, e.g. `UpdateDetail`).
    pub template: u32,
    pub id: u64,
    /// `SummonMate` detail block: `(exp, dead flag, level)`; a non-zero dead flag marks a corpse.
    pub mate: Option<(i32, u8, u8)>,
}

/// The readable part of an `SCItemTaskSuccessPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ItemTaskInfo {
    /// `ItemTaskType` (59 mate death, 58 repair pets, 11 GM, ...).
    pub action: u8,
    pub items: Vec<TaskItem>,
    /// Sum of the `MoneyChange` tasks (copper; negative = deducted).
    pub money: i64,
}

/// Something the server told us that scenarios assert on, in arrival order.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum GameEvent {
    /// `SCErrorMsgPacket` (error code).
    ErrorMsg(i16),
    ItemTask(ItemTaskInfo),
}

/// `ItemAction` values of the tasks [`parse_item_task`] reads.
mod item_action {
    pub const CHANGE_MONEY_AMOUNT: u8 = 1;
    pub const CHANGE_BANK_MONEY_AMOUNT: u8 = 2;
    pub const CREATE: u8 = 5;
    pub const TAKE: u8 = 6;
    pub const UPDATE_DETAIL: u8 = 9;
    pub const CHANGE_OWNER: u8 = 15;
}

/// Best-effort decode of an `SCItemTaskSuccessPacket` body (`[u8 reason][u8 count]`, then the
/// tasks, see `models/item_task.rs` of the game crate). Reading stops at the first task kind it
/// does not know or on truncation; what was read so far is returned.
pub fn parse_item_task(body: &[u8]) -> ItemTaskInfo {
    fn details(s: &mut PacketStream) -> anyhow::Result<Option<(i32, u8, u8)>> {
        let len = s.read_i16()?;
        if len != 128 {
            bail!("task details length {len}");
        }
        let mut block = [0u8; 128];
        for b in &mut block {
            *b = s.read_u8()?;
        }
        // detail type 3 = SummonMate: [i32 exp][u8 dead][u8 level]
        Ok((block[0] == 3).then(|| {
            (
                i32::from_le_bytes([block[1], block[2], block[3], block[4]]),
                block[5],
                block[6],
            )
        }))
    }
    fn read(s: &mut PacketStream, info: &mut ItemTaskInfo) -> anyhow::Result<()> {
        info.action = s.read_u8()?;
        let count = s.read_u8()?;
        for _ in 0..count {
            let task = s.read_u8()?;
            match task {
                item_action::CHANGE_MONEY_AMOUNT | item_action::CHANGE_BANK_MONEY_AMOUNT => {
                    info.money += i64::from(s.read_i32()?);
                }
                item_action::CREATE | item_action::CHANGE_OWNER | item_action::TAKE => {
                    s.read_u8()?; // slot type
                    s.read_u8()?; // slot
                    let template = s.read_u32()?;
                    let id = s.read_u64()?;
                    info.items.push(TaskItem {
                        task,
                        template,
                        id,
                        mate: None,
                    });
                    s.read_u8()?; // grade
                    s.read_u8()?; // flags
                    s.read_i32()?; // count
                    let mate = details(s)?;
                    if let Some(item) = info.items.last_mut() {
                        item.mate = mate;
                    }
                    s.read_i64()?; // create time
                    s.read_i32()?; // lifespan
                    s.read_u32()?; // made unit id
                    s.read_u8()?; // world id
                    s.read_i64()?; // unsecure time
                    s.read_i64()?; // unpack time
                }
                item_action::UPDATE_DETAIL => {
                    s.read_u8()?; // slot type
                    s.read_u8()?; // slot
                    let id = s.read_u64()?;
                    let mate = details(s)?;
                    info.items.push(TaskItem {
                        task,
                        template: 0,
                        id,
                        mate,
                    });
                }
                _ => break,
            }
        }
        Ok(())
    }
    let mut s = PacketStream::from_bytes(body.to_vec());
    let mut info = ItemTaskInfo::default();
    let _ = read(&mut s, &mut info);
    info
}

/// What the bot learned from the server so far.
#[derive(Debug, Default)]
pub struct GameState {
    pub token: u32,
    pub stream_port: u16,
    pub last_change_state: Option<i32>,
    pub characters: Vec<CharInfo>,
    pub char_list_done: bool,
    pub selected_char: u32,
    pub self_obj: u32,
    pub self_pos: (f32, f32, f32),
    pub units: HashMap<u32, UnitInfo>,
    /// First item of the bag (`SCCharacterInvenContentsPacket`, slot type 2): `(item id, slot)`.
    pub bag_items: Vec<(u64, u8, u32)>,
    pub bag_slots: u8,
    /// Latest `SCUnitPointsPacket` per unit: `(hp, mp)` (the wire carries them times 100).
    pub unit_points: HashMap<u32, (i32, i32)>,
    /// Units announced dead by `SCUnitDeathPacket` (and not removed since).
    pub dead_units: HashSet<u32>,
    /// Last `SCMateSpawnedPacket`.
    pub mate_spawned: Option<MateSpawn>,
    /// Number of `SCMateSpawnedPacket`s seen so far.
    pub mate_spawn_count: u32,
    /// `SCErrorMsgPacket`s and `SCItemTaskSuccessPacket`s in arrival order; scenarios remember
    /// `events.len()` before an action and inspect what was appended.
    pub events: Vec<GameEvent>,
    /// Position of the last `SCTeleportUnitPacket` (a GM `/move` only tells the client to go
    /// there; the server adopts the position from the client's next movement packet).
    pub last_teleport: Option<(f32, f32, f32)>,
    pub disconnected: bool,
}

impl GameState {
    /// Updates the state from a packet.
    pub fn observe(&mut self, p: &Packet) -> anyhow::Result<()> {
        let mut s = p.stream();
        let st = self;
        if p.is(LEVEL_PROXY, pp::ChangeStatePacket) {
            st.last_change_state = Some(s.read_i32()?);
        } else if p.is_sc(sc::X2EnterWorldResponsePacket) {
            let _reason = s.read_i16()?;
            let _gm = s.read_bool()?;
            st.token = s.read_u32()?;
            st.stream_port = s.read_u16()?;
        } else if p.is_sc(sc::SCCharacterListPacket) {
            let last = s.read_bool()?;
            let n = s.read_u8()?;
            if n > 0 {
                // Only the first entry can be located without decoding a whole Character.
                let id = s.read_u32()?;
                let name = s.read_string()?;
                if !st.characters.iter().any(|c| c.id == id) {
                    st.characters.push(CharInfo { id, name });
                }
            }
            st.char_list_done |= last;
        } else if p.is_sc(sc::SCCreateCharacterResponsePacket) {
            let id = s.read_u32()?;
            let name = s.read_string()?;
            st.characters.push(CharInfo { id, name });
        } else if p.is_sc(sc::SCUnitStatePacket) {
            let obj_id = s.read_bc()?;
            let name = s.read_string()?;
            let unit_type = s.read_u8()?;
            let mut tl_id = 0;
            let id = match unit_type {
                0 => {
                    let id = s.read_u32()?;
                    s.read_u64()?;
                    id
                }
                1 => {
                    s.read_bc()?;
                    let tpl = s.read_u32()?;
                    s.read_u32()?;
                    s.read_u8()?;
                    tpl
                }
                5 => {
                    // Mate: tl id, template id, owner character id.
                    tl_id = s.read_u16()?;
                    let tpl = s.read_u32()?;
                    s.read_u32()?;
                    tpl
                }
                _ => return Ok(()),
            };
            let _owner = s.read_string()?;
            let pos = s.read_position()?;
            if unit_type == 0 && id == st.selected_char && id != 0 {
                st.self_obj = obj_id;
                st.self_pos = pos;
            }
            st.units.insert(
                obj_id,
                UnitInfo {
                    obj_id,
                    name,
                    unit_type,
                    id,
                    pos,
                    tl_id,
                },
            );
        } else if p.is_sc(sc::SCOneUnitMovementPacket) {
            // objId, MoveTypeEnum (1 = Unit), time, flags [+ scType, phase], position.
            let obj_id = s.read_bc()?;
            if s.read_u8()? == 1 {
                s.read_u32()?;
                let flags = s.read_u8()?;
                if flags & 0x10 != 0 {
                    s.read_u32()?;
                    s.read_u8()?;
                }
                let pos = s.read_position()?;
                if let Some(u) = st.units.get_mut(&obj_id) {
                    u.pos = pos;
                }
            }
        } else if p.is_sc(sc::SCTeleportUnitPacket) {
            // reason, error message, position, z2
            s.read_u8()?;
            s.read_i16()?;
            let pos = s.read_position()?;
            st.last_teleport = Some(pos);
            st.self_pos = pos;
        } else if p.is_sc(sc::SCUnitsRemovedPacket) {
            let n = s.read_u16()?;
            for _ in 0..n {
                let id = s.read_bc()?;
                st.units.remove(&id);
                st.unit_points.remove(&id);
                st.dead_units.remove(&id);
            }
        } else if p.is_sc(sc::SCUnitPointsPacket) {
            let obj_id = s.read_bc()?;
            let hp = s.read_i32()? / 100;
            let mp = s.read_i32()? / 100;
            st.unit_points.insert(obj_id, (hp, mp));
        } else if p.is_sc(sc::SCUnitDeathPacket) {
            st.dead_units.insert(s.read_bc()?);
        } else if p.is_sc(sc::SCErrorMsgPacket) {
            st.events.push(GameEvent::ErrorMsg(s.read_i16()?));
        } else if p.is_sc(sc::SCItemTaskSuccessPacket) {
            st.events
                .push(GameEvent::ItemTask(parse_item_task(&p.body)));
        } else if p.is_sc(sc::SCMateSpawnedPacket) {
            st.mate_spawned = Some(MateSpawn {
                tl_id: s.read_u16()?,
                db_id: s.read_u32()?,
                item_id: s.read_u64()?,
                user_state: s.read_u8()?,
            });
            st.mate_spawn_count += 1;
        } else if p.is_sc(sc::SCCharacterInvenContentsPacket) {
            let slot_type = s.read_u8()?;
            let _num_chunks = s.read_u8()?;
            let chunk = s.read_u8()?;
            if slot_type == 2 {
                // 10 items per chunk; only leading items can be located (variable layout).
                let tpl = s.read_u32()?;
                if tpl != 0 {
                    let id = s.read_u64()?;
                    st.bag_items.push((id, chunk * 10, tpl));
                }
            }
        }
        Ok(())
    }
}

pub struct GameClient {
    writer: FramedWrite<OwnedWriteHalf, FrameCodec>,
    rx: mpsc::UnboundedReceiver<Vec<u8>>,
    rec: Recorder,
    count: u8,
    pub account_id: u32,
    pub state: GameState,
    pub timeout: Duration,
    /// Keep-alive: the servers drop connections without a `PingPacket` for 30 s.
    pub ping_every: Duration,
    last_ping: Option<Instant>,
    started: Instant,
}

impl GameClient {
    pub async fn connect(addr: &str, rec: Recorder) -> anyhow::Result<Self> {
        let sock = TcpStream::connect(addr)
            .await
            .with_context(|| format!("connecting to game server {addr}"))?;
        sock.set_nodelay(true)?;
        let (rd, wr) = sock.into_split();
        let (tx, rx) = mpsc::unbounded_channel();
        tokio::spawn(async move {
            let mut reader = FramedRead::new(rd, FrameCodec);
            loop {
                match reader.next().await {
                    Some(Ok(frame)) => {
                        if tx.send(frame).is_err() {
                            break;
                        }
                    }
                    Some(Err(e)) => {
                        tracing::warn!("game socket read error: {e}");
                        break;
                    }
                    None => {
                        tracing::debug!("game socket closed by the server");
                        break;
                    }
                }
            }
        });
        Ok(Self {
            writer: FramedWrite::new(wr, FrameCodec),
            rx,
            rec,
            count: 0,
            account_id: 0,
            state: GameState::default(),
            timeout: Duration::from_secs(20),
            ping_every: Duration::from_secs(3),
            last_ping: None,
            started: Instant::now(),
        })
    }

    pub fn recorder(&self) -> &Recorder {
        &self.rec
    }

    /// Sends a level-1 (`CS*`) packet.
    pub async fn send_cs(&mut self, opcode: u16, body: &[u8]) -> anyhow::Result<()> {
        self.rec.push(
            Channel::Game,
            Dir::C2S,
            LEVEL_GAME,
            opcode,
            packet_name(Dir::C2S, LEVEL_GAME, opcode),
            body,
        );
        let mut p = Vec::with_capacity(body.len() + 6);
        p.extend_from_slice(&[0xDD, LEVEL_GAME, 0, self.count]);
        self.count = self.count.wrapping_add(1);
        p.extend_from_slice(&opcode.to_le_bytes());
        p.extend_from_slice(body);
        self.writer.send(p).await?;
        Ok(())
    }

    /// Sends a level-2 (proxy) packet.
    pub async fn send_proxy(&mut self, opcode: u16, body: &[u8]) -> anyhow::Result<()> {
        self.rec.push(
            Channel::Game,
            Dir::C2S,
            LEVEL_PROXY,
            opcode,
            packet_name(Dir::C2S, LEVEL_PROXY, opcode),
            body,
        );
        let mut p = Vec::with_capacity(body.len() + 4);
        p.extend_from_slice(&[0xDD, LEVEL_PROXY]);
        p.extend_from_slice(&opcode.to_le_bytes());
        p.extend_from_slice(body);
        self.writer.send(p).await?;
        Ok(())
    }

    fn decode(&mut self, frame: Vec<u8>) -> anyhow::Result<Packet> {
        if frame.len() < 4 {
            bail!("short game frame ({} bytes)", frame.len());
        }
        let level = frame[1];
        if level == LEVEL_COMPRESSED {
            // `CompressedGamePackets`: [dd][04][u16 count][raw deflate of
            // ([u16 0][u16 typeId][body])*]. The inner packets carry no lengths; the capture
            // keeps the inflated batch (C# and Rust compress differently, the content is what
            // must match).
            let count = u16::from_le_bytes([frame[2], frame[3]]);
            let body = miniz_oxide::inflate::decompress_to_vec(&frame[4..])
                .map_err(|e| anyhow!("DD04 inflate failed: {e:?}"))?;
            let p = Packet {
                level,
                opcode: count,
                body,
            };
            self.rec.push(
                Channel::Game,
                Dir::S2C,
                level,
                count,
                "CompressedGamePackets",
                &p.body,
            );
            return Ok(p);
        }
        let (opcode, body) = if level == LEVEL_GAME {
            if frame.len() < 6 {
                bail!("short level-1 frame");
            }
            (
                u16::from_le_bytes([frame[4], frame[5]]),
                frame[6..].to_vec(),
            )
        } else {
            (
                u16::from_le_bytes([frame[2], frame[3]]),
                frame[4..].to_vec(),
            )
        };
        let p = Packet {
            level,
            opcode,
            body,
        };
        self.rec
            .push(Channel::Game, Dir::S2C, level, opcode, p.name(), &p.body);
        if let Err(e) = self.observe(&p) {
            tracing::debug!("could not parse {}: {e}", p.name());
        }
        Ok(p)
    }

    /// `PingPacket` (proxy): `tPhy`, `ping` and `local`, like the client's keep-alive.
    pub async fn ping(&mut self) -> anyhow::Result<()> {
        let ms = self.started.elapsed().as_millis() as i64;
        let mut s = PacketStream::new();
        s.write_i64(ms).write_i64(ms).write_u32(ms as u32);
        self.last_ping = Some(Instant::now());
        self.send_proxy(pp::PingPacket, s.as_bytes()).await
    }

    async fn keep_alive(&mut self) -> anyhow::Result<()> {
        if self.account_id != 0
            && self
                .last_ping
                .is_none_or(|t| t.elapsed() >= self.ping_every)
        {
            self.ping().await?;
        }
        Ok(())
    }

    /// Next packet, or `None` after `wait` without one. Sends keep-alive pings as needed.
    pub async fn recv_within(&mut self, wait: Duration) -> anyhow::Result<Option<Packet>> {
        self.keep_alive().await?;
        let wait = wait.min(self.ping_every);
        match tokio::time::timeout(wait, self.rx.recv()).await {
            Err(_) => Ok(None),
            Ok(None) => {
                self.state.disconnected = true;
                Err(anyhow!("game server closed the connection"))
            }
            Ok(Some(frame)) => Ok(Some(self.decode(frame)?)),
        }
    }

    /// Reads packets until one matches `pred` (returned) or the timeout elapses.
    pub async fn wait_for<F>(&mut self, what: &str, mut pred: F) -> anyhow::Result<Packet>
    where
        F: FnMut(&Packet) -> bool,
    {
        let deadline = Instant::now() + self.timeout;
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            if left.is_zero() {
                bail!("timed out waiting for {what}");
            }
            match self.recv_within(left).await? {
                Some(p) if pred(&p) => return Ok(p),
                Some(_) | None => {}
            }
        }
    }

    /// Waits for a level-1 packet with `opcode`.
    pub async fn wait_sc(&mut self, opcode: u16) -> anyhow::Result<Packet> {
        let name = packet_name(Dir::S2C, LEVEL_GAME, opcode);
        self.wait_for(name, |p| p.is_sc(opcode)).await
    }

    /// Reads everything that arrives until the connection is quiet for `quiet` (or `max`).
    pub async fn drain(&mut self, quiet: Duration, max: Duration) -> anyhow::Result<Vec<Packet>> {
        let deadline = Instant::now() + max;
        let mut out = Vec::new();
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            if left.is_zero() {
                return Ok(out);
            }
            match self.recv_within(quiet.min(left)).await? {
                Some(p) => out.push(p),
                None => return Ok(out),
            }
        }
    }

    /// Updates [`GameState`] from a packet.
    fn observe(&mut self, p: &Packet) -> anyhow::Result<()> {
        self.state.observe(p)
    }

    // ----- C2G packets -----

    /// `X2EnterWorldPacket`.
    pub async fn enter_world(&mut self, account_id: u32, cookie: u32) -> anyhow::Result<()> {
        self.account_id = account_id;
        let mut s = PacketStream::new();
        s.write_u32(0) // pFrom
            .write_u32(0) // pTo
            .write_u32(account_id)
            .write_u32(cookie)
            .write_i32(0) // zoneId
            .write_u8(0) // tb
            .write_u64(0); // revision
        self.send_cs(cs::X2EnterWorldPacket, s.as_bytes()).await
    }

    /// `FinishStatePacket` (proxy).
    pub async fn finish_state(&mut self, state: i32) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_i32(state);
        self.send_proxy(pp::FinishStatePacket, s.as_bytes()).await
    }

    /// `CSListCharacterPacket`.
    pub async fn list_characters(&mut self) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_i32(0).write_bytes_sized(&[]);
        self.send_cs(cs::CSListCharacterPacket, s.as_bytes()).await
    }

    /// `CSCreateCharacterPacket`: Nuian (1) male (1) Fighter (ability 1), default appearance.
    pub async fn create_character(&mut self, name: &str) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_str(name).write_u8(1).write_u8(1);
        for _ in 0..7 {
            s.write_u32(0); // body items: template defaults
        }
        write_custom_model(&mut s);
        s.write_u8(1) // ability1: Fight
            .write_u8(11) // ability2: None
            .write_u8(11) // ability3: None
            .write_u8(1); // level
        self.send_cs(cs::CSCreateCharacterPacket, s.as_bytes())
            .await
    }

    /// `CSSelectCharacterPacket`.
    pub async fn select_character(&mut self, id: u32) -> anyhow::Result<()> {
        self.state.selected_char = id;
        let mut s = PacketStream::new();
        s.write_u32(id).write_bool(false).write_u8(0);
        self.send_cs(cs::CSSelectCharacterPacket, s.as_bytes())
            .await
    }

    /// `CSSpawnCharacterPacket` (visual options: none).
    /// `CSSpawnCharacterPacket` with the visual options the client sends (all flags set).
    pub async fn spawn_character(&mut self) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u8(0x1F) // stp, helmet, back holdable, cosplay, cosplay backpack
            .write_raw(&[0u8; 6])
            .write_bool(true)
            .write_bool(true)
            .write_bool(true)
            .write_bool(true);
        self.send_cs(cs::CSSpawnCharacterPacket, s.as_bytes()).await
    }

    pub async fn notify_in_game(&mut self) -> anyhow::Result<()> {
        self.send_cs(cs::CSNotifyInGamePacket, &[]).await
    }

    pub async fn notify_in_game_completed(&mut self) -> anyhow::Result<()> {
        self.send_cs(cs::CSNotifyInGameCompletedPacket, &[]).await
    }

    /// `CSMoveUnitPacket` with a `UnitMoveType` for our own character.
    pub async fn move_to(&mut self, pos: (f32, f32, f32), time: u32) -> anyhow::Result<()> {
        self.move_to_with_direction(pos, time, 0).await
    }

    /// Move into range while facing a unit, so angle restrictions and auto attacks apply.
    pub async fn move_to_facing(
        &mut self,
        pos: (f32, f32, f32),
        target: (f32, f32, f32),
        time: u32,
    ) -> anyhow::Result<()> {
        self.move_to_with_direction(pos, time, facing_direction(pos, target))
            .await
    }

    async fn move_to_with_direction(
        &mut self,
        pos: (f32, f32, f32),
        time: u32,
        direction: i8,
    ) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_bc(self.state.self_obj)
            .write_u8(1) // MoveTypeEnum.Unit
            .write_u32(time)
            .write_u8(0x02) // Moving
            .write_position(pos.0, pos.1, pos.2)
            .write_i16(0)
            .write_i16(0)
            .write_i16(0) // velocity
            .write_i8(0)
            .write_i8(0)
            .write_i8(direction) // yaw
            .write_i8(0)
            .write_i8(0)
            .write_i8(0) // delta movement
            .write_i8(1) // stance: idle
            .write_u8(0) // alertness
            .write_u8(0); // actor flags
        self.state.self_pos = pos;
        self.send_cs(cs::CSMoveUnitPacket, s.as_bytes()).await
    }

    /// `CSSendChatMessagePacket`.
    pub async fn chat(&mut self, chat_type: i16, message: &str) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_i16(chat_type)
            .write_i16(0)
            .write_i32(0)
            .write_str("") // target
            .write_str(message)
            .write_u8(0) // language
            .write_i32(0); // ability
        self.send_cs(cs::CSSendChatMessagePacket, s.as_bytes())
            .await
    }

    /// `CSStartSkillPacket`: unit caster (us) on a unit target.
    pub async fn start_skill(&mut self, skill_id: u32, target_obj: u32) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u32(skill_id)
            .write_u8(0)
            .write_bc(self.state.self_obj)
            .write_u8(0)
            .write_bc(target_obj)
            .write_u8(0); // flag: no skill object
        self.send_cs(cs::CSStartSkillPacket, s.as_bytes()).await
    }

    /// `CSStartSkillPacket` cast from an inventory item (the item's use skill) on ourselves,
    /// as the client does for a summon item.
    pub async fn use_item_skill(
        &mut self,
        skill_id: u32,
        item_id: u64,
        item_template_id: u32,
    ) -> anyhow::Result<()> {
        let me = self.state.self_obj;
        self.start_item_skill(skill_id, item_id, item_template_id, me)
            .await
    }

    /// `CSStartSkillPacket` cast by an item (the 1.2 client's item-use skill): item caster
    /// (our object, the item id and template), unit target, no skill object.
    pub async fn start_item_skill(
        &mut self,
        skill_id: u32,
        item_id: u64,
        item_template_id: u32,
        target_obj: u32,
    ) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u32(skill_id)
            .write_u8(2) // SkillCasterType.Item
            .write_bc(self.state.self_obj)
            .write_u64(item_id)
            .write_u32(item_template_id)
            .write_u8(0) // type1
            .write_u32(0) // type2
            .write_u8(0) // SkillCastUnitTarget
            .write_bc(target_obj)
            .write_u8(0); // flag: no skill object
        self.send_cs(cs::CSStartSkillPacket, s.as_bytes()).await
    }

    /// `CSTeleportEndedPacket`: the client's answer to `SCTeleportUnitPacket` (the server
    /// then accepts our movement packets again and re-announces the objects around).
    pub async fn teleport_ended(&mut self, pos: (f32, f32, f32)) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_i64(aaemu_commons::helpers::convert_coord_to_long(pos.0))
            .write_i64(aaemu_commons::helpers::convert_coord_to_long(pos.1))
            .write_f32(pos.2)
            .write_raw(&[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x3f]); // identity
        self.send_cs(cs::CSTeleportEndedPacket, s.as_bytes()).await
    }

    /// `CSRemoveMatePacket`: dismiss the mate with transport id `tl_id`.
    pub async fn remove_mate(&mut self, tl_id: u16) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u16(tl_id);
        self.send_cs(cs::CSRemoveMatePacket, s.as_bytes()).await
    }

    /// `CSRepairPetItemsPacket`: the stablehand revive of dead summon items; the NPC must be the
    /// current interaction object (`CSInteractNPCPacket` first).
    pub async fn repair_pet_items(&mut self, npc_obj: u32) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_bc(npc_obj);
        self.send_cs(cs::CSRepairPetItemsPacket, s.as_bytes()).await
    }

    /// `CSInteractNPCPacket`.
    pub async fn interact_npc(&mut self, npc_obj: u32, target_changed: bool) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_bc(npc_obj).write_bool(target_changed);
        self.send_cs(cs::CSInteractNPCPacket, s.as_bytes()).await
    }

    /// `CSStartInteractionPacket` (asks the NPC for its interaction skill list).
    pub async fn start_interaction(&mut self, npc_obj: u32) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_bc(npc_obj)
            .write_bc(npc_obj)
            .write_i32(0) // extra info
            .write_i32(0) // pick id
            .write_u8(1) // mouse button
            .write_i32(0); // modifier keys
        self.send_cs(cs::CSStartInteractionPacket, s.as_bytes())
            .await
    }

    /// Select the unit used by auto attacks, as the real client does before attacking.
    pub async fn change_target(&mut self, target_obj: u32) -> anyhow::Result<()> {
        let mut packet = PacketStream::new();
        packet.write_bc(target_obj);
        self.send_cs(cs::CSChangeTargetPacket, packet.as_bytes())
            .await
    }

    /// `CSSwapItemsPacket` (bag -> bag).
    pub async fn swap_items(
        &mut self,
        from_item: u64,
        from_slot: u8,
        to_item: u64,
        to_slot: u8,
    ) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u64(from_item)
            .write_u64(to_item)
            .write_u8(2)
            .write_u8(from_slot)
            .write_u8(2)
            .write_u8(to_slot);
        self.send_cs(cs::CSSwapItemsPacket, s.as_bytes()).await
    }

    /// `CSStartQuestContextPacket` without an NPC / doodad / sphere.
    pub async fn start_quest(&mut self, quest_id: u32, npc_obj: u32) -> anyhow::Result<()> {
        let mut s = PacketStream::new();
        s.write_u32(quest_id)
            .write_bc(npc_obj)
            .write_bc(0)
            .write_u32(0);
        self.send_cs(cs::CSStartQuestContextPacket, s.as_bytes())
            .await
    }

    /// `CSLeaveWorldPacket` (0 = quit, 1 = character select, 2 = server select).
    pub async fn leave_world(&mut self, target: u8) -> anyhow::Result<()> {
        self.send_cs(cs::CSLeaveWorldPacket, &[target]).await
    }

    /// Closest unit of `unit_type` to our position.
    pub fn nearest_unit(&self, unit_type: u8) -> Option<UnitInfo> {
        let me = self.state.self_pos;
        self.state
            .units
            .values()
            .filter(|u| u.unit_type == unit_type && u.obj_id != self.state.self_obj)
            .min_by(|a, b| dist2(a.pos, me).total_cmp(&dist2(b.pos, me)))
            .cloned()
    }

    /// A unit of `unit_type` within `range` picked independently of object ids and wandering:
    /// the lowest template / character id, then the closest. Used where two servers must be
    /// driven identically (parity runs).
    pub fn stable_unit(&self, unit_type: u8, range: f32) -> Option<UnitInfo> {
        let me = self.state.self_pos;
        self.state
            .units
            .values()
            .filter(|u| {
                u.unit_type == unit_type
                    && u.obj_id != self.state.self_obj
                    && dist2(u.pos, me) <= range * range
            })
            .min_by(|a, b| {
                a.id.cmp(&b.id)
                    .then(dist2(a.pos, me).total_cmp(&dist2(b.pos, me)))
            })
            .cloned()
    }
}

fn dist2(a: (f32, f32, f32), b: (f32, f32, f32)) -> f32 {
    let (dx, dy, dz) = (a.0 - b.0, a.1 - b.1, a.2 - b.2);
    dx * dx + dy * dy + dz * dz
}

fn facing_direction(pos: (f32, f32, f32), target: (f32, f32, f32)) -> i8 {
    let (dx, dy) = (target.0 - pos.0, target.1 - pos.1);
    if dx == 0.0 && dy == 0.0 {
        return 0;
    }
    let yaw = (f64::from(dy).atan2(f64::from(dx)) - std::f64::consts::FRAC_PI_2
        + std::f64::consts::PI)
        .rem_euclid(std::f64::consts::TAU)
        - std::f64::consts::PI;
    aaemu_game::models::movement::convert_radian_to_direction(yaw)
}

/// `UnitCustomModelParams` with a full face block (type 3), all defaults.
fn write_custom_model(s: &mut PacketStream) {
    s.write_u8(3) // UnitCustomModelType.Face
        .write_u32(0) // hair color
        .write_u32(0) // skin color
        .write_u32(0); // model id
                       // UnitCustomFace
    s.write_u32(0)
        .write_f32(0.0)
        .write_f32(0.0)
        .write_f32(0.0)
        .write_i16(0)
        .write_i16(0);
    for _ in 0..4 {
        s.write_u32(0).write_f32(0.0); // fixed decal assets
    }
    for _ in 0..3 {
        s.write_u32(0); // diffuse, normal, eyelash maps
    }
    s.write_f32(0.0); // normal map weight
    for _ in 0..5 {
        s.write_u32(0); // lip, pupils, eyebrow, deco colors
    }
    s.write_bytes_sized(&[0u8; 128]); // modifier
}

#[cfg(test)]
mod facing_tests {
    use super::*;

    #[test]
    fn movement_faces_each_compass_target_using_the_1_2_yaw() {
        let origin = (0.0, 0.0, 0.0);
        for (target, direction) in [
            ((0.0, 1.0, 0.0), 0),
            ((-1.0, 0.0, 0.0), 32),
            ((1.0, 0.0, 0.0), -32),
            ((0.0, -1.0, 0.0), 64),
        ] {
            assert_eq!(facing_direction(origin, target), direction);
        }
    }
}
