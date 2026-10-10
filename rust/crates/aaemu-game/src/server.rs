//! Shared server state (the C# singletons used by this phase) and the frame dispatcher
//! (`GameProtocolHandler.OnReceive` after reassembly).

use std::sync::atomic::{AtomicU32, Ordering};

use sqlx::mysql::MySqlPool;

use crate::config::AppConfig;
use crate::data::GameData;
use crate::error::{GameError, Result};
use crate::login::LoginLink;
use crate::managers::{AccountManager, EnterWorldManager, StreamManager};
use crate::net::connection::{ConnectionTable, GameConnection};
use crate::net::packet::{decode_frame, LEVEL_PLAIN, LEVEL_PROXY};
use crate::net::registry::PacketRegistry;
use crate::opcodes;

pub struct GameServer {
    pub config: AppConfig,
    pub db: MySqlPool,
    pub game_data: GameData,
    pub registry: PacketRegistry,
    pub connections: ConnectionTable,
    pub enter_world: EnterWorldManager,
    pub accounts: AccountManager,
    pub streams: StreamManager,
    pub login: LoginLink,
    next_connection_id: AtomicU32,
}

impl GameServer {
    pub fn new(config: AppConfig, db: MySqlPool, game_data: GameData) -> Self {
        Self {
            config,
            db,
            game_data,
            registry: crate::packets::build_registry(),
            connections: ConnectionTable::default(),
            enter_world: EnterWorldManager::default(),
            accounts: AccountManager::default(),
            streams: StreamManager::default(),
            login: LoginLink::default(),
            next_connection_id: AtomicU32::new(1),
        }
    }

    /// Session ids. C# uses `RemoteEndPoint.GetHashCode()`; any unique u32 serves the same purpose.
    pub fn next_connection_id(&self) -> u32 {
        self.next_connection_id.fetch_add(1, Ordering::Relaxed)
    }

    /// `LoadTask.Execute`: 0 = LOW, 1 = MIDDLE, 2 = HIGH.
    pub fn load_level(&self) -> u8 {
        let rate = self.accounts.count() as f64 / f64::from(self.config.network.num_connections);
        if rate >= 0.6 {
            2
        } else if rate >= 0.3 {
            1
        } else {
            0
        }
    }

    /// Decodes and dispatches one frame payload for `conn`.
    ///
    /// The C# handler only creates packet tables for levels 1 and 2 ("For 1.2 client we only have
    /// Level1 and Level2 packets"); `_packets[level]` on any other level throws
    /// `KeyNotFoundException` and the connection is shut down, which the returned error mirrors.
    pub async fn handle_frame(&self, conn: &mut GameConnection, payload: &[u8]) -> Result<()> {
        let raw = decode_frame(payload)?;
        if raw.level != LEVEL_PLAIN && raw.level != LEVEL_PROXY {
            return Err(GameError::Protocol(format!(
                "no packet table for level {}",
                raw.level
            )));
        }
        match self.registry.get(raw.level, raw.opcode) {
            Some(reg) => (reg.handler)(self, conn, raw.body).await,
            None => {
                let names = if raw.level == LEVEL_PROXY {
                    opcodes::pp::NAMES
                } else {
                    opcodes::cs::NAMES
                };
                let known = opcodes::name_of(names, raw.opcode)
                    .map(|n| format!(" ({n}: not ported yet)"))
                    .unwrap_or_default();
                let dump: String = raw
                    .body
                    .unread()
                    .iter()
                    .map(|b| format!("{b:02x} "))
                    .collect();
                tracing::error!(
                    "Unknown packet 0x{:02x}({}){} from {}:\n{}",
                    raw.opcode,
                    raw.level,
                    known,
                    conn.ip,
                    dump
                );
                Ok(())
            }
        }
    }
}
