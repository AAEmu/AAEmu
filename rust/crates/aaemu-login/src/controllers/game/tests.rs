//! Port of `AAEmu.UnitTests/Login/Core/Controllers/GameControllerTests.cs`.
//!
//! The mocked `IRequestController` / `ILoginConnectionTable` are the real (in-memory)
//! implementations; game server sessions are `InternalConnection`s whose outgoing packets are
//! captured through their channel.

use std::net::{IpAddr, Ipv4Addr};
use std::sync::Arc;
use std::time::Duration;

use tokio::sync::mpsc;

use super::*;
use crate::auth::{AuthFlow, PasswordAuthFlow};
use crate::models::LoginResult;
use crate::packets::l2c::opcodes as l2c_op;
use crate::packets::l2g::opcodes as l2g_op;
use crate::packets::split_opcode;
use crate::services::password::Password;
use crate::test_support::{
    opcode_of, test_config, test_connection, test_server, LoginBehavior, MockBackend,
};

const GS_ID1: GameServerId = GameServerId(1);
const GS_ID2: GameServerId = GameServerId(2);
const GS_ID3: GameServerId = GameServerId(3);
const ACCOUNT_ID: AccountId = AccountId(100);
const CONNECTION_ID: ConnectionId = ConnectionId(50);

struct Sut {
    games: GameController,
    requests: Arc<RequestController>,
    connections: Arc<LoginConnectionTable>,
    config: AppConfig,
}

/// `CreateSut(MakeConfig(servers...))`: `(id, name, host, port, hidden)` tuples.
fn create_sut(servers: &[(u8, &str, &str, u16, bool)]) -> Sut {
    let servers: Vec<_> = servers
        .iter()
        .map(|(id, name, host, port, hidden)| {
            serde_json::json!({ "Id": id, "Name": name, "Host": host, "Port": port, "Hidden": hidden })
        })
        .collect();
    let config = test_config(serde_json::json!({ "GameServers": servers }));
    let requests = Arc::new(RequestController::new());
    requests.initialize();
    let connections = Arc::new(LoginConnectionTable::default());
    Sut {
        games: GameController::new(requests.clone(), connections.clone()),
        requests,
        connections,
        config,
    }
}

/// `CreateConnection` / `CreateTrackedConnection`: an `InternalConnection` whose sent packets
/// can be inspected.
fn create_connection() -> (Arc<InternalConnection>, mpsc::UnboundedReceiver<Vec<u8>>) {
    let (tx, rx) = mpsc::unbounded_channel();
    (
        Arc::new(InternalConnection::new(IpAddr::V4(Ipv4Addr::LOCALHOST), tx)),
        rx,
    )
}

fn drain(rx: &mut mpsc::UnboundedReceiver<Vec<u8>>) -> Vec<Vec<u8>> {
    let mut out = Vec::new();
    while let Ok(p) = rx.try_recv() {
        out.push(p);
    }
    out
}

#[tokio::test]
async fn load_visible_server_is_available_via_get_game_server() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);

    sut.games.load(&sut.config).await;

    let server = sut.games.get_game_server(GS_ID1).expect("loaded");
    assert_eq!(server.name, "World1");
    assert_eq!(server.host, "127.0.0.1");
    assert_eq!(server.port, 1234);
}

#[tokio::test]
async fn load_hidden_server_is_not_available_via_get_game_server() {
    let sut = create_sut(&[(1, "HiddenWorld", "127.0.0.1", 1234, true)]);

    sut.games.load(&sut.config).await;

    assert!(sut.games.get_game_server(GS_ID1).is_none());
}

#[tokio::test]
async fn load_mixed_servers_only_loads_visible_ones() {
    let sut = create_sut(&[
        (1, "Visible", "127.0.0.1", 1234, false),
        (2, "Hidden", "127.0.0.2", 5678, true),
    ]);

    sut.games.load(&sut.config).await;

    assert!(sut.games.get_game_server(GS_ID1).is_some());
    assert!(sut.games.get_game_server(GS_ID2).is_none());
}

#[test]
fn get_game_server_unknown_id_returns_null() {
    let sut = create_sut(&[]);

    assert!(sut.games.get_game_server(GS_ID1).is_none());
}

#[tokio::test]
async fn try_get_parent_id_no_mirror_registered_returns_false() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;

    assert_eq!(sut.games.try_get_parent_id(GS_ID1), None);
}

