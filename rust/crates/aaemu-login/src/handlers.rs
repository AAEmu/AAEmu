//! Packet handlers (`AAEmu.Login.Core.PacketHandlers.C2L` / `.G2L`).

use std::sync::Arc;
use std::time::Duration;

use anyhow::Context;

use crate::auth::{
    AuthFlow, KoreaAuthFlow, KoreaStep, LoginClient, PasswordAuthFlow, ReconnectAuthFlow,
};
use crate::controllers::game::send_with_delay;
use crate::models::GsRegisterResult;
use crate::network::internal::InternalConnection;
use crate::network::login::LoginConnection;
use crate::packets::c2l::C2lPacket;
use crate::packets::g2l::G2lPacket;
use crate::packets::l2g::LgRegisterGameServer;
use crate::server::LoginServer;
use crate::services::password::Password;

/// Dispatches a client packet.
pub async fn handle_c2l(
    server: &Arc<LoginServer>,
    conn: &Arc<LoginConnection>,
    packet: C2lPacket,
) -> anyhow::Result<()> {
    match packet {
        C2lPacket::CancelEnterWorld(_) => {
            conn.cancel_enter_world();
            Ok(())
        }
        C2lPacket::ChallengeResponse2(p) => {
            conn.continue_auth(server, KoreaStep::ChallengeV2 { ch: p.ch })
                .await
        }
        C2lPacket::ChallengeResponse(p) => {
            conn.continue_auth(server, KoreaStep::ChallengeV1 { ch: p.ch, pw: p.pw })
                .await
        }
        C2lPacket::EnterWorld(p) => conn.initiate_enter_world(server, p.gs_id).await,
        C2lPacket::ListWorld(_) => {
            let list = server.games.get_world_list(conn).await?;
            let worlds = list.game_servers.iter().map(|g| g.world_info()).collect();
            conn.send_world_list(worlds, list.characters)?;
            Ok(())
        }
        C2lPacket::OtpNumber(p) => {
            conn.continue_auth(server, KoreaStep::Otp(p.otp_number))
                .await
        }
        C2lPacket::PcCertNumber(p) => {
            conn.continue_auth(server, KoreaStep::Cert(p.cert_number))
                .await
        }
        C2lPacket::RequestAuthGameOn(_) => Ok(()),
        C2lPacket::RequestAuthMailRu(p) => {
            // Convert.ToHexString: upper-case hex of the raw token.
            let token_hex = hex::encode_upper(&p.token);
            let flow = PasswordAuthFlow {
                username: Some(p.id),
                password: Some(Password::from_sha256_hex(token_hex)),
                client_ip: conn.ip(),
            };
            conn.authenticate(server, AuthFlow::Password(flow)).await
        }
        C2lPacket::RequestAuth(p) => {
            let flow = KoreaAuthFlow::new(p.account, conn.ip());
            conn.authenticate(server, AuthFlow::Korea(Box::new(flow)))
                .await
        }
        // CARequestAuthTencentPacketHandler is a no-op.
        C2lPacket::RequestAuthTencent(_) => Ok(()),
        C2lPacket::RequestAuthTrion(p) => {
            let flow = PasswordAuthFlow {
                username: p.username,
                password: p.password.map(Password::from_sha256_hex),
                client_ip: conn.ip(),
            };
            conn.authenticate(server, AuthFlow::Password(flow)).await
        }
        C2lPacket::RequestReconnect(p) => {
            let flow = ReconnectAuthFlow {
                gs_id: p.gs_id,
                account_id: p.account_id,
                token: p.cookie,
            };
            conn.authenticate(server, AuthFlow::Reconnect(flow)).await
        }
    }
}

/// Dispatches a game server packet.
pub async fn handle_g2l(
    server: &Arc<LoginServer>,
    conn: &Arc<InternalConnection>,
    packet: G2lPacket,
) -> anyhow::Result<()> {
    match packet {
        G2lPacket::GameServerLoad(p) => {
            // C#: `connection.GameServer!.Load` (NullReferenceException if not registered).
            let gs_id = conn
                .game_server()
                .context("Object reference not set to an instance of an object (GameServer)")?;
            server.games.set_load(gs_id, p.load)
        }
        G2lPacket::PlayerEnter(p) => {
            server
                .games
                .route_enter_world_response(p.connection_id, p.gs_id, p.result);
            Ok(())
        }
        G2lPacket::PlayerReconnect(p) => {
            server
                .logins
                .add_reconnection_token(conn, p.gs_id, p.account_id, p.token);
            Ok(())
        }
        G2lPacket::RegisterGameServer(p) => {
            if p.secret_key != server.config.secret_key {
                tracing::error!("Connection {}, bad secret key", conn.ip);
                send_with_delay(
                    conn.clone(),
                    Duration::from_secs(5),
                    LgRegisterGameServer {
                        result: GsRegisterResult::Error,
                    },
                );
                return Ok(());
            }
            server.games.add(p.gs_id, p.mirrors, conn);
            Ok(())
        }
        G2lPacket::RequestInfo(p) => {
            let login_connection = server.connections.get(p.connection_id);
            if !p.characters.is_empty() {
                // C#: `loginConnection!` / `connection.GameServer!` throw before ReleaseId.
                let login_connection = login_connection.context(
                    "Object reference not set to an instance of an object (login connection)",
                )?;
                let gs_id = conn
                    .game_server()
                    .context("Object reference not set to an instance of an object (GameServer)")?;
                login_connection.add_characters(gs_id, p.characters)?;
            }
            server.requests.release_id(p.request_id);
            Ok(())
        }
    }
}
