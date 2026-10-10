//! Shared helpers for flow tests that need a `GameServer` without MySQL (see
//! `CONTRIBUTING-PORT.md`, "Testing"). The pool points at a closed port and is never used
//! unless a test calls into the DB; creating it needs a Tokio runtime (`#[tokio::test]`).

use std::net::SocketAddr;
use std::time::Duration;

use sqlx::mysql::MySqlPoolOptions;
use tokio::sync::mpsc;

use crate::config::AppConfig;
use crate::data::skills::{BuffKind, BuffTemplate};
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::models::char_data::VisualOptions;
use crate::models::character::{Character, Inventory};
use crate::net::connection::{ConnCommand, GameConnection};
use crate::world::unit_state::UNIT_STATE_FORCED_BUFFS;
use crate::world::{Player, PlayerRef};
use crate::GameServer;

/// Zone key of the synthetic `main_world`.
pub const TEST_ZONE: u32 = 100;

/// A server with game data `data` plus a synthetic 2x2-cell `main_world` (32x32 sectors of
/// 64 m, zone [`TEST_ZONE`] everywhere) and the two buff templates `SCUnitStatePacket` forces
/// on characters.
pub fn test_server_with(mut data: GameData) -> GameServer {
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1; // nothing listens here
    let db = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    data.worlds = WorldTemplates::from_templates(
        vec![WorldTemplate::synthetic(0, "main_world", 2, 2, TEST_ZONE)],
        Vec::new(),
    )
    .expect("synthetic world");
    for id in UNIT_STATE_FORCED_BUFFS {
        let mut t = BuffTemplate::simple(id, BuffKind::Good);
        t.duration = 0;
        data.skills.buffs.insert(id, t);
    }
    GameServer::new(config, db, data)
}

/// [`test_server_with`] with empty game data.
pub fn test_server() -> GameServer {
    test_server_with(GameData::empty())
}

/// A connection whose outgoing frames can be inspected.
pub struct TestClient {
    pub conn: GameConnection,
    out: mpsc::UnboundedReceiver<Vec<u8>>,
    _cmd: mpsc::UnboundedReceiver<ConnCommand>,
}

impl TestClient {
    pub fn new(server: &GameServer, id: u32, account: u32) -> Self {
        let (out_tx, out) = mpsc::unbounded_channel();
        let (cmd_tx, cmd) = mpsc::unbounded_channel();
        let peer: SocketAddr = "127.0.0.1:50000".parse().expect("addr");
        let (mut conn, handle) = GameConnection::new(id, peer, out_tx, cmd_tx);
        conn.set_account_id(account);
        server.connections.add(handle);
        Self {
            conn,
            out,
            _cmd: cmd,
        }
    }

    /// Drained frames as (opcode, body); level-2 frames are tagged `0x8000 | opcode`.
    pub fn drain(&mut self) -> Vec<(u16, Vec<u8>)> {
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

    /// The `ConnCommand`s queued for this connection since the last call.
    pub fn commands(&mut self) -> Vec<ConnCommand> {
        let mut v = Vec::new();
        while let Ok(c) = self._cmd.try_recv() {
            v.push(c);
        }
        v
    }

    /// Opcodes of the drained frames.
    pub fn drain_ops(&mut self) -> Vec<u16> {
        self.drain().into_iter().map(|f| f.0).collect()
    }
}

/// A level-10 character at (x, y, 100) with visual options (so `SCUnitStatePacket` is complete).
pub fn test_character(id: u32, name: &str, x: f32, y: f32) -> Character {
    let mut c = Character::new(id, 1000 + id, Default::default());
    c.unit.name = name.into();
    c.race = 1;
    c.gender = 1;
    c.unit.level = 10;
    c.unit.faction_id = 101;
    c.num_inventory_slots = 50;
    c.num_bank_slots = 50;
    c.inventory = Inventory::standalone(id, 50, 50);
    c.unit.transform.set_xyz(x, y, 100.0);
    c.unit.transform.zone_id = TEST_ZONE;
    c.visual_options = Some(VisualOptions {
        flag: 1,
        stp: Some([0; 6]),
        ..VisualOptions::default()
    });
    c
}

/// A player handle for `character` on `client`'s connection, registered server-wide and
/// spawned (`Character.Spawn`) in instance 0.
pub fn spawn_player(
    server: &GameServer,
    client: &TestClient,
    mut character: Character,
    obj_id: u32,
) -> PlayerRef {
    character.obj_id = obj_id;
    let p = Player::new(character, client.conn.handle());
    server.world.try_add_character(&p);
    let world = server.world.world(0).expect("instance 0");
    world.spawn(&p, &server.view_ctx());
    p
}