#[tokio::test]
async fn try_get_parent_id_after_add_with_mirror_returns_true_with_parent_id() {
    let sut = create_sut(&[
        (1, "World1", "127.0.0.1", 1234, false),
        (2, "Mirror", "127.0.0.2", 1234, false),
    ]);
    sut.games.load(&sut.config).await;
    let (conn, _rx) = create_connection();
    sut.games.add(GS_ID1, vec![GS_ID2], &conn);

    assert_eq!(sut.games.try_get_parent_id(GS_ID2), Some(GS_ID1));
}

#[tokio::test]
async fn add_known_server_id_makes_server_active() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;
    let (conn, _rx) = create_connection();

    sut.games.add(GS_ID1, vec![], &conn);

    assert!(sut.games.get_game_server(GS_ID1).unwrap().active());
}

#[tokio::test]
async fn add_with_mirrors_makes_mirror_servers_active() {
    let sut = create_sut(&[
        (1, "World1", "127.0.0.1", 1234, false),
        (2, "Mirror1", "127.0.0.2", 1234, false),
        (3, "Mirror2", "127.0.0.3", 1234, false),
    ]);
    sut.games.load(&sut.config).await;
    let (conn, _rx) = create_connection();

    sut.games.add(GS_ID1, vec![GS_ID2, GS_ID3], &conn);

    assert!(sut.games.get_game_server(GS_ID2).unwrap().active());
    assert!(sut.games.get_game_server(GS_ID3).unwrap().active());
}

#[tokio::test]
async fn remove_after_add_clears_server_connection() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;
    let (conn, _rx) = create_connection();
    sut.games.add(GS_ID1, vec![], &conn);

    sut.games.remove(GS_ID1);

    assert!(!sut.games.get_game_server(GS_ID1).unwrap().active());
}

#[tokio::test]
async fn remove_with_mirrors_clears_mirror_connections() {
    let sut = create_sut(&[
        (1, "World1", "127.0.0.1", 1234, false),
        (2, "Mirror", "127.0.0.2", 1234, false),
    ]);
    sut.games.load(&sut.config).await;
    let (conn, _rx) = create_connection();
    sut.games.add(GS_ID1, vec![GS_ID2], &conn);

    sut.games.remove(GS_ID1);

    assert!(!sut.games.get_game_server(GS_ID2).unwrap().active());
    assert_eq!(sut.games.try_get_parent_id(GS_ID2), None);
}

#[test]
fn remove_unknown_server_id_does_not_throw() {
    let sut = create_sut(&[]);

    sut.games.remove(GS_ID1);
}

#[tokio::test]
async fn set_load_sets_load_on_server() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;

    sut.games.set_load(GS_ID1, GsLoad::HIGH).unwrap();

    assert_eq!(
        sut.games.get_game_server(GS_ID1).unwrap().load,
        GsLoad::HIGH
    );
}

#[test]
fn request_enter_world_unknown_gs_id_does_not_throw() {
    let sut = create_sut(&[]);

    sut.games
        .request_enter_world(ACCOUNT_ID, CONNECTION_ID, GS_ID1);
}

#[tokio::test]
async fn request_enter_world_known_but_inactive_server_does_not_throw() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await; // loaded but never add()ed, so inactive

    sut.games
        .request_enter_world(ACCOUNT_ID, CONNECTION_ID, GS_ID1);
}

#[tokio::test]
async fn request_enter_world_active_server_sends_packet_to_session() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;
    let (conn, mut rx) = create_connection();
    sut.games.add(GS_ID1, vec![], &conn);
    // Note: add() sends 1 packet; request_enter_world() sends 1 more

    sut.games
        .request_enter_world(ACCOUNT_ID, CONNECTION_ID, GS_ID1);

    let sent = drain(&mut rx);
    assert_eq!(sent.len(), 2);
    assert_eq!(opcode_of(&sent[0]), l2g_op::LG_REGISTER_GAME_SERVER);
    assert_eq!(opcode_of(&sent[1]), l2g_op::LG_PLAYER_ENTER);
}

#[test]
fn route_enter_world_response_connection_not_found_does_not_throw() {
    let sut = create_sut(&[]);

    sut.games
        .route_enter_world_response(CONNECTION_ID, GS_ID1, 0);
}

