//! Ports of `AAEmu.UnitTests/Login/Core/Network/Connections/LoginSessionTests.cs`,
//! `Login/Core/Network/Login/LoginConnectionFactoryTests.cs`,
//! `Login/Core/Network/Login/LoginConnectionHandlerTests.cs` and
//! `Login/Core/PacketHandlers/C2L/*PacketHandlerTests.cs`.
//!
//! The C# `LoginSession` tests mock `IAuthenticationFlow` with a canned `StartAsync` result. In
//! Rust the flows are a closed enum, so the canned results are produced by real flows driven by a
//! [`MockBackend`]: `Success`/`Denied` via `PasswordAuthFlow` (mocked `Login`), `Pending` via
//! `KoreaAuthFlow` (mocked Korea material; that flow sends its own `ACChallenge2Packet`, which is
//! the only packet allowed in the "Pending sends nothing" assertions).

use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::sync::Arc;
use std::time::Duration;

use futures::{SinkExt, StreamExt};
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio_util::codec::Framed;
use tokio_util::sync::CancellationToken;

use super::*;
use crate::auth::{KoreaAuthFlow, PasswordAuthFlow};
use crate::models::{KoreaAuthInfo, LoginResult};
use crate::network::internal::InternalConnection;
use crate::packets::c2l::{
    C2lPacket, CaOtpNumber, CaPcCertNumber, CaRequestAuth, CaRequestAuthTrion,
};
use crate::packets::l2c::opcodes as l2c_op;
use crate::services::password::{Password, PasswordKind};
use crate::services::sha256_crypt;
use crate::test_support::{
    test_config, test_connection, test_server, LoginBehavior, MockBackend, PacketLog,
};

const TEST_ACCOUNT_ID: AccountId = AccountId(100);
const TEST_GS_ID: GameServerId = GameServerId(1);
const TEST_IP: IpAddr = IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1));

struct Fixture {
    backend: Arc<MockBackend>,
    server: Arc<LoginServer>,
    conn: Arc<LoginConnection>,
    sent: PacketLog,
}

/// `new AppConfiguration { SecretKey = "test", EnterWorldTimeout = 200ms, GameServers = [] }`,
/// plus one (not yet registered) game server so `CreateActiveGameServer` can activate it.
fn fixture() -> Fixture {
    let backend = Arc::new(MockBackend::default());
    let server = test_server(
        test_config(serde_json::json!({
            "EnterWorldTimeout": "00:00:00.200",
            "KoreaAuth": { "Enabled": true },
            "GameServers": [ { "Id": 1, "Name": "TestServer", "Host": "127.0.0.1", "Port": 1234 } ]
        })),
        backend.clone(),
    );
    let (conn, sent) = test_connection(TEST_IP);
    Fixture {
        backend,
        server,
        conn,
        sent,
    }
}

impl Fixture {
    /// A flow whose `StartAsync` returns `Success(TEST_ACCOUNT_ID, username)`.
    fn success_flow(&self, username: &str) -> AuthFlow {
        self.backend
            .set_login(LoginBehavior::Result(LoginResult::ok(TEST_ACCOUNT_ID)));
        AuthFlow::Password(PasswordAuthFlow {
            username: Some(username.to_string()),
            password: Some(Password::from_plaintext("pw")),
            client_ip: TEST_IP,
        })
    }

    /// A flow whose `StartAsync` returns `Denied(reason)`.
    fn denied_flow(&self, reason: LoginDeniedReason) -> AuthFlow {
        self.backend
            .set_login(LoginBehavior::Result(LoginResult::denied(reason)));
        AuthFlow::Password(PasswordAuthFlow {
            username: Some("testuser".to_string()),
            password: Some(Password::from_plaintext("pw")),
            client_ip: TEST_IP,
        })
    }

    /// A flow whose `StartAsync` returns `Pending` (a Korea flow awaiting its challenge).
    fn pending_flow(&self) -> AuthFlow {
        self.backend
            .set_korea_info(Some(korea_info(TEST_ACCOUNT_ID, "password")));
        AuthFlow::Korea(Box::new(KoreaAuthFlow::new("testuser", TEST_IP)))
    }

    async fn authenticate_successfully(&mut self) {
        let flow = self.success_flow("testuser");
        self.conn.authenticate(&self.server, flow).await.unwrap();
        self.sent.drain();
    }

