//! `GameController`: the configured game servers, their registration by game server processes,
//! world lists and enter-world routing.

use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use dashmap::DashMap;

use super::request::RequestController;
use crate::config::AppConfig;
use crate::models::{
    AccountId, ConnectionId, GameServerId, GsLoad, GsRegisterResult, LoginCharacterInfo,
};
use crate::network::internal::InternalConnection;
use crate::network::login::{LoginConnection, LoginConnectionTable};
use crate::packets::l2c::WorldInfo;
use crate::packets::l2g::{LgPlayerEnter, LgRegisterGameServer, LgRequestInfo};
use crate::packets::OutgoingPacket;

/// `TODO Request 20s` in the C# code.
const WORLD_LIST_REQUEST_TIMEOUT: Duration = Duration::from_millis(20_000);

/// A configured game server (`Models/GameServer.cs`).
#[derive(Clone, Debug)]
pub struct GameServer {
    pub id: GameServerId,
    pub name: String,
    pub host: String,
    pub port: u16,
    /// The registered game server connection (mirrors share their parent's connection).
    pub connection: Option<Arc<InternalConnection>>,
    pub load: GsLoad,
    pub mirrors: Vec<GameServerId>,
}

impl GameServer {
    pub fn new(id: GameServerId, name: String, host: String, port: u16) -> Self {
        Self {
            id,
            name,
            host,
            port,
            connection: None,
            load: GsLoad::default(),
            mirrors: Vec::new(),
        }
    }

    pub fn active(&self) -> bool {
        self.connection.is_some()
    }

    pub fn send_packet<P: OutgoingPacket>(&self, packet: &P) {
        if let Some(c) = &self.connection {
            c.send_packet(packet);
        }
    }

    pub fn world_info(&self) -> WorldInfo {
        WorldInfo {
            id: self.id,
            name: self.name.clone(),
            active: self.active(),
            load: self.load,
        }
    }
}

/// The result of [`GameController::get_world_list`].
#[derive(Clone, Debug)]
pub struct WorldListResult {
    pub game_servers: Vec<GameServer>,
    pub characters: Vec<LoginCharacterInfo>,
}

pub struct GameController {
    requests: Arc<RequestController>,
    connections: Arc<LoginConnectionTable>,
    game_servers: DashMap<GameServerId, GameServer>,
    /// mirror id -> parent id
    mirrors_id: Mutex<HashMap<GameServerId, GameServerId>>,
}

/// Resolves `host` to its first IPv4 address; on failure the host is returned un-parsed.
async fn resolve_host_name(host: &str) -> String {
    match tokio::net::lookup_host((host, 0)).await {
        Ok(addrs) => {
            if let Some(v4) = addrs.map(|a| a.ip()).find(IpAddr::is_ipv4) {
                tracing::debug!("Resolved {} to {}", host, v4);
                return v4.to_string();
            }
            tracing::warn!("Unable to resolve {} to an IPv4 address", host);
            host.to_string()
        }
        Err(e) => {
            // in case of errors, just return it un-parsed
            tracing::error!("Exception resolving {}: {}", host, e);
            host.to_string()
        }
    }
}

impl GameController {
    pub fn new(requests: Arc<RequestController>, connections: Arc<LoginConnectionTable>) -> Self {
        Self {
            requests,
            connections,
            game_servers: DashMap::new(),
            mirrors_id: Mutex::new(HashMap::new()),
        }
    }

    pub fn try_get_parent_id(&self, gs_id: GameServerId) -> Option<GameServerId> {
        self.mirrors_id.lock().unwrap().get(&gs_id).copied()
    }

    /// Loads the non-hidden game servers from the configuration.
    pub async fn load(&self, config: &AppConfig) {
        for gs in config.game_servers.iter().filter(|gs| !gs.hidden) {
            let id = GameServerId(gs.id);
            let host = if config.skip_host_resolve {
                gs.host.clone()
            } else {
                resolve_host_name(&gs.host).await
            };
            let server = GameServer::new(id, gs.name.clone(), host.clone(), gs.port);
            if self.game_servers.contains_key(&id) {
                tracing::error!(
                    "Game server {} ({}) has been defined more than once!",
                    id,
                    server.name
                );
            } else {
                self.game_servers.insert(id, server);
            }
            let extra = if host != gs.host {
                format!("from {}", gs.host)
            } else if config.skip_host_resolve {
                " (unresolved)".to_string()
            } else {
                String::new()
            };
            tracing::info!(
                "Game Server {}: {} -> {}:{} {}",
                id,
                gs.name,
                host,
                gs.port,
                extra
            );
        }
        if self.game_servers.is_empty() {
            tracing::error!("No game servers have been defined!");
            return;
        }
        tracing::info!("Loaded {} game server(s)", self.game_servers.len());
    }

