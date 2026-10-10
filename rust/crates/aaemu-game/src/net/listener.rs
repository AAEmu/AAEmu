//! `GameNetwork.Start` + the per-session parts of `GameProtocolHandler`
//! (`OnConnect` / `OnReceive` / `OnDisconnect`).

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use aaemu_commons::net::FrameCodec;
use futures::{SinkExt, StreamExt};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::mpsc;
use tokio_util::codec::{FramedRead, FramedWrite};

use crate::net::connection::{ConnCommand, GameConnection};
use crate::server::GameServer;

/// Binds `Network.Host:Port` (`"*"` = any) and serves connections until the task is dropped.
pub async fn run(server: Arc<GameServer>) -> std::io::Result<()> {
    let cfg = &server.config.network;
    let host = if cfg.host == "*" {
        "0.0.0.0"
    } else {
        cfg.host.as_str()
    };
    let listener = TcpListener::bind((host, cfg.port)).await?;
    tracing::info!("Network started on {}", listener.local_addr()?);
    loop {
        let (socket, peer) = listener.accept().await?;
        let server = server.clone();
        tokio::spawn(async move { serve(server, socket, peer).await });
    }
}

/// Drives one client connection: a reader/dispatcher loop owning the [`GameConnection`] plus a
/// writer task fed by an mpsc channel.
pub async fn serve(server: Arc<GameServer>, socket: TcpStream, peer: SocketAddr) {
    let id = server.next_connection_id();
    tracing::info!("Connect from {} established, session id: {id}", peer.ip());
    let _ = socket.set_nodelay(true);
    let (rd, wr) = socket.into_split();
    let mut reader = FramedRead::new(rd, FrameCodec);

    let (out_tx, mut out_rx) = mpsc::unbounded_channel::<Vec<u8>>();
    let writer = tokio::spawn(async move {
        let mut sink = FramedWrite::new(wr, FrameCodec);
        while let Some(frame) = out_rx.recv().await {
            if let Err(e) = sink.send(frame).await {
                tracing::debug!("write to session {id} failed: {e}");
                break;
            }
        }
    });

    let (cmd_tx, mut cmd_rx) = mpsc::unbounded_channel::<ConnCommand>();
    let (mut conn, handle) = GameConnection::new(id, peer, out_tx, cmd_tx);
    server.connections.add(handle);

    while !conn.is_closing() {
        tokio::select! {
            frame = reader.next() => match frame {
                Some(Ok(payload)) => {
                    if let Err(e) = server.handle_frame(&mut conn, &payload).await {
                        // GameProtocolHandler.OnReceive catch-all: connection.Shutdown()
                        tracing::error!("session {id}: {e}");
                        break;
                    }
                }
                Some(Err(e)) => {
                    tracing::debug!("session {id}: read error: {e}");
                    break;
                }
                None => break,
            },
            cmd = cmd_rx.recv() => match cmd {
                Some(ConnCommand::Send(packet)) => conn.send(packet.as_ref()),
                Some(ConnCommand::LeaveWorld(target)) => {
                    let active = conn.active_char.clone();
                    crate::world::session::leave_world_task(&server, Some(&mut conn), target, active).await;
                }
                Some(ConnCommand::Close) | None => break,
            },
        }
    }

    on_disconnect(&server, &mut conn).await;
    drop(conn); // closes the writer channel; queued frames are flushed first
    let _ = tokio::time::timeout(Duration::from_secs(5), writer).await;
    tracing::info!("Client from {} disconnected", peer.ip());
}

/// `GameProtocolHandler.OnDisconnect` + `GameConnection.OnDisconnect`.
pub async fn on_disconnect(server: &Arc<GameServer>, conn: &mut GameConnection) {
    let account_id = conn.account_id();
    server.accounts.remove(account_id);
    crate::world::session::on_disconnect(server, conn).await;
    server.streams.remove_token(conn.id);
    server.connections.remove(conn.id);
    // AccountManager.UpdateLoginTime(AccountId, UtcNow), fire and forget. Deviation: C# also runs
    // it for connections that never entered (AccountId 0), which inserts an `accounts` row with
    // id 0 via GetAccountDetailsInternal; that is skipped here.
    if account_id != 0 {
        let server = server.clone();
        tokio::spawn(async move {
            crate::db::update_login_time(
                &server.db,
                &server.config,
                account_id,
                chrono::Utc::now().naive_utc(),
            )
            .await;
        });
    }
}