    /// `CreateActiveGameServer`: registers the configured game server with a game server
    /// connection, which makes it active.
    async fn create_active_game_server(&self) -> mpsc::UnboundedReceiver<Vec<u8>> {
        self.server.games.load(&self.server.config).await;
        let (tx, rx) = mpsc::unbounded_channel();
        let gs_conn = Arc::new(InternalConnection::new(IpAddr::V4(Ipv4Addr::LOCALHOST), tx));
        self.server.games.add(TEST_GS_ID, vec![], &gs_conn);
        rx
    }

    async fn await_enter_world_task(&self) {
        let task = self
            .conn
            .take_enter_world_task()
            .expect("EnterWorldBackgroundTask");
        tokio::time::timeout(Duration::from_secs(5), task)
            .await
            .expect("background task completes")
            .unwrap();
    }
}

fn korea_info(account_id: AccountId, password: &str) -> KoreaAuthInfo {
    let salt = "unitsalttestABCD";
    KoreaAuthInfo {
        account_id,
        challenge_key_hash: sha256_crypt::compute_raw(password, salt, 1000),
        challenge_salt: salt.to_string(),
        challenge_rounds: 1000,
    }
}

// ---- LoginSessionTests --------------------------------------------------------------------

#[tokio::test]
async fn authenticate_async_success_sends_join_then_auth_response() {
    let mut f = fixture();
    let flow = f.success_flow("testuser");

    f.conn.authenticate(&f.server, flow).await.unwrap();

    assert_eq!(
        f.sent.opcodes(),
        vec![l2c_op::AC_JOIN_RESPONSE, l2c_op::AC_AUTH_RESPONSE]
    );
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn authenticate_async_denied_sends_login_denied_packet() {
    let mut f = fixture();
    let flow = f.denied_flow(LoginDeniedReason::BAD_ACCOUNT);

    f.conn.authenticate(&f.server, flow).await.unwrap();

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_LOGIN_DENIED]);
    assert_eq!(f.conn.state(), LoginState::Connected);
}

#[tokio::test]
async fn authenticate_async_from_wrong_state_sends_denied_bad_response() {
    let mut f = fixture();
    f.authenticate_successfully().await;

    let flow = f.success_flow("other");
    f.conn.authenticate(&f.server, flow).await.unwrap();

    let sent = f.sent.drain();
    assert_eq!(sent.len(), 1);
    assert_eq!(
        crate::test_support::opcode_of(&sent[0]),
        l2c_op::AC_LOGIN_DENIED
    );
    // ACLoginDeniedPacket body starts with the reason byte.
    assert_eq!(sent[0][2], LoginDeniedReason::BAD_RESPONSE.0);
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn authenticate_async_pending_no_packet_sent() {
    let mut f = fixture();
    let flow = f.pending_flow();

    f.conn.authenticate(&f.server, flow).await.unwrap();

    // The session sends nothing; the only packet is the flow's own challenge prompt.
    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_CHALLENGE2]);
    assert_eq!(f.conn.state(), LoginState::Authenticating);
}

#[tokio::test]
async fn authenticate_async_success_sets_connection_properties() {
    let f = fixture();
    let flow = f.success_flow("testuser");
    let before = chrono::Utc::now();

    f.conn.authenticate(&f.server, flow).await.unwrap();

    let after = chrono::Utc::now();
    assert_eq!(f.conn.account_id(), TEST_ACCOUNT_ID);
    assert_eq!(f.conn.account_name().as_deref(), Some("testuser"));
    assert_eq!(f.conn.last_ip(), Some(TEST_IP));
    let last_login = f.conn.last_login().expect("LastLogin set");
    assert!(before <= last_login && last_login <= after);
}

/// Starts a pending Korea flow and returns the challenge (`hc`) it sent.
async fn start_pending(f: &mut Fixture) -> Vec<u32> {
    let flow = f.pending_flow();
    f.conn.authenticate(&f.server, flow).await.unwrap();
    let sent = f.sent.drain();
    assert_eq!(sent.len(), 1);
    let (op, mut s) = crate::packets::split_opcode(sent[0].clone()).unwrap();
    assert_eq!(op, l2c_op::AC_CHALLENGE2);
    // ACChallenge2Packet body: [i32 round][u16-sized salt][8 x u32 hc]
    s.read_i32().unwrap();
    s.read_bytes_sized().unwrap();
    (0..8).map(|_| s.read_u32().unwrap()).collect()
}

