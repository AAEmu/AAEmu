//! Game server -> login server packets (`AAEmu.Login.Core.Packets.G2L`).
//!
//! Decoding uses the lenient C# `PacketStream` semantics. Each packet can also be encoded
//! (via [`OutgoingPacket`]) so a Rust game server can reuse these definitions.

use aaemu_commons::{Marshal, PacketStream};

use super::{OutgoingPacket, PacketError};
use crate::lenient::LenientRead;
use crate::models::{AccountId, ConnectionId, GameServerId, GsLoad, LoginCharacterInfo};

/// `GLOffsets` — all opcodes here are updated for version client_12_r208022.
pub mod opcodes {
    pub const GL_REGISTER_GAME_SERVER: u16 = 0x000;
    pub const GL_PLAYER_ENTER: u16 = 0x001;
    pub const GL_PLAYER_RECONNECT: u16 = 0x002;
    pub const GL_REQUEST_INFO: u16 = 0x003;
    pub const GL_GAME_SERVER_LOAD: u16 = 0x004;
}

/// `GLRegisterGameServerPacket`: a game server registers itself.
/// Body: `[str secretKey][u8 gsId][i32 mirrorCount][u8 mirrorId x mirrorCount]`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GlRegisterGameServer {
    pub secret_key: String,
    pub gs_id: GameServerId,
    pub mirrors: Vec<GameServerId>,
}

impl GlRegisterGameServer {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        let secret_key = s.l_string();
        let gs_id = GameServerId(s.l_u8());
        let count = s.l_i32();
        if count < 0 {
            // C#: `new List<GameServerId>(negative)` throws.
            return Err(PacketError::NegativeCount(count));
        }
        let mirrors = (0..count).map(|_| GameServerId(s.l_u8())).collect();
        Ok(Self {
            secret_key,
            gs_id,
            mirrors,
        })
    }
}

impl OutgoingPacket for GlRegisterGameServer {
    const OPCODE: u16 = opcodes::GL_REGISTER_GAME_SERVER;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_str(&self.secret_key)
            .write_u8(self.gs_id.0)
            .write_i32(self.mirrors.len() as i32);
        for m in &self.mirrors {
            s.write_u8(m.0);
        }
        Ok(())
    }
}

/// `GLPlayerEnterPacket`: result of an `LGPlayerEnterPacket`.
/// Body: `[u32 connectionId][u8 gsId][u8 result]` (result 0 = success).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GlPlayerEnter {
    pub connection_id: ConnectionId,
    pub gs_id: GameServerId,
    pub result: u8,
}

impl GlPlayerEnter {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            connection_id: ConnectionId(s.l_u32()),
            gs_id: GameServerId(s.l_u8()),
            result: s.l_u8(),
        }
    }
}

impl OutgoingPacket for GlPlayerEnter {
    const OPCODE: u16 = opcodes::GL_PLAYER_ENTER;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.connection_id.0)
            .write_u8(self.gs_id.0)
            .write_u8(self.result);
        Ok(())
    }
}

/// `GLPlayerReconnectPacket`: registers a reconnection token for an account.
/// Body: `[u8 gsId][u32 accountId][u32 token]`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GlPlayerReconnect {
    pub gs_id: GameServerId,
    pub account_id: AccountId,
    pub token: u32,
}

impl GlPlayerReconnect {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            gs_id: GameServerId(s.l_u8()),
            account_id: AccountId(s.l_u32()),
            token: s.l_u32(),
        }
    }
}

impl OutgoingPacket for GlPlayerReconnect {
    const OPCODE: u16 = opcodes::GL_PLAYER_RECONNECT;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.gs_id.0)
            .write_u32(self.account_id.0)
            .write_u32(self.token);
        Ok(())
    }
}

/// `GLRequestInfoPacket`: the characters an account has on the game server, in response to
/// `LGRequestInfoPacket`.
/// Body: `[u32 connectionId][u32 requestId][i32 count]` then per character
/// `[u32 id][u32 accountId][str name][u8 race][u8 gender]`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GlRequestInfo {
    pub connection_id: ConnectionId,
    pub request_id: u32,
    pub characters: Vec<LoginCharacterInfo>,
}

impl GlRequestInfo {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        let connection_id = ConnectionId(s.l_u32());
        let request_id = s.l_u32();
        let count = s.l_i32();
        if count < 0 {
            return Err(PacketError::NegativeCount(count));
        }
        let mut characters = Vec::new();
        for _ in 0..count {
            // Deviation: the C# ReadCollection keeps appending default entries once the stream
            // is exhausted (up to `count`, which may be ~2^31). Stop instead.
            if s.remaining() == 0 {
                tracing::error!("GLRequestInfo: character count {count} exceeds packet data");
                break;
            }
            characters.push(LoginCharacterInfo::read_lenient(s));
        }
        Ok(Self {
            connection_id,
            request_id,
            characters,
        })
    }
}

impl OutgoingPacket for GlRequestInfo {
    const OPCODE: u16 = opcodes::GL_REQUEST_INFO;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.connection_id.0)
            .write_u32(self.request_id)
            .write_i32(self.characters.len() as i32);
        for c in &self.characters {
            c.write(s);
        }
        Ok(())
    }
}

