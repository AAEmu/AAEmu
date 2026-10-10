//! `StreamNetwork.Start` + the per-session parts of `StreamProtocolHandler`
//! (`OnConnect` / `OnReceive` / `OnDisconnect`).

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use aaemu_commons::net::FrameCodec;
use futures::{SinkExt, StreamExt};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::mpsc;
use tokio_util::codec::{FramedRead, FramedWrite};

use crate::server::GameServer;
use crate::stream::connection::{StreamCommand, StreamConnection};
use crate::stream::protocol::handle_frame;

/// Binds `StreamNetwork.Host:Port` (`"*"` = any) and serves connections until the task is
/// dropped.
pub async fn run(server: Arc<GameServer>) -> std::io::Result<()> {
    let cfg = &server.config.stream_network;
    let host = if cfg.host == "*" {
        "0.0.0.0"
    } else {
        cfg.host.as_str()
    };
    let listener = TcpListener::bind((host, cfg.port)).await?;
    tracing::info!("StreamNetwork started on {}", listener.local_addr()?);
    serve_listener(server, listener).await
}

/// Accept loop on an already bound listener.
pub async fn serve_listener(server: Arc<GameServer>, listener: TcpListener) -> std::io::Result<()> {
    loop {
        let (socket, peer) = listener.accept().await?;
        let server = server.clone();
        tokio::spawn(async move { serve(server, socket, peer).await });
    }
}

/// Drives one stream connection: a reader/dispatcher loop owning the [`StreamConnection`] plus
/// a writer task fed by an mpsc channel.
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
                tracing::debug!("stream write to session {id} failed: {e}");
                break;
            }
        }
    });

    let (cmd_tx, mut cmd_rx) = mpsc::unbounded_channel::<StreamCommand>();
    let (mut conn, handle) = StreamConnection::new(id, peer, out_tx, cmd_tx);
    server.streams.connections.add(handle);

    while !conn.is_closing() {
        tokio::select! {
            frame = reader.next() => match frame {
                Some(Ok(payload)) => {
                    if let Err(e) = handle_frame(&server, &mut conn, &payload).await {
                        // StreamProtocolHandler.OnReceive catch-all: connection?.Shutdown()
                        tracing::error!("stream session {id}: {e}");
                        break;
                    }
                }
                Some(Err(e)) => {
                    tracing::debug!("stream session {id}: read error: {e}");
                    break;
                }
                None => break,
            },
            cmd = cmd_rx.recv() => match cmd {
                Some(StreamCommand::Send(packet)) => conn.send(packet.as_ref()),
                Some(StreamCommand::Close) | None => break,
            },
        }
    }

    // OnDisconnect: only the table entry is removed (UCC queues are left alone, as in C#).
    server.streams.connections.remove(id);
    drop(conn); // closes the writer channel; queued frames are flushed first
    let _ = tokio::time::timeout(Duration::from_secs(5), writer).await;
    tracing::info!("Client from {} disconnected", peer.ip());
}