#[tokio::test]
async fn continue_auth_async_matching_flow_success_sends_auth_packets() {
    let mut f = fixture();
    let hc = start_pending(&mut f).await;
    let key = korea_info(TEST_ACCOUNT_ID, "password").challenge_key_hash;
    let ch: [u32; 8] = crate::services::korea::compute_response(&key, &hc)
        .try_into()
        .unwrap();

    f.conn
        .continue_auth(&f.server, KoreaStep::ChallengeV2 { ch })
        .await
        .unwrap();

    assert_eq!(
        f.sent.opcodes(),
        vec![l2c_op::AC_JOIN_RESPONSE, l2c_op::AC_AUTH_RESPONSE]
    );
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn continue_auth_async_matching_flow_denied_sends_denied_packet() {
    let mut f = fixture();
    start_pending(&mut f).await;

    f.conn
        .continue_auth(&f.server, KoreaStep::ChallengeV2 { ch: [0; 8] })
        .await
        .unwrap();

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_LOGIN_DENIED]);
    assert_eq!(f.conn.state(), LoginState::Connected);
}

/// The C# test continues a pending plain `IAuthenticationFlow` as `IOtherFlow`. In Rust only the
/// Korea flow can be continued; a session that is `Authenticating` without a Korea flow (here: the
/// flow's `StartAsync` threw, which leaves the session in `Authenticating`) is the mismatch case.
#[tokio::test]
async fn continue_auth_async_wrong_flow_type_sends_denied_and_shutdown() {
    let mut f = fixture();
    f.backend.set_login(LoginBehavior::Error);
    let flow = AuthFlow::Password(PasswordAuthFlow {
        username: Some("testuser".into()),
        password: Some(Password::from_plaintext("pw")),
        client_ip: TEST_IP,
    });
    assert!(f.conn.authenticate(&f.server, flow).await.is_err());
    assert_eq!(f.conn.state(), LoginState::Authenticating);
    f.sent.drain();

    f.conn
        .continue_auth(&f.server, KoreaStep::Otp("123456".into()))
        .await
        .unwrap();

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_LOGIN_DENIED]);
    assert!(f.conn.connection_closed().is_cancelled(), "Shutdown called");
    assert_eq!(f.conn.state(), LoginState::Disconnected);
}

#[tokio::test]
async fn continue_auth_async_from_wrong_state_sends_denied() {
    // Session is in Connected state (no pending auth)
    let mut f = fixture();

    f.conn
        .continue_auth(&f.server, KoreaStep::ChallengeV2 { ch: [0; 8] })
        .await
        .unwrap();

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_LOGIN_DENIED]);
    assert_eq!(f.conn.state(), LoginState::Connected);
}

#[tokio::test]
async fn initiate_enter_world_success_sends_world_cookie_packet() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    f.conn
        .complete_enter_world_request(&f.server.games, TEST_GS_ID, 0);
    f.await_enter_world_task().await;

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_WORLD_COOKIE]);
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn initiate_enter_world_failure_sends_enter_world_denied_packet() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    f.conn
        .complete_enter_world_request(&f.server.games, TEST_GS_ID, 1);
    f.await_enter_world_task().await;

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_ENTER_WORLD_DENIED]);
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test(start_paused = true)]
async fn initiate_enter_world_timeout_sends_enter_world_denied() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    // Advance past the 200ms timeout
    tokio::time::advance(Duration::from_millis(300)).await;
    f.await_enter_world_task().await;

    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_ENTER_WORLD_DENIED]);
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn initiate_enter_world_from_wrong_state_no_action() {
    // Session is in Connected state
    let mut f = fixture();

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Connected);
}

#[tokio::test]
async fn initiate_enter_world_server_not_active_no_action() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    // Server with no connection (not active)
    f.server.games.load(&f.server.config).await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn complete_enter_world_gs_id_mismatch_ignored() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    // Complete with a different gsId
    f.conn
        .complete_enter_world_request(&f.server.games, GameServerId(99), 0);
    tokio::task::yield_now().await;
    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::EnteringWorld);

    // Clean up: cancel to avoid timeout firing later
    f.conn.cancel_enter_world();
    f.await_enter_world_task().await;
}

