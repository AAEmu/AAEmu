//! Internal network: game server connections (`InternalNetwork`, `InternalProtocolHandler`,
//! `InternalConnection`, `InternalConnectionTable`).

use std::net::{IpAddr, SocketAddr};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::{Arc, Mutex};

use aaemu_commons::net::FrameCodec;
use dashmap::DashMap;
use futures::{SinkExt, StreamExt};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::mpsc;
use tokio_util::codec::{FramedRead, FramedWrite};
use tokio_util::sync::CancellationToken;

use crate::handlers;
use crate::models::GameServerId;
use crate::packets::g2l::G2lPacket;
use crate::packets::{dump_unread, split_opcode, OutgoingPacket};
use crate::server::LoginServer;

static NEXT_SESSION_ID: AtomicU32 = AtomicU32::new(1);

/// A connection from a game server.
#[derive(Debug)]
pub struct InternalConnection {
    /// Session id (unique per connection).
    pub id: u32,
    pub ip: IpAddr,
    tx: mpsc::UnboundedSender<Vec<u8>>,
    /// The registered game server (`InternalConnection.GameServer` and the `gsId` session
    /// attribute in C#).
    game_server: Mutex<Option<GameServerId>>,
    /// Never set by the C# code; kept for parity.
    pub block: AtomicBool,
    closed: CancellationToken,
}

impl InternalConnection {
    pub fn new(ip: IpAddr, tx: mpsc::UnboundedSender<Vec<u8>>) -> Self {
        Self {
            id: NEXT_SESSION_ID.fetch_add(1, Ordering::Relaxed),
            ip,
            tx,
            game_server: Mutex::new(None),
            block: AtomicBool::new(false),
            closed: CancellationToken::new(),
        }
    }

    pub fn game_server(&self) -> Option<GameServerId> {
        *self.game_server.lock().unwrap()
    }

    pub fn set_game_server(&self, gs_id: GameServerId) {
        *self.game_server.lock().unwrap() = Some(gs_id);
    }

    pub fn send_packet<P: OutgoingPacket>(&self, packet: &P) {
        if self.block.load(Ordering::Relaxed) {
            return;
        }
        match packet.encode_payload() {
            Ok(payload) => {
                let _ = self.tx.send(payload);
            }
            Err(e) => tracing::error!("Failed to encode internal packet 0x{:x}: {}", P::OPCODE, e),
        }
    }

    pub fn close(&self) {
        self.closed.cancel();
    }
}

/// `InternalConnectionTable`.
#[derive(Debug, Default)]
pub struct InternalConnectionTable {
    connections: DashMap<u32, Arc<InternalConnection>>,
}

impl InternalConnectionTable {
    pub fn add(&self, con: Arc<InternalConnection>) {
        self.connections.entry(con.id).or_insert(con);
    }

    pub fn get(&self, id: u32) -> Option<Arc<InternalConnection>> {
        self.connections.get(&id).map(|c| c.clone())
    }

    pub fn remove(&self, id: u32) -> Option<Arc<InternalConnection>> {
        self.connections.remove(&id).map(|(_, c)| c)
    }
}

/// Binds the internal listener. `*` binds all IPv4 interfaces (the C# server uses a dual-mode
/// IPv6 socket).
pub async fn bind(host: &str, port: u16) -> std::io::Result<TcpListener> {
    let addr: SocketAddr = if host == "*" {
        SocketAddr::from(([0, 0, 0, 0], port))
    } else {
        let ip: IpAddr = host
            .parse()
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidInput, e))?;
        SocketAddr::new(ip, port)
    };
    TcpListener::bind(addr).await
}

/// Accepts game server connections until `shutdown` is cancelled.
pub async fn run(server: Arc<LoginServer>, listener: TcpListener, shutdown: CancellationToken) {
    tracing::info!("InternalNetwork started");
    loop {
        tokio::select! {
            _ = shutdown.cancelled() => break,
            accepted = listener.accept() => match accepted {
                Ok((stream, addr)) => {
                    let server = server.clone();
                    let shutdown = shutdown.clone();
                    tokio::spawn(async move { handle_connection(server, stream, addr, shutdown).await });
                }
                Err(e) => tracing::error!("InternalNetwork accept error: {}", e),
            }
        }
    }
    tracing::info!("InternalNetwork stopped");
}

async fn handle_connection(
    server: Arc<LoginServer>,
    stream: TcpStream,
    addr: SocketAddr,
    shutdown: CancellationToken,
) {
    let _ = stream.set_nodelay(true);
    let (read_half, write_half) = stream.into_split();
    let mut reader = FramedRead::new(read_half, FrameCodec);
    let mut writer = FramedWrite::new(write_half, FrameCodec);
    let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();

    let connection = Arc::new(InternalConnection::new(addr.ip(), tx));
    tracing::info!(
        "GameServer from {} connected, session id: {}",
        addr.ip(),
        connection.id
    );
    server.internal_connections.add(connection.clone());

    let closed = connection.closed.clone();
    let writer_task = tokio::spawn({
        let closed = closed.clone();
        async move {
            loop {
                tokio::select! {
                    _ = closed.cancelled() => break,
                    msg = rx.recv() => match msg {
                        Some(payload) => {
                            if let Err(e) = writer.send(payload).await {
                                tracing::debug!("internal write error: {}", e);
                                closed.cancel();
                                break;
                            }
                        }
                        None => break,
                    }
                }
            }
        }
    });

    loop {
        let frame = tokio::select! {
            _ = closed.cancelled() => break,
            _ = shutdown.cancelled() => break,
            frame = reader.next() => frame,
        };
        let payload = match frame {
            Some(Ok(p)) => p,
            Some(Err(e)) => {
                tracing::debug!("internal read error from {}: {}", addr.ip(), e);
                break;
            }
            None => break,
        };
        let (opcode, mut stream) = match split_opcode(payload) {
            Ok(v) => v,
            Err(e) => {
                tracing::error!("Error on packet dispatch from {}: {}", addr.ip(), e);
                continue;
            }
        };
        match G2lPacket::decode(opcode, &mut stream) {
            Ok(Some(packet)) => {
                if let Err(e) = handlers::handle_g2l(&server, &connection, packet).await {
                    tracing::error!("Error on packet dispatch {}: {:#}", opcode, e);
                }
            }
            Ok(None) => {
                tracing::error!(
                    "Unknown packet 0x{:02x} from {}:\n{}",
                    opcode,
                    addr.ip(),
                    dump_unread(&stream)
                );
            }
            Err(e) => tracing::error!("Error on packet dispatch {}: {}", opcode, e),
        }
    }

    closed.cancel();
    let _ = writer_task.await;
    tracing::info!("GameServer from {} disconnected", addr.ip());
    if let Some(gs_id) = connection.game_server() {
        server.games.remove(gs_id);
    }
    server.internal_connections.remove(connection.id);
}