    /// Registers a game server connection (`GLRegisterGameServerPacket`).
    pub fn add(
        &self,
        gs_id: GameServerId,
        mirrors: Vec<GameServerId>,
        connection: &Arc<InternalConnection>,
    ) {
        {
            let Some(mut gs) = self.game_servers.get_mut(&gs_id) else {
                tracing::error!(
                    "GameServer connection from {} is requesting an invalid WorldId {}",
                    connection.ip,
                    gs_id
                );
                send_with_delay(
                    connection.clone(),
                    Duration::from_secs(5),
                    LgRegisterGameServer {
                        result: GsRegisterResult::Error,
                    },
                );
                return;
            };
            gs.connection = Some(connection.clone());
            gs.mirrors.extend(mirrors.iter().copied());
            connection.set_game_server(gs_id);
            gs.send_packet(&LgRegisterGameServer {
                result: GsRegisterResult::Success,
            });
        }

        for mirror_id in mirrors {
            // C#: `_gameServers[mirrorId]` (KeyNotFoundException) and `_mirrorsId.Add` (duplicate
            // key ArgumentException) abort the remaining registration; mirrored here.
            match self.game_servers.get_mut(&mirror_id) {
                Some(mut mirror) => mirror.connection = Some(connection.clone()),
                None => {
                    tracing::error!(
                        "Error on packet dispatch: mirror game server {} is not configured",
                        mirror_id
                    );
                    return;
                }
            }
            let mut map = self.mirrors_id.lock().unwrap();
            if map.contains_key(&mirror_id) {
                tracing::error!(
                    "Error on packet dispatch: mirror {} is already registered",
                    mirror_id
                );
                return;
            }
            map.insert(mirror_id, gs_id);
        }

        if let Some(gs) = self.game_servers.get(&gs_id) {
            tracing::info!(
                "Registered GameServer {} ({}) from {}",
                gs.id,
                gs.name,
                connection.ip
            );
        }
    }

    /// Unregisters a game server (its connection closed).
    pub fn remove(&self, gs_id: GameServerId) {
        let mirrors = {
            let Some(mut gs) = self.game_servers.get_mut(&gs_id) else {
                return;
            };
            gs.connection = None;
            std::mem::take(&mut gs.mirrors)
        };
        for mirror_id in mirrors {
            if let Some(mut server) = self.game_servers.get_mut(&mirror_id) {
                server.connection = None;
            }
            self.mirrors_id.lock().unwrap().remove(&mirror_id);
        }
    }

    fn snapshot(&self) -> Vec<GameServer> {
        let mut list: Vec<GameServer> = self
            .game_servers
            .iter()
            .map(|e| e.value().clone())
            .collect();
        // ConcurrentDictionary enumeration order is unspecified in C#; use id order.
        list.sort_by_key(|g| g.id);
        list
    }

    /// Collects the world list for a client, first asking every active game server for the
    /// account's characters (unless already known) and waiting for the answers (or timeouts).
    pub async fn get_world_list(
        &self,
        connection: &LoginConnection,
    ) -> anyhow::Result<WorldListResult> {
        let servers = self.snapshot();
        if servers.iter().any(GameServer::active) {
            let (request_ids, wait) = self
                .requests
                .create(servers.len(), WORLD_LIST_REQUEST_TIMEOUT)?;
            for (gs, &request_id) in servers.iter().zip(&request_ids) {
                if !gs.active() || connection.has_characters(gs.id) {
                    self.requests.release_id(request_id);
                    continue;
                }
                gs.send_packet(&LgRequestInfo {
                    connection_id: connection.id(),
                    request_id,
                    account_id: connection.account_id(),
                });
            }
            wait.await;
        }
        // The C# list holds the live GameServer objects, so state is read after the wait.
        let game_servers = servers
            .iter()
            .filter_map(|s| self.get_game_server(s.id))
            .collect();
        Ok(WorldListResult {
            game_servers,
            characters: connection.get_characters(),
        })
    }

    pub fn set_load(&self, gs_id: GameServerId, load: GsLoad) -> anyhow::Result<()> {
        let mut gs = self.game_servers.get_mut(&gs_id).ok_or_else(|| {
            anyhow::anyhow!("The given key '{gs_id}' was not present in the dictionary.")
        })?;
        gs.load = load;
        Ok(())
    }

    /// A game server by id (a snapshot).
    pub fn get_game_server(&self, gs_id: GameServerId) -> Option<GameServer> {
        self.game_servers.get(&gs_id).map(|g| g.clone())
    }

    /// Sends an enter world request to the game server (fire-and-forget); the response is
    /// routed via [`Self::route_enter_world_response`].
    pub fn request_enter_world(
        &self,
        account_id: AccountId,
        connection_id: ConnectionId,
        gs_id: GameServerId,
    ) {
        let Some(gs) = self.get_game_server(gs_id) else {
            tracing::warn!("RequestEnterWorld: game server {} not found", gs_id);
            return;
        };
        if !gs.active() {
            tracing::warn!("RequestEnterWorld: game server {} not active", gs_id);
            return;
        }
        gs.send_packet(&LgPlayerEnter {
            account_id,
            connection_id,
        });
    }

    /// Routes a `GLPlayerEnterPacket` result to the client connection's session.
    pub fn route_enter_world_response(
        &self,
        connection_id: ConnectionId,
        gs_id: GameServerId,
        result: u8,
    ) {
        let Some(connection) = self.connections.get(connection_id) else {
            tracing::warn!(
                "RouteEnterWorldResponse: connection {} not found",
                connection_id
            );
            return;
        };
        connection.complete_enter_world_request(self, gs_id, result);
    }
}

pub(crate) fn send_with_delay<P: OutgoingPacket + Send + 'static>(
    connection: Arc<InternalConnection>,
    delay: Duration,
    packet: P,
) {
    tokio::spawn(async move {
        tokio::time::sleep(delay).await;
        connection.send_packet(&packet);
    });
}

#[cfg(test)]
mod tests;