#[tokio::test]
async fn cancel_enter_world_while_entering_cancels_operation() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    f.conn.cancel_enter_world();
    f.await_enter_world_task().await;

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn cancel_enter_world_not_entering_ignored() {
    let mut f = fixture();
    f.authenticate_successfully().await;

    f.conn.cancel_enter_world();

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}

#[tokio::test]
async fn disconnect_async_cancels_pending_enter_world() {
    let mut f = fixture();
    f.authenticate_successfully().await;
    let _gs = f.create_active_game_server().await;

    f.conn
        .initiate_enter_world(&f.server, TEST_GS_ID)
        .await
        .unwrap();
    f.conn.disconnect().await;

    // Should not send any packets since disconnect cancels before result
    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Disconnected);
}

#[tokio::test]
async fn disconnect_async_from_authenticated_sets_disconnected() {
    let mut f = fixture();
    f.authenticate_successfully().await;

    f.conn.disconnect().await;

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Disconnected);
}

#[tokio::test]
async fn disconnect_async_already_disconnected_idempotent() {
    let mut f = fixture();

    f.conn.disconnect().await;
    f.conn.disconnect().await;

    assert!(f.sent.drain().is_empty());
    assert_eq!(f.conn.state(), LoginState::Disconnected);
}

// ---- LoginConnectionFactoryTests ----------------------------------------------------------

fn loopback(port: u16) -> SocketAddr {
    SocketAddr::new(IpAddr::V4(Ipv4Addr::LOCALHOST), port)
}

fn create(ids: &Arc<SimpleIdManager>, local: SocketAddr, remote: SocketAddr) -> LoginConnection {
    let (tx, _rx) = mpsc::unbounded_channel();
    LoginConnection::create(ids, Some(local.ip()), remote.ip(), tx).unwrap()
}

#[test]
fn factory_create_with_valid_context_returns_login_connection() {
    let ids = Arc::new(SimpleIdManager::new());
    let connection = create(&ids, loopback(1234), loopback(12345));
    assert_eq!(connection.id(), ConnectionId(1));
}

#[test]
fn factory_create_rents_connection_id_lease() {
    let ids = Arc::new(SimpleIdManager::new());
    let connection = create(&ids, loopback(1234), loopback(12345));
    // Exactly one id was rented: the next rent mints id 2.
    assert_eq!(ids.rent().unwrap(), 2);
    drop(connection);
}

#[test]
fn factory_create_multiple_calls_rents_new_lease_each_time() {
    let ids = Arc::new(SimpleIdManager::new());
    let connection1 = create(&ids, loopback(1234), loopback(12345));
    let connection2 = create(&ids, loopback(1234), loopback(12346));
    assert_eq!(connection1.id(), ConnectionId(1));
    assert_eq!(connection2.id(), ConnectionId(2));
}

/// `Create_WithPacketDescriptors_IncludesThemInConnection` /
/// `Create_WithDuplicatePacketDescriptorTypeIds_ThrowsArgumentException`: the C# factory builds an
/// opcode -> descriptor dictionary (duplicates throw). Rust dispatches with a `match` over the
/// opcode table, so the equivalent invariant is that the C2L opcodes are distinct and each one
/// decodes to its own packet.
#[test]
fn factory_c2l_packet_table_has_unique_type_ids() {
    use crate::packets::c2l::opcodes::*;
    let all = [
        CA_REQUEST_AUTH,
        CA_REQUEST_AUTH_TENCENT,
        CA_REQUEST_AUTH_GAME_ON,
        CA_REQUEST_AUTH_TRION,
        CA_REQUEST_AUTH_MAIL_RU,
        CA_CHALLENGE_RESPONSE,
        CA_CHALLENGE_RESPONSE2,
        CA_OTP_NUMBER,
        CA_PC_CERT_NUMBER,
        CA_LIST_WORLD,
        CA_ENTER_WORLD,
        CA_CANCEL_ENTER_WORLD,
        CA_REQUEST_RECONNECT,
    ];
    let mut names = std::collections::HashSet::new();
    let mut ids = std::collections::HashSet::new();
    for op in all {
        assert!(ids.insert(op), "duplicate opcode {op:#x}");
        let mut s = aaemu_commons::PacketStream::from_bytes(vec![0u8; 64]);
        match C2lPacket::decode(op, &mut s) {
            Ok(Some(packet)) => {
                assert!(
                    names.insert(packet.name()),
                    "{op:#x} decodes to a duplicate"
                )
            }
            Ok(None) => panic!("opcode {op:#x} has no packet"),
            // Recognised, but a zeroed body is not a valid packet (the Trion XML ticket).
            Err(_) => {}
        }
    }
}

