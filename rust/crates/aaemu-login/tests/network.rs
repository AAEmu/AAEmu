//! End-to-end test over real sockets, without a database: a fake game server registers, issues a
//! reconnection token, answers the character request and the enter-world request, while a fake
//! client authenticates with the token, lists worlds and enters the world.

use std::time::Duration;

use aaemu_commons::net::FrameCodec;
use aaemu_commons::PacketStream;
use aaemu_login::config::{lowercase_keys, AppConfig};
use aaemu_login::models::{AccountId, ConnectionId, GameServerId, GsLoad, LoginCharacterInfo};
use aaemu_login::packets::g2l::{
    GlGameServerLoad, GlPlayerEnter, GlPlayerReconnect, GlRegisterGameServer, GlRequestInfo,
};
use aaemu_login::packets::l2g::{
    self, LgPlayerEnter, LgPlayerReconnect, LgRegisterGameServer, LgRequestInfo,
};
use aaemu_login::packets::{l2c, split_opcode, OutgoingPacket};
use aaemu_login::server::LoginServer;
use aaemu_login::{db, network};
use futures::{SinkExt, StreamExt};
use tokio::net::TcpStream;
use tokio_util::codec::Framed;
use tokio_util::sync::CancellationToken;

type Conn = Framed<TcpStream, FrameCodec>;

async fn recv(c: &mut Conn) -> (u16, PacketStream) {
    let frame = tokio::time::timeout(Duration::from_secs(5), c.next())
        .await
        .expect("timely frame")
        .expect("open")
        .expect("ok");
    split_opcode(frame).unwrap()
}

async fn send<P: OutgoingPacket>(c: &mut Conn, p: &P) {
    c.send(p.encode_payload().unwrap()).await.unwrap();
}

async fn send_raw(c: &mut Conn, opcode: u16, body: impl FnOnce(&mut PacketStream)) {
    let mut s = PacketStream::new();
    s.write_u16(opcode);
    body(&mut s);
    c.send(s.into_bytes()).await.unwrap();
}

