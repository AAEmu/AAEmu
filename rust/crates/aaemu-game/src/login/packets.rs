//! Internal game <-> login protocol (`Core/Packets/G2L`, `Core/Packets/L2G`).
//!
//! Frame payload: `[u16 opcode][body]` (`LoginPacket.Encode`), length-prefixed by `FrameCodec`.

use aaemu_commons::{Marshal, PacketStream};

use crate::db::LoginCharacterInfo;
use crate::error::Result;
use crate::opcodes::{gl, lg};

/// A G2L packet.
pub trait LoginOutPacket: Marshal + std::fmt::Debug {
    const OPCODE: u16;
    const NAME: &'static str;
}

/// `LoginPacket.Encode` (without the length prefix).
pub fn encode<P: LoginOutPacket>(packet: &P) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_u16(P::OPCODE);
    packet.write(&mut s);
    s.into_bytes()
}

/// `GLRegisterGameServerPacket` (0x000).
#[derive(Debug, Clone)]
pub struct GLRegisterGameServerPacket {
    pub secret_key: String,
    pub gs_id: u8,
    pub additional_gs_ids: Vec<u8>,
}

impl LoginOutPacket for GLRegisterGameServerPacket {
    const OPCODE: u16 = gl::GLRegisterGameServerPacket;
    const NAME: &'static str = "GLRegisterGameServerPacket";
}

impl Marshal for GLRegisterGameServerPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.secret_key)
            .write_u8(self.gs_id)
            .write_i32(self.additional_gs_ids.len() as i32);
        for id in &self.additional_gs_ids {
            s.write_u8(*id);
        }
    }
}

/// `GLPlayerEnterPacket` (0x001).
#[derive(Debug, Clone)]
pub struct GLPlayerEnterPacket {
    pub connection_id: u32,
    pub gs_id: u8,
    pub result: u8,
}

impl LoginOutPacket for GLPlayerEnterPacket {
    const OPCODE: u16 = gl::GLPlayerEnterPacket;
    const NAME: &'static str = "GLPlayerEnterPacket";
}

impl Marshal for GLPlayerEnterPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.connection_id)
            .write_u8(self.gs_id)
            .write_u8(self.result);
    }
}

/// `GLPlayerReconnectPacket` (0x002).
#[derive(Debug, Clone)]
pub struct GLPlayerReconnectPacket {
    pub gs_id: u8,
    pub account_id: u32,
    pub connection_id: u32,
}

impl LoginOutPacket for GLPlayerReconnectPacket {
    const OPCODE: u16 = gl::GLPlayerReconnectPacket;
    const NAME: &'static str = "GLPlayerReconnectPacket";
}

impl Marshal for GLPlayerReconnectPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.gs_id)
            .write_u32(self.account_id)
            .write_u32(self.connection_id);
    }
}

/// `GLRequestInfoPacket` (0x003).
#[derive(Debug, Clone)]
pub struct GLRequestInfoPacket {
    pub connection_id: u32,
    pub request_id: u32,
    pub characters: Vec<LoginCharacterInfo>,
}

impl LoginOutPacket for GLRequestInfoPacket {
    const OPCODE: u16 = gl::GLRequestInfoPacket;
    const NAME: &'static str = "GLRequestInfoPacket";
}

impl Marshal for LoginCharacterInfo {
    /// `LoginCharacterInfo.Write` (`GsId` is not serialised).
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.account_id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.gender);
    }
}

impl Marshal for GLRequestInfoPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.connection_id)
            .write_u32(self.request_id)
            .write_collection(&self.characters);
    }
}

/// `GLGameServerLoadPacket` (0x004).
#[derive(Debug, Clone)]
pub struct GLGameServerLoadPacket {
    pub load: u8,
}

impl LoginOutPacket for GLGameServerLoadPacket {
    const OPCODE: u16 = gl::GLGameServerLoadPacket;
    const NAME: &'static str = "GLGameServerLoadPacket";
}

impl Marshal for GLGameServerLoadPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.load);
    }
}