#[test]
fn factory_create_connection_is_local_when_same_endpoint() {
    let ids = Arc::new(SimpleIdManager::new());
    let endpoint = loopback(12345);
    let connection = create(&ids, endpoint, endpoint);
    assert!(connection.is_locally_connected());
}

#[test]
fn factory_create_connection_is_not_local_when_different_endpoint() {
    let ids = Arc::new(SimpleIdManager::new());
    let remote = SocketAddr::new("192.168.1.1".parse().unwrap(), 54321);
    let connection = create(&ids, loopback(12345), remote);
    assert!(!connection.is_locally_connected());
}

// ---- LoginConnectionHandlerTests ----------------------------------------------------------

async fn wait_until(mut cond: impl FnMut() -> bool) {
    for _ in 0..500 {
        if cond() {
            return;
        }
        tokio::time::sleep(Duration::from_millis(10)).await;
    }
    panic!("condition not reached in time");
}

/// Starts the client listener for `server`; returns its address and the shutdown token.
async fn start_listener(server: &Arc<LoginServer>) -> (SocketAddr, CancellationToken) {
    let listener = bind("127.0.0.1", 0).await.unwrap();
    let addr = listener.local_addr().unwrap();
    let shutdown = CancellationToken::new();
    tokio::spawn(run(server.clone(), listener, shutdown.clone()));
    (addr, shutdown)
}

#[tokio::test]
async fn on_connected_async_normal_connection_adds_and_removes_connection() {
    let server = test_server(test_config(serde_json::json!({})), Default::default());
    let (addr, shutdown) = start_listener(&server).await;

    let client = TcpStream::connect(addr).await.unwrap();
    wait_until(|| server.connections.all().len() == 1).await;
    let id = server.connections.all()[0].id();
    assert_eq!(id, ConnectionId(1));

    drop(client);
    wait_until(|| server.connections.get(id).is_none()).await;
    shutdown.cancel();
}

#[tokio::test]
async fn on_connected_async_connection_throws_still_removes_connection() {
    let server = test_server(test_config(serde_json::json!({})), Default::default());
    let (addr, shutdown) = start_listener(&server).await;

    let mut client = Framed::new(TcpStream::connect(addr).await.unwrap(), FrameCodec);
    wait_until(|| server.connections.all().len() == 1).await;
    let id = server.connections.all()[0].id();

    // A frame too short to hold an opcode makes the dispatch loop fail (C#: an exception).
    client.send(vec![0u8]).await.unwrap();

    wait_until(|| server.connections.get(id).is_none()).await;
    shutdown.cancel();
}

#[tokio::test]
async fn on_connected_async_disposes_connection_context() {
    let server = test_server(test_config(serde_json::json!({})), Default::default());
    let (addr, shutdown) = start_listener(&server).await;

    let mut client = Framed::new(TcpStream::connect(addr).await.unwrap(), FrameCodec);
    wait_until(|| server.connections.all().len() == 1).await;
    client.send(vec![0u8]).await.unwrap();

    // The server closes the transport: the client reads EOF.
    let next = tokio::time::timeout(Duration::from_secs(5), client.next())
        .await
        .expect("socket closed in time");
    assert!(next.is_none() || next.unwrap().is_err());
    shutdown.cancel();
}

#[tokio::test]
async fn on_connected_async_disposes_connection() {
    let server = test_server(test_config(serde_json::json!({})), Default::default());
    let (addr, shutdown) = start_listener(&server).await;

    let client = TcpStream::connect(addr).await.unwrap();
    wait_until(|| server.connections.all().len() == 1).await;
    let conn = server.connections.all()[0].clone();
    let id = conn.id();

    drop(client);
    wait_until(|| conn.state() == LoginState::Disconnected).await;
    wait_until(|| server.connections.get(id).is_none()).await;
    // Disposing the connection returns its id lease once the last reference is gone.
    drop(conn);
    assert_eq!(server.connection_ids.rent().unwrap(), id.0);
    shutdown.cancel();
}