/// The routed result reaches the connection's session (`CompleteEnterWorldRequest(gsId, 42)`),
/// observed through the denial it sends to the client.
#[tokio::test]
async fn route_enter_world_response_connection_found_delegates_to_session() {
    let backend = Arc::new(MockBackend::default());
    backend.set_login(LoginBehavior::Result(LoginResult::ok(ACCOUNT_ID)));
    let server = test_server(
        test_config(serde_json::json!({
            "GameServers": [ { "Id": 1, "Name": "World1", "Host": "127.0.0.1", "Port": 1234 } ]
        })),
        backend,
    );
    server.games.load(&server.config).await;
    let (gs_conn, _gs_rx) = create_connection();
    server.games.add(GS_ID1, vec![], &gs_conn);

    let (conn, mut sent) = test_connection(IpAddr::V4(Ipv4Addr::LOCALHOST));
    server.connections.add(conn.clone()).unwrap();
    let flow = AuthFlow::Password(PasswordAuthFlow {
        username: Some("user".into()),
        password: Some(Password::from_plaintext("pw")),
        client_ip: conn.ip(),
    });
    conn.authenticate(&server, flow).await.unwrap();
    conn.initiate_enter_world(&server, GS_ID1).await.unwrap();
    sent.drain();

    server
        .games
        .route_enter_world_response(conn.id(), GS_ID1, 42);
    let task = conn.take_enter_world_task().unwrap();
    tokio::time::timeout(Duration::from_secs(5), task)
        .await
        .unwrap()
        .unwrap();

    let packets = sent.drain();
    assert_eq!(packets.len(), 1);
    assert_eq!(opcode_of(&packets[0]), l2c_op::AC_ENTER_WORLD_DENIED);
    assert_eq!(packets[0][2], 42);
}

#[tokio::test]
async fn get_world_list_async_no_servers_loaded_returns_empty_result() {
    let sut = create_sut(&[]);
    let (conn, _sent) = test_connection(IpAddr::V4(Ipv4Addr::LOCALHOST));

    let result = sut.games.get_world_list(&conn).await.unwrap();

    assert!(result.game_servers.is_empty());
    assert!(result.characters.is_empty());
}

#[tokio::test(start_paused = true)]
async fn get_world_list_async_loaded_but_inactive_servers_returns_servers_without_requesting() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;
    let (conn, _sent) = test_connection(IpAddr::V4(Ipv4Addr::LOCALHOST));
    let start = tokio::time::Instant::now();

    let result = sut.games.get_world_list(&conn).await.unwrap();

    assert_eq!(result.game_servers.len(), 1);
    assert!(result.characters.is_empty());
    // No request was created: nothing was waited for and no request id was consumed.
    assert_eq!(tokio::time::Instant::now(), start);
    assert_eq!(sut.requests.get_next_id().unwrap(), 1);
}

#[tokio::test]
async fn get_world_list_async_active_server_requests_character_info_per_server() {
    let sut = create_sut(&[(1, "World1", "127.0.0.1", 1234, false)]);
    sut.games.load(&sut.config).await;
    let (gs_conn, mut gs_rx) = create_connection();
    sut.games.add(GS_ID1, vec![], &gs_conn);
    drain(&mut gs_rx);
    let (conn, _sent) = test_connection(IpAddr::V4(Ipv4Addr::LOCALHOST));
    sut.connections.add(conn.clone()).unwrap();

    // The game server answers the character request (GLRequestInfoPacket releases the id).
    let requests = sut.requests.clone();
    let responder = async move {
        let payload = gs_rx.recv().await.expect("LGRequestInfoPacket");
        let (op, mut s) = split_opcode(payload).unwrap();
        assert_eq!(op, l2g_op::LG_REQUEST_INFO);
        let p = LgRequestInfo::read(&mut s).unwrap();
        requests.release_id(p.request_id);
        assert!(gs_rx.try_recv().is_err(), "exactly one request per server");
        p
    };
    let (result, request) = tokio::time::timeout(
        Duration::from_secs(5),
        futures::future::join(sut.games.get_world_list(&conn), responder),
    )
    .await
    .expect("world list completes once the request is answered");

    let result = result.unwrap();
    assert_eq!(result.game_servers.len(), 1);
    assert_eq!(request.request_id, 1);
    assert_eq!(request.connection_id, conn.id());
}