/// `GLGameServerLoadPacket`: the game server's current load. Body: `[u8 load]`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GlGameServerLoad {
    pub load: GsLoad,
}

impl GlGameServerLoad {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            load: GsLoad(s.l_u8()),
        }
    }
}

impl OutgoingPacket for GlGameServerLoad {
    const OPCODE: u16 = opcodes::GL_GAME_SERVER_LOAD;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.load.0);
        Ok(())
    }
}

/// A decoded G2L packet.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum G2lPacket {
    RegisterGameServer(GlRegisterGameServer),
    PlayerEnter(GlPlayerEnter),
    PlayerReconnect(GlPlayerReconnect),
    RequestInfo(GlRequestInfo),
    GameServerLoad(GlGameServerLoad),
}

impl G2lPacket {
    /// Decodes the body for `opcode`; `Ok(None)` for an unknown opcode.
    pub fn decode(opcode: u16, s: &mut PacketStream) -> Result<Option<Self>, PacketError> {
        Ok(Some(match opcode {
            opcodes::GL_REGISTER_GAME_SERVER => {
                Self::RegisterGameServer(GlRegisterGameServer::read(s)?)
            }
            opcodes::GL_PLAYER_ENTER => Self::PlayerEnter(GlPlayerEnter::read(s)),
            opcodes::GL_PLAYER_RECONNECT => Self::PlayerReconnect(GlPlayerReconnect::read(s)),
            opcodes::GL_REQUEST_INFO => Self::RequestInfo(GlRequestInfo::read(s)?),
            opcodes::GL_GAME_SERVER_LOAD => Self::GameServerLoad(GlGameServerLoad::read(s)),
            _ => return Ok(None),
        }))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::packets::{hex_bytes, split_opcode};

    fn decode(frame_payload: Vec<u8>) -> G2lPacket {
        let (op, mut s) = split_opcode(frame_payload).unwrap();
        G2lPacket::decode(op, &mut s).unwrap().unwrap()
    }

    #[test]
    fn character_info_matches_csharp() {
        let c = LoginCharacterInfo {
            id: 10,
            account_id: 20,
            gs_id: 0,
            name: "Bob".into(),
            race: 1,
            gender: 2,
        };
        let mut s = PacketStream::new();
        s.write(&c);
        assert_eq!(
            s.as_bytes(),
            hex_bytes("0a000000140000000300426f620102").as_slice()
        );
        let mut r = PacketStream::from_bytes(s.into_bytes());
        assert_eq!(LoginCharacterInfo::read_lenient(&mut r), c);
    }

    #[test]
    fn register_game_server() {
        // [opcode 0][str "test"][gs 1][count 2][2][3]
        let bytes = hex_bytes("0000040074657374010200000002")
            .into_iter()
            .chain([3])
            .collect();
        let p = decode(bytes);
        assert_eq!(
            p,
            G2lPacket::RegisterGameServer(GlRegisterGameServer {
                secret_key: "test".into(),
                gs_id: GameServerId(1),
                mirrors: vec![GameServerId(2), GameServerId(3)],
            })
        );
        if let G2lPacket::RegisterGameServer(r) = &p {
            assert_eq!(
                r.encode_payload().unwrap(),
                hex_bytes("000004007465737401020000000203")
            );
        }
    }

    #[test]
    fn simple_packets() {
        assert_eq!(
            decode(hex_bytes("0100090000000102")),
            G2lPacket::PlayerEnter(GlPlayerEnter {
                connection_id: ConnectionId(9),
                gs_id: GameServerId(1),
                result: 2
            })
        );
        assert_eq!(
            decode(hex_bytes("02000105000000efbeadde")),
            G2lPacket::PlayerReconnect(GlPlayerReconnect {
                gs_id: GameServerId(1),
                account_id: AccountId(5),
                token: 0xDEAD_BEEF
            })
        );
        assert_eq!(
            decode(hex_bytes("040002")),
            G2lPacket::GameServerLoad(GlGameServerLoad { load: GsLoad::HIGH })
        );
    }

    #[test]
    fn request_info_roundtrip() {
        let p = GlRequestInfo {
            connection_id: ConnectionId(3),
            request_id: 4,
            characters: vec![
                LoginCharacterInfo {
                    id: 1,
                    account_id: 2,
                    gs_id: 0,
                    name: "A".into(),
                    race: 3,
                    gender: 1,
                },
                LoginCharacterInfo {
                    id: 5,
                    account_id: 2,
                    gs_id: 0,
                    name: "Bé".into(),
                    race: 4,
                    gender: 2,
                },
            ],
        };
        assert_eq!(
            decode(p.encode_payload().unwrap()),
            G2lPacket::RequestInfo(p)
        );
    }

    #[test]
    fn negative_counts_fail_and_unknown_is_none() {
        let (op, mut s) = split_opcode(hex_bytes("03000100000002000000ffffffff")).unwrap();
        assert!(G2lPacket::decode(op, &mut s).is_err());
        let (op, mut s) = split_opcode(hex_bytes("0900")).unwrap();
        assert_eq!(G2lPacket::decode(op, &mut s).unwrap(), None);
    }
}