// ---- C2L packet handler tests -------------------------------------------------------------

/// `CAOtpNumberPacketHandler` / `CAPcCertNumberPacketHandler` call `ContinueAuthAsync` on the
/// session; observed here through the pending Korea flow receiving the step (it is still awaiting
/// its challenge, so the step is denied with `BadResponse` and the session drops back to
/// `Connected`).
async fn assert_continue_auth_called(packet: C2lPacket) {
    let mut f = fixture();
    start_pending(&mut f).await;

    crate::handlers::handle_c2l(&f.server, &f.conn, packet)
        .await
        .unwrap();

    let sent = f.sent.drain();
    assert_eq!(sent.len(), 1);
    assert_eq!(
        crate::test_support::opcode_of(&sent[0]),
        l2c_op::AC_LOGIN_DENIED
    );
    assert_eq!(sent[0][2], LoginDeniedReason::BAD_RESPONSE.0);
    assert_eq!(f.conn.state(), LoginState::Connected);
}

#[tokio::test]
async fn ca_otp_number_handler_execute_calls_continue_auth_async() {
    assert_continue_auth_called(C2lPacket::OtpNumber(CaOtpNumber {
        otp_number: "123456".into(),
    }))
    .await;
}

/// C# passes a `null` OtpNumber; a decoded Rust packet always holds a string, the closest
/// equivalent being an empty one.
#[tokio::test]
async fn ca_otp_number_handler_execute_null_otp_number_calls_continue_auth_async() {
    assert_continue_auth_called(C2lPacket::OtpNumber(CaOtpNumber {
        otp_number: String::new(),
    }))
    .await;
}

#[tokio::test]
async fn ca_pc_cert_number_handler_execute_calls_continue_auth_async() {
    assert_continue_auth_called(C2lPacket::PcCertNumber(CaPcCertNumber {
        cert_number: "12345678".into(),
    }))
    .await;
}

#[tokio::test]
async fn ca_pc_cert_number_handler_execute_null_cert_number_calls_continue_auth_async() {
    assert_continue_auth_called(C2lPacket::PcCertNumber(CaPcCertNumber {
        cert_number: String::new(),
    }))
    .await;
}

#[tokio::test]
async fn ca_request_auth_handler_execute_creates_flow_and_calls_authenticate_async() {
    let mut f = fixture();
    f.backend
        .set_korea_info(Some(korea_info(TEST_ACCOUNT_ID, "password")));
    let packet = C2lPacket::RequestAuth(CaRequestAuth {
        account: "testuser".into(),
        ..Default::default()
    });

    crate::handlers::handle_c2l(&f.server, &f.conn, packet)
        .await
        .unwrap();

    // A Korea flow for "testuser" was created and started by AuthenticateAsync.
    assert_eq!(
        *f.backend.korea_calls.lock().unwrap(),
        vec!["testuser".to_string()]
    );
    assert_eq!(f.conn.state(), LoginState::Authenticating);
    assert_eq!(f.sent.opcodes(), vec![l2c_op::AC_CHALLENGE2]);
}

#[tokio::test]
async fn ca_request_auth_trion_handler_execute_calls_authenticate_async() {
    let f = fixture();
    f.backend
        .set_login(LoginBehavior::Result(LoginResult::ok(TEST_ACCOUNT_ID)));
    let packet = C2lPacket::RequestAuthTrion(CaRequestAuthTrion {
        username: Some("testuser".into()),
        password: Some("testpass".into()),
        ..Default::default()
    });

    crate::handlers::handle_c2l(&f.server, &f.conn, packet)
        .await
        .unwrap();

    let calls = f.backend.login_calls.lock().unwrap().clone();
    assert_eq!(calls.len(), 1);
    assert_eq!(calls[0].0, "testuser");
    assert_eq!(calls[0].1.value, "testpass");
    assert_eq!(calls[0].1.kind, PasswordKind::Sha256Hex);
    assert_eq!(calls[0].2, TEST_IP);
    assert_eq!(f.conn.state(), LoginState::Authenticated);
}
