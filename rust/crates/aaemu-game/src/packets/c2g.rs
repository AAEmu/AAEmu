//! C->G lobby packets (`AAEmu.Game.Core.Packets.C2G`) and their behaviour
//! (`EnterWorldManager.Login`, `CSListCharacterPacket`, `CSLeaveWorldPacket`).

use aaemu_commons::helpers::unix_time_now;
use aaemu_commons::PacketStream;
use futures::future::BoxFuture;

use crate::error::{GameError, Result};
use crate::login::packets::GLPlayerReconnectPacket;
use crate::managers::LoginDecision;
use crate::models::character::Character;
use crate::net::connection::{GameConnection, GameState};
use crate::net::registry::ClientPacket;
use crate::packets::g2c::*;
use crate::packets::proxy::ChangeStatePacket;
use crate::server::GameServer;

/// `X2EnterWorldPacket` (level 1, 0x000).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct X2EnterWorldPacket {
    pub p_from: u32,
    pub p_to: u32,
    pub account_id: u32,
    pub cookie: u32,
    pub zone_id: i32,
    pub tb: u8,
    pub revision: u64,
}

impl ClientPacket for X2EnterWorldPacket {
    const NAME: &'static str = "X2EnterWorldPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            p_from: s.read_u32()?,
            p_to: s.read_u32()?,
            account_id: s.read_u32()?,
            cookie: s.read_u32()?,
            zone_id: s.read_i32()?,
            tb: s.read_u8()?,
            revision: s.read_u64()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move { enter_world_login(server, conn, self.account_id, self.cookie).await })
    }
}

/// `EnterWorldManager.Login(connection, accountId, token)`.
pub async fn enter_world_login(
    server: &GameServer,
    conn: &mut GameConnection,
    account_id: u32,
    token: u32,
) -> Result<()> {
    if server.is_shutting_down() {
        crate::world::session::refuse_during_shutdown(conn);
        return Ok(());
    }
    match server.enter_world.login(account_id, token) {
        LoginDecision::Accepted => {}
        LoginDecision::AccountMismatch { expected } => {
            // TODO (C#): Token did not match the expected account (phishing attempt?)
            tracing::warn!(
                "Token does not match expected account, Token: {token}, AccountId: {account_id}, Expected AccountId: {expected}, IP: {}",
                conn.ip
            );
            conn.shutdown();
            return Ok(());
        }
        LoginDecision::InvalidToken => {
            // TODO (C#): Invalid token (hacking attempt?)
            tracing::warn!(
                "Invalid token during login attempt, Token: {token}, AccountId: {account_id}, IP: {}",
                conn.ip
            );
            conn.shutdown();
            return Ok(());
        }
    }

    conn.set_account_id(account_id);
    conn.state = GameState::Lobby;

    account_manager_add(server, conn).await;
    if !server.streams.add_token(account_id, conn.id) {
        // StreamManager.AddToken: Dictionary.Add throws for a connection that already entered.
        return Err(GameError::Protocol(format!(
            "stream token for connection {} already registered",
            conn.id
        )));
    }

    let gm = false; // connection.GetAttribute("gmFlag") != null — never set in 1.2
    conn.send(&X2EnterWorldResponsePacket {
        reason: 0,
        gm,
        token: conn.id,
        port: server.config.stream_network.port,
        world_time: unix_time_now(),
    });
    conn.send(&ChangeStatePacket { state: 0 });
    Ok(())
}

/// `AccountManager.Add(connection)` (timed rewards included: `systems::services::accounts`).
async fn account_manager_add(server: &GameServer, conn: &GameConnection) {
    crate::systems::services::accounts::add(server, conn).await;
}

/// Sends `SCAccountInfoPacket` built from `Connection.Payment`.
fn send_account_info(conn: &mut GameConnection) {
    let p = conn.payment.clone();
    conn.send(&SCAccountInfoPacket {
        pay_method: p.method as i32,
        pay_location: p.location,
        pay_start: p.start_time,
        pay_end: p.end_time,
    });
}

/// `Connection.LoadAccount()`: characters of the account (houses are a later phase).
async fn load_account(server: &GameServer, conn: &mut GameConnection) -> Result<()> {
    // TODO (C#): Load payment and account tier information
    conn.characters.clear();
    let (characters, packets) =
        crate::db_character::load_account_characters(server, conn.account_id()).await?;
    for p in &packets {
        conn.send(p.as_ref());
    }
    conn.characters = characters;
    // Houses.Clear(); HousingManager.GetByAccountId(Houses, AccountId): read by
    // `systems::housing::send_login_houses` / `send_my_houses` when the packets are sent.
    Ok(())
}