/// Decoded L2G packets.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum LoginInPacket {
    /// `LGRegisterGameServerPacket` (0x000): `u8 result` (0 = ok).
    RegisterGameServer {
        result: u8,
    },
    /// `LGPlayerEnterPacket` (0x001): `u32 accountId, u32 connectionId`.
    PlayerEnter {
        account_id: u32,
        connection_id: u32,
    },
    /// `LGPlayerReconnectPacket` (0x002): `u32 token` (the game connection id previously sent in
    /// `GLPlayerReconnectPacket`; the C# local is misleadingly named `accountId`).
    PlayerReconnect {
        token: u32,
    },
    /// `LGRequestInfoPacket` (0x003): `u32 connectionId, u32 requestId, u32 accountId`.
    /// The login server writes the account id as a `u64`; the game reads the low 32 bits and
    /// ignores the rest.
    RequestInfo {
        connection_id: u32,
        request_id: u32,
        account_id: u32,
    },
    Unknown {
        opcode: u16,
        body: Vec<u8>,
    },
}

impl LoginInPacket {
    /// `LoginProtocolHandler.OnReceive` dispatch on one frame payload.
    pub fn decode(payload: &[u8]) -> Result<Self> {
        let mut s = PacketStream::from_bytes(payload.to_vec());
        let opcode = s.read_u16()?;
        Ok(match opcode {
            lg::LGRegisterGameServerPacket => Self::RegisterGameServer {
                result: s.read_u8()?,
            },
            lg::LGPlayerEnterPacket => Self::PlayerEnter {
                account_id: s.read_u32()?,
                connection_id: s.read_u32()?,
            },
            lg::LGPlayerReconnectPacket => Self::PlayerReconnect {
                token: s.read_u32()?,
            },
            lg::LGRequestInfoPacket => Self::RequestInfo {
                connection_id: s.read_u32()?,
                request_id: s.read_u32()?,
                account_id: s.read_u32()?,
            },
            _ => Self::Unknown {
                opcode,
                body: s.unread().to_vec(),
            },
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn g2l_layouts() {
        let p = GLRegisterGameServerPacket {
            secret_key: "test".into(),
            gs_id: 1,
            additional_gs_ids: vec![2, 3],
        };
        assert_eq!(
            encode(&p),
            vec![0, 0, 4, 0, b't', b'e', b's', b't', 1, 2, 0, 0, 0, 2, 3]
        );
        assert_eq!(
            encode(&GLPlayerEnterPacket {
                connection_id: 0x01020304,
                gs_id: 1,
                result: 0
            }),
            vec![1, 0, 4, 3, 2, 1, 1, 0]
        );
        assert_eq!(
            encode(&GLPlayerReconnectPacket {
                gs_id: 1,
                account_id: 5,
                connection_id: 6
            }),
            vec![2, 0, 1, 5, 0, 0, 0, 6, 0, 0, 0]
        );
        let info = GLRequestInfoPacket {
            connection_id: 7,
            request_id: 8,
            characters: vec![LoginCharacterInfo {
                id: 9,
                account_id: 10,
                name: "Ab".into(),
                race: 1,
                gender: 2,
            }],
        };
        assert_eq!(
            encode(&info),
            vec![
                3, 0, 7, 0, 0, 0, 8, 0, 0, 0, 1, 0, 0, 0, 9, 0, 0, 0, 10, 0, 0, 0, 2, 0, b'A',
                b'b', 1, 2
            ]
        );
        assert_eq!(encode(&GLGameServerLoadPacket { load: 2 }), vec![4, 0, 2]);
    }

    #[test]
    fn l2g_decode() {
        assert_eq!(
            LoginInPacket::decode(&[0, 0, 0]).unwrap(),
            LoginInPacket::RegisterGameServer { result: 0 }
        );
        assert_eq!(
            LoginInPacket::decode(&[1, 0, 5, 0, 0, 0, 6, 0, 0, 0]).unwrap(),
            LoginInPacket::PlayerEnter {
                account_id: 5,
                connection_id: 6
            }
        );
        assert_eq!(
            LoginInPacket::decode(&[2, 0, 9, 0, 0, 0]).unwrap(),
            LoginInPacket::PlayerReconnect { token: 9 }
        );
        // login writes the account id as u64
        assert_eq!(
            LoginInPacket::decode(&[3, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0]).unwrap(),
            LoginInPacket::RequestInfo {
                connection_id: 1,
                request_id: 2,
                account_id: 3
            }
        );
        assert!(matches!(
            LoginInPacket::decode(&[9, 0]).unwrap(),
            LoginInPacket::Unknown { opcode: 9, .. }
        ));
    }
}