#[tokio::test]
async fn reconnect_list_world_and_enter_world() {
    let config = AppConfig::from_value(lowercase_keys(serde_json::json!({
        "SecretKey": "test",
        "SkipHostResolve": true,
        "InternalNetwork": { "Host": "127.0.0.1", "Port": 0 },
        "Network": { "Host": "127.0.0.1", "Port": 0 },
        "Connections": { "MySQLProvider": { "Host": "127.0.0.1", "Port": 1, "User": "x", "Password": "x", "Database": "x" } },
        "GameServers": [ { "Id": 1, "Name": "AAEmu", "Host": "127.0.0.1", "Port": 1239 } ]
    })))
    .unwrap();
    // Lazy pool: never connects in this test (no DB-backed flow is used).
    let pool = db::create_pool(db::connect_options(&config).unwrap());
    let server = LoginServer::new(config, pool);
    server.requests.initialize();
    server.games.load(&server.config).await;

    let shutdown = CancellationToken::new();
    let internal = network::internal::bind("127.0.0.1", 0).await.unwrap();
    let internal_addr = internal.local_addr().unwrap();
    tokio::spawn(network::internal::run(
        server.clone(),
        internal,
        shutdown.clone(),
    ));
    let login = network::login::bind("127.0.0.1", 0).await.unwrap();
    let login_addr = login.local_addr().unwrap();
    tokio::spawn(network::login::run(server.clone(), login, shutdown.clone()));

    // --- game server registers
    let mut gs = Framed::new(TcpStream::connect(internal_addr).await.unwrap(), FrameCodec);
    send(
        &mut gs,
        &GlRegisterGameServer {
            secret_key: "test".into(),
            gs_id: GameServerId(1),
            mirrors: vec![],
        },
    )
    .await;
    let (op, mut s) = recv(&mut gs).await;
    assert_eq!(op, l2g::opcodes::LG_REGISTER_GAME_SERVER);
    assert_eq!(
        LgRegisterGameServer::read(&mut s).unwrap().result,
        aaemu_login::models::GsRegisterResult::Success
    );
    send(
        &mut gs,
        &GlGameServerLoad {
            load: GsLoad::MEDIUM,
        },
    )
    .await;

    // --- game server hands out a reconnection token
    send(
        &mut gs,
        &GlPlayerReconnect {
            gs_id: GameServerId(1),
            account_id: AccountId(42),
            token: 777,
        },
    )
    .await;
    let (op, mut s) = recv(&mut gs).await;
    assert_eq!(op, l2g::opcodes::LG_PLAYER_RECONNECT);
    assert_eq!(LgPlayerReconnect::read(&mut s).unwrap().token, 777);

    // --- client reconnects with the token
    let mut client = Framed::new(TcpStream::connect(login_addr).await.unwrap(), FrameCodec);
    send_raw(&mut client, 0x00e, |s| {
        s.write_u32(0)
            .write_u32(0)
            .write_u32(42)
            .write_u8(1)
            .write_u32(777)
            .write_u16(0);
    })
    .await;
    let (op, s) = recv(&mut client).await;
    assert_eq!(op, l2c::opcodes::AC_JOIN_RESPONSE);
    // reason Success, afs: 6 characters per account, 0 additional, no pre-select period
    assert_eq!(s.unread(), [0, 0, 6, 0, 0, 0, 0, 0, 0, 0]);
    let (op, mut s) = recv(&mut client).await;
    assert_eq!(op, l2c::opcodes::AC_AUTH_RESPONSE);
    assert_eq!(s.read_u32().unwrap(), 42);
    assert_eq!(s.read_u16().unwrap(), 32); // wsk
    assert_eq!(s.read_bytes(32).unwrap(), [0u8; 32]);
    assert_eq!(s.read_u8().unwrap(), 6); // slotCount
    assert_eq!(s.remaining(), 0);

    // --- client asks for the world list; the login server asks the game server for characters
    send_raw(&mut client, 0x00b, |s| {
        s.write_u64(0);
    })
    .await;
    let (op, mut s) = recv(&mut gs).await;
    assert_eq!(op, l2g::opcodes::LG_REQUEST_INFO);
    let req = LgRequestInfo::read(&mut s).unwrap();
    assert_eq!(req.account_id, AccountId(42));
    let connection_id: ConnectionId = req.connection_id;
    let character = LoginCharacterInfo {
        id: 1,
        account_id: 42,
        gs_id: 0,
        name: "Bob".into(),
        race: 1,
        gender: 2,
    };
    send(
        &mut gs,
        &GlRequestInfo {
            connection_id,
            request_id: req.request_id,
            characters: vec![character.clone()],
        },
    )
    .await;

    let (op, s) = recv(&mut client).await;
    assert_eq!(op, l2c::opcodes::AC_WORLD_LIST);
    // 1 world: id 1, "AAEmu", active, con 1, rcon x9; 1 character (tagged with gsId 1)
    let expected = l2c::AcWorldList {
        worlds: vec![l2c::WorldInfo {
            id: GameServerId(1),
            name: "AAEmu".into(),
            active: true,
            load: GsLoad::MEDIUM,
        }],
        characters: vec![LoginCharacterInfo {
            gs_id: 1,
            ..character
        }],
    };
    assert_eq!(s.as_bytes(), expected.encode_payload().unwrap().as_slice());
    // Same bytes as the C# 1.2 ACWorldListPacket for this input (frame minus the u16 length).
    assert_eq!(
        hex::encode(s.as_bytes()),
        "0800010105004141456d750101000000000000000000012a00000001010000000300426f6201021000000000000000000000000000000000000000000000000000"
    );
    let conn = server
        .connections
        .get(connection_id)
        .expect("connection registered");
    assert_eq!(conn.get_characters().len(), 1);
    assert_eq!(conn.get_characters()[0].gs_id, 1);

    // --- client enters the world: the cookie is sent once the game server accepts
    send_raw(&mut client, 0x00c, |s| {
        s.write_u64(0).write_u8(1);
    })
    .await;
    let (op, mut s) = recv(&mut gs).await;
    assert_eq!(op, l2g::opcodes::LG_PLAYER_ENTER);
    assert_eq!(
        LgPlayerEnter::read(&mut s).unwrap(),
        LgPlayerEnter {
            account_id: AccountId(42),
            connection_id
        }
    );
    let cookie = l2c::AcWorldCookie::new(
        connection_id.0,
        "127.0.0.1".parse().unwrap(),
        true,
        "127.0.0.1",
        1239,
    )
    .encode_payload()
    .unwrap();
    send(
        &mut gs,
        &GlPlayerEnter {
            connection_id,
            gs_id: GameServerId(1),
            result: 0,
        },
    )
    .await;
    let (op, s) = recv(&mut client).await;
    assert_eq!(op, l2c::opcodes::AC_WORLD_COOKIE);
    assert_eq!(s.as_bytes(), cookie.as_slice());

    // --- a second attempt that the game server rejects is denied with its result code
    // (give the background task time to return the session to Authenticated)
    tokio::time::sleep(Duration::from_millis(100)).await;
    send_raw(&mut client, 0x00c, |s| {
        s.write_u64(0).write_u8(1);
    })
    .await;
    let (op, _) = recv(&mut gs).await;
    assert_eq!(op, l2g::opcodes::LG_PLAYER_ENTER);
    send(
        &mut gs,
        &GlPlayerEnter {
            connection_id,
            gs_id: GameServerId(1),
            result: 5,
        },
    )
    .await;
    let (op, s) = recv(&mut client).await;
    assert_eq!(op, l2c::opcodes::AC_ENTER_WORLD_DENIED);
    assert_eq!(s.unread(), [5]);

    // --- a bad reconnect token on a fresh connection is denied
    let mut client2 = Framed::new(TcpStream::connect(login_addr).await.unwrap(), FrameCodec);
    send_raw(&mut client2, 0x00e, |s| {
        s.write_u32(0)
            .write_u32(0)
            .write_u32(42)
            .write_u8(1)
            .write_u32(778)
            .write_u16(0);
    })
    .await;
    let (op, s) = recv(&mut client2).await;
    assert_eq!(op, l2c::opcodes::AC_LOGIN_DENIED);
    assert_eq!(s.as_bytes()[2], 2); // BadResponse

    // --- game server disconnect unregisters it
    drop(gs);
    tokio::time::sleep(Duration::from_millis(200)).await;
    assert!(!server
        .games
        .get_game_server(GameServerId(1))
        .unwrap()
        .active());

    shutdown.cancel();
}
