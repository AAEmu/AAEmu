//! Test harness for the GM commands: a server with every subsystem's hooks installed and the
//! scripts loaded, GM players on test connections, and [`run`] (a chat command through
//! `CommandManager.Handle` at a given access level).

use std::sync::Arc;

use crate::data::GameData;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::services::commands::output::{
    CharacterMessageOutput, MessageOutput, PlayerCharacter,
};
use crate::systems::services::commands::CommandContext;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::PlayerRef;

use super::ScriptEnv;

/// A server with `data`, every subsystem's hooks (as `systems::start_all` installs them) and
/// the command scripts loaded (`ScriptReflector.Reflect`).
pub fn server_with(data: GameData) -> Arc<GameServer> {
    let server = Arc::new(test_server_with(data));
    server.set_self();
    crate::systems::skills::bind(&server);
    crate::systems::npcs::install_hooks(&server);
    crate::systems::social::install_hooks(&server);
    crate::systems::quests::install_hooks(&server);
    crate::systems::vehicles::install_hooks(&server);
    crate::systems::economy::wiring::install(&server);
    crate::systems::economy::bridges::install(&server);
    let commands = &server.systems.services.commands;
    super::install(commands, ScriptEnv::default());
    assert!(commands.reload());
    server
}

/// [`server_with`] with empty game data.
pub fn server() -> Arc<GameServer> {
    server_with(GameData::empty())
}

/// A player on its own test connection.
pub struct Pc {
    pub client: TestClient,
    pub p: PlayerRef,
}

impl Pc {
    /// The `SCChatMessagePacket` texts this player received since the last drain (other frames
    /// are dropped).
    pub fn chat(&mut self) -> Vec<String> {
        self.client
            .drain()
            .into_iter()
            .filter(|(op, _)| *op == sc::SCChatMessagePacket)
            .map(|(_, body)| chat_text(&body))
            .collect()
    }

    /// The frames other than `SCChatMessagePacket` since the last drain (opcode, body).
    pub fn packets(&mut self) -> Vec<(u16, Vec<u8>)> {
        self.client
            .drain()
            .into_iter()
            .filter(|(op, _)| *op != sc::SCChatMessagePacket)
            .collect()
    }

    /// Every frame since the last drain (opcode, body).
    pub fn frames(&mut self) -> Vec<(u16, Vec<u8>)> {
        self.client.drain()
    }
}

/// The message of an `SCChatMessagePacket` body.
pub fn chat_text(body: &[u8]) -> String {
    let mut pos = 21;
    let name_len = u16::from_le_bytes([body[pos], body[pos + 1]]) as usize;
    pos += 2 + name_len;
    let len = u16::from_le_bytes([body[pos], body[pos + 1]]) as usize;
    String::from_utf8_lossy(&body[pos + 2..pos + 2 + len]).into_owned()
}

/// A level-10 player `name` (character id `id`, obj id `0x100 + id`) at (x, y, 100), online,
/// spawned in instance 0, with 5000 hp / 1000 mp.
pub fn pc(server: &GameServer, id: u32, name: &str, x: f32, y: f32) -> Pc {
    let mut client = TestClient::new(server, id, 1000 + id);
    let mut c = test_character(id, name, x, y);
    c.unit.hp = 5_000;
    c.unit.max_hp = 5_000;
    c.unit.mp = 1_000;
    c.unit.max_mp = 1_000;
    c.is_online = true;
    c.access_level = 100;
    let p = spawn_player(server, &client, c, 0x100 + id);
    client.conn.active_char = Some(p.clone());
    client.drain();
    Pc { client, p }
}

/// `CommandManager.Handle(character, text)` with access level 100 (the text after `/`).
pub async fn run(server: &GameServer, p: &PlayerRef, text: &str) -> (bool, Vec<String>) {
    run_at(server, p, text, 100).await
}

/// [`run`] at `level`.
pub async fn run_at(
    server: &GameServer,
    p: &PlayerRef,
    text: &str,
    level: i32,
) -> (bool, Vec<String>) {
    let ctx = CommandContext {
        server: Some(server),
        character: PlayerCharacter::arc(p),
    };
    let (handled, out): (bool, CharacterMessageOutput) = server
        .systems
        .services
        .commands
        .handle_with_level_async(&ctx, level, text)
        .await;
    (handled, out.messages().to_vec())
}

/// `SendNormalText`: `[{name}] {text}`.
pub fn normal(name: &str, text: &str) -> String {
    format!("[{name}] {text}")
}

/// `SendErrorText`: `|cFFFFFFFF[{name}]|r |cFFFF0000{text}|r`.
pub fn error(name: &str, text: &str) -> String {
    format!("|cFFFFFFFF[{name}]|r |cFFFF0000{text}|r")
}
