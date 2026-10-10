//! Shared server state (the C# singletons used by this phase) and the frame dispatcher
//! (`GameProtocolHandler.OnReceive` after reassembly).

use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Arc;

use sqlx::mysql::MySqlPool;

use crate::config::AppConfig;
use crate::data::GameData;
use crate::error::{GameError, Result};
use crate::login::LoginLink;
use crate::managers::chat::ChatManager;
use crate::managers::expedition::ExpeditionManager;
use crate::managers::friend::FriendManager;
use crate::managers::id::IdManagers;
use crate::managers::item::ItemManager;
use crate::managers::name::NameManager;
use crate::managers::save::SaveManager;
use crate::managers::time::TimeManager;
use crate::managers::{AccountManager, EnterWorldManager, StreamManager};
use crate::net::connection::{ConnectionTable, GameConnection};
use crate::net::packet::{decode_frame, LEVEL_PLAIN, LEVEL_PROXY};
use crate::net::registry::PacketRegistry;
use crate::opcodes;
use crate::world::WorldManager;

pub struct GameServer {
    pub config: crate::config::ConfigCell,
    pub db: MySqlPool,
    pub game_data: Arc<GameData>,
    pub registry: PacketRegistry,
    pub connections: ConnectionTable,
    pub enter_world: EnterWorldManager,
    pub accounts: AccountManager,
    pub streams: StreamManager,
    pub login: LoginLink,
    /// The `Core/Managers/Id` singletons.
    pub ids: Arc<IdManagers>,
    pub names: Arc<NameManager>,
    /// `ItemManager` (user items / containers).
    pub items: Arc<ItemManager>,
    pub world: Arc<WorldManager>,
    pub chat: ChatManager,
    pub time: TimeManager,
    pub expeditions: ExpeditionManager,
    pub friends: FriendManager,
    pub save: SaveManager,
    /// The phase-3 subsystems' managers and behaviour hooks (`systems`).
    pub systems: crate::systems::Systems,
    /// A weak self reference for the timers that outlive a packet handler.
    self_ref: std::sync::OnceLock<std::sync::Weak<GameServer>>,
    next_connection_id: AtomicU32,
}

impl GameServer {
    pub fn new(config: AppConfig, db: MySqlPool, game_data: GameData) -> Self {
        let ids = Arc::new(IdManagers::default());
        let world = Arc::new(WorldManager::default());
        // WorldManager.CreateStaticInstances: main_world as instance 0 (dungeon instances from
        // the configuration are a later phase).
        if let Some(t) = game_data.worlds.get_world_template_by_name("main_world") {
            world.create_world_instance(
                Arc::new(t.clone()),
                crate::models::character::DEFAULT_INSTANCE_ID,
            );
        }
        Self {
            names: Arc::new(NameManager::new(config.character_name_regex.as_deref())),
            chat: ChatManager::new(&ids.chat),
            ids,
            items: Arc::new(ItemManager::default()),
            world,
            time: TimeManager::default(),
            expeditions: ExpeditionManager::default(),
            friends: FriendManager::default(),
            save: SaveManager::default(),
            systems: crate::systems::Systems::default(),
            config: config.into(),
            db,
            game_data: Arc::new(game_data),
            registry: crate::packets::build_registry(),
            connections: ConnectionTable::default(),
            enter_world: EnterWorldManager::default(),
            accounts: AccountManager::default(),
            streams: StreamManager::default(),
            login: LoginLink::default(),
            self_ref: std::sync::OnceLock::new(),
            next_connection_id: AtomicU32::new(1),
        }
    }

    /// Registers the shared handle (call once after wrapping the server in an `Arc`).
    pub fn set_self(self: &Arc<Self>) {
        let _ = self.self_ref.set(Arc::downgrade(self));
    }

    /// The shared handle registered with [`GameServer::set_self`].
    pub fn self_arc(&self) -> Option<Arc<GameServer>> {
        self.self_ref.get().and_then(std::sync::Weak::upgrade)
    }

    /// The context the visibility packets are built with (`world::ViewCtx`).
    pub fn view_ctx(&self) -> crate::world::ViewCtx<'_> {
        crate::world::ViewCtx {
            data: &self.game_data,
            names: &self.names,
            now_ms: crate::world::now_ms(),
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
            Some(reg) => {
                let started = std::time::Instant::now();
                let res = (reg.handler)(self, conn, raw.body).await;
                let took = started.elapsed();
                if took > std::time::Duration::from_millis(250) {
                    let names = if raw.level == LEVEL_PROXY {
                        opcodes::pp::NAMES
                    } else {
                        opcodes::cs::NAMES
                    };
                    tracing::warn!(
                        "Slow packet handler: {} took {} ms (session {})",
                        opcodes::name_of(names, raw.opcode).unwrap_or("?"),
                        took.as_millis(),
                        conn.id
                    );
                }
                res
            }
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