/// The part of `CSListCharacterPacket.Read` after `LoadAccount()`: race congestion, the character
/// list in chunks of two (the `SCLoginCharInfoHouse` packets are sent by the caller).
pub fn send_character_list(conn: &mut GameConnection, characters: &[Character]) {
    conn.send(&SCRaceCongestionPacket);
    for packet in SCCharacterListPacket::chunked(characters) {
        conn.send(&packet);
    }
    // foreach house in Connection.Houses: SCLoginCharInfoHouse(house.OwnerId, house) follows
    // (`systems::housing::send_login_houses`).
}

/// `CSListCharacterPacket` (level 1, 0x020).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSListCharacterPacket {
    pub size: i32,
    pub data: Vec<u8>,
}

impl ClientPacket for CSListCharacterPacket {
    const NAME: &'static str = "CSListCharacterPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            size: s.read_i32()?,         // TODO (C#) max size 4096
            data: s.read_bytes_sized()?, // TODO (C#) or string?
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            conn.send(&SCGetSlotCountPacket { sc: 0 });
            send_account_info(conn);
            load_account(server, conn).await?;
            let characters = std::mem::take(&mut conn.characters);
            send_character_list(conn, &characters);
            crate::systems::housing::send_login_houses(server, conn);
            conn.characters = characters;
            Ok(())
        })
    }
}

/// `LeaveWorldTargetType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LeaveWorldTargetType {
    QuitGame,
    CharacterSelect,
    ServerSelect,
    Unknown(u8),
}

impl From<u8> for LeaveWorldTargetType {
    fn from(v: u8) -> Self {
        match v {
            0 => Self::QuitGame,
            1 => Self::CharacterSelect,
            2 => Self::ServerSelect,
            o => Self::Unknown(o),
        }
    }
}

/// `CSLeaveWorldPacket` (level 1, 0x001) -> `EnterWorldManager.Leave`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSLeaveWorldPacket {
    pub target: LeaveWorldTargetType,
}

impl ClientPacket for CSLeaveWorldPacket {
    const NAME: &'static str = "CSLeaveWorldPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            target: s.read_u8()?.into(),
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            match self.target {
                LeaveWorldTargetType::QuitGame => {
                    crate::world::session::leave(server, conn, 0);
                }
                LeaveWorldTargetType::CharacterSelect => {
                    crate::world::session::leave(server, conn, 1);
                }
                LeaveWorldTargetType::ServerSelect => {
                    if conn.state == GameState::Lobby {
                        server.login.send(&GLPlayerReconnectPacket {
                            gs_id: server.config.id,
                            account_id: conn.account_id(),
                            connection_id: conn.id,
                        });
                    }
                }
                LeaveWorldTargetType::Unknown(v) => tracing::warn!("[Leave] Unknown type: {v}"),
            }
            Ok(())
        })
    }
}

/// Registers the C->S packets of this module (enter world, leave world, character list).
pub fn register(r: &mut crate::net::registry::PacketRegistry) {
    use crate::net::packet::LEVEL_PLAIN;
    use crate::opcodes::cs;
    r.register::<X2EnterWorldPacket>(LEVEL_PLAIN, cs::X2EnterWorldPacket)
        .register::<CSLeaveWorldPacket>(LEVEL_PLAIN, cs::CSLeaveWorldPacket)
        .register::<CSListCharacterPacket>(LEVEL_PLAIN, cs::CSListCharacterPacket);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_enter_world() {
        let mut s = PacketStream::new();
        s.write_u32(1)
            .write_u32(2)
            .write_u32(3)
            .write_u32(4)
            .write_i32(-1)
            .write_u8(5)
            .write_u64(6);
        let p = X2EnterWorldPacket::read(&mut s).unwrap();
        assert_eq!(
            p,
            X2EnterWorldPacket {
                p_from: 1,
                p_to: 2,
                account_id: 3,
                cookie: 4,
                zone_id: -1,
                tb: 5,
                revision: 6
            }
        );
    }

    #[test]
    fn reads_list_character() {
        let mut s = PacketStream::new();
        s.write_i32(3).write_bytes_sized(&[7, 8, 9]);
        assert_eq!(
            CSListCharacterPacket::read(&mut s).unwrap(),
            CSListCharacterPacket {
                size: 3,
                data: vec![7, 8, 9]
            }
        );
    }
}
