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
use crate::net::outbox::{outbox, OutboxReceiver};
use crate::server::GameServer;

/// Frames written to the socket buffer before one flush.
const WRITE_BATCH_FRAMES: usize = 256;
/// A batch the client does not accept within this time means it stopped reading.
const WRITE_STALL_TIMEOUT: Duration = Duration::from_secs(20);

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
/// writer task fed by its outbox ([`write_frames`]).
pub async fn serve(server: Arc<GameServer>, socket: TcpStream, peer: SocketAddr) {
    let id = server.next_connection_id();
    tracing::info!("Connect from {} established, session id: {id}", peer.ip());
    let _ = socket.set_nodelay(true);
    let (rd, wr) = socket.into_split();
    let mut reader = FramedRead::new(rd, FrameCodec);

    let (out_tx, out_rx) = outbox();
    let (cmd_tx, mut cmd_rx) = mpsc::unbounded_channel::<ConnCommand>();
    let mut writer = tokio::spawn(write_frames(
        id,
        FramedWrite::new(wr, FrameCodec),
        out_rx,
        cmd_tx.clone(),
    ));

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
                Some(ConnCommand::ReturnToWorldList) => {
                    crate::world::session::return_to_world_list(&server, &mut conn).await;
                }
                Some(ConnCommand::DetachAndClose) => {
                    conn.active_char = None;
                    break;
                }
                Some(ConnCommand::Close) | None => break,
            },
        }
    }

    on_disconnect(&server, &mut conn).await;
    drop(conn); // closes the writer channel; queued frames are flushed first
    if tokio::time::timeout(Duration::from_secs(5), &mut writer)
        .await
        .is_err()
    {
        // The client is not reading: drop what is still queued with the task.
        writer.abort();
    }
    tracing::info!("Client from {} disconnected", peer.ip());
}

/// The writer task: drains the outbox into the socket, up to [`WRITE_BATCH_FRAMES`] frames per
/// flush (one write for many small packets instead of one per packet). A write error or a batch
/// stuck for [`WRITE_STALL_TIMEOUT`] closes the connection.
async fn write_frames(
    id: u32,
    mut sink: FramedWrite<tokio::net::tcp::OwnedWriteHalf, FrameCodec>,
    mut frames: OutboxReceiver,
    commands: mpsc::UnboundedSender<ConnCommand>,
) {
    while let Some(first) = frames.recv().await {
        let batch = async {
            sink.feed(first).await?;
            for _ in 1..WRITE_BATCH_FRAMES {
                let Ok(frame) = frames.try_recv() else { break };
                sink.feed(frame).await?;
            }
            SinkExt::<bytes::Bytes>::flush(&mut sink).await
        };
        match tokio::time::timeout(WRITE_STALL_TIMEOUT, batch).await {
            Ok(Ok(())) => {}
            Ok(Err(e)) => {
                tracing::debug!("write to session {id} failed: {e}");
                break;
            }
            Err(_) => {
                tracing::warn!(
                    "session {id}: client accepted no data for {} s, closing the connection",
                    WRITE_STALL_TIMEOUT.as_secs()
                );
                break;
            }
        }
    }
    let _ = commands.send(ConnCommand::Close);
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
