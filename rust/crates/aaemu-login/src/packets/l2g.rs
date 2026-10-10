//! Login server -> game server packets (`AAEmu.Login.Core.Packets.L2G`).
//!
//! Each packet also has a `read` so a Rust game server can reuse these definitions.

use aaemu_commons::PacketStream;

use super::{OutgoingPacket, PacketError};
use crate::models::{AccountId, ConnectionId, GsRegisterResult};

/// `LGOffsets` — all opcodes here are updated for version client_12_r208022.
pub mod opcodes {
    pub const LG_REGISTER_GAME_SERVER: u16 = 0x000;
    pub const LG_PLAYER_ENTER: u16 = 0x001;
    pub const LG_PLAYER_RECONNECT: u16 = 0x002;
    pub const LG_REQUEST_INFO: u16 = 0x003;
}

/// `LGRegisterGameServerPacket`: response to `GLRegisterGameServerPacket`.
/// Body: `[u8 result]` (0 = success, 1 = error).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LgRegisterGameServer {
    pub result: GsRegisterResult,
}

impl LgRegisterGameServer {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        let result = if s.read_u8()? == 0 {
            GsRegisterResult::Success
        } else {
            GsRegisterResult::Error
        };
        Ok(Self { result })
    }
}

impl OutgoingPacket for LgRegisterGameServer {
    const OPCODE: u16 = opcodes::LG_REGISTER_GAME_SERVER;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.result as u8);
        Ok(())
    }
}

/// `LGPlayerEnterPacket`: asks the game server to accept a player.
/// Body: `[u32 accountId][u32 connectionId]`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LgPlayerEnter {
    pub account_id: AccountId,
    pub connection_id: ConnectionId,
}

impl LgPlayerEnter {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        Ok(Self {
            account_id: AccountId(s.read_u32()?),
            connection_id: ConnectionId(s.read_u32()?),
        })
    }
}

impl OutgoingPacket for LgPlayerEnter {
    const OPCODE: u16 = opcodes::LG_PLAYER_ENTER;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.account_id.0)
            .write_u32(self.connection_id.0);
        Ok(())
    }
}

/// `LGPlayerReconnectPacket`: acknowledges `GLPlayerReconnectPacket`. Body: `[u32 token]`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LgPlayerReconnect {
    pub token: u32,
}

impl LgPlayerReconnect {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        Ok(Self {
            token: s.read_u32()?,
        })
    }
}

impl OutgoingPacket for LgPlayerReconnect {
    const OPCODE: u16 = opcodes::LG_PLAYER_RECONNECT;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.token);
        Ok(())
    }
}

/// `LGRequestInfoPacket`: requests the characters an account has on the game server.
/// Body: `[u32 connectionId][u32 requestId][u64 accountId]` (note: account id widened to 64 bits).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LgRequestInfo {
    pub connection_id: ConnectionId,
    pub request_id: u32,
    pub account_id: AccountId,
}

impl LgRequestInfo {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        Ok(Self {
            connection_id: ConnectionId(s.read_u32()?),
            request_id: s.read_u32()?,
            account_id: AccountId(s.read_u64()? as u32),
        })
    }
}

impl OutgoingPacket for LgRequestInfo {
    const OPCODE: u16 = opcodes::LG_REQUEST_INFO;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.connection_id.0)
            .write_u32(self.request_id)
            .write_u64(u64::from(self.account_id.0));
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    //! Expected bytes were produced by the C# `InternalPacket.Encode()`.
    use super::*;
    use crate::packets::split_opcode;

    fn frame<P: OutgoingPacket>(p: &P) -> String {
        hex::encode(p.encode_frame().unwrap())
    }

    #[test]
    fn encodings_match_csharp() {
        assert_eq!(
            frame(&LgRegisterGameServer {
                result: GsRegisterResult::Success
            }),
            "0300000000"
        );
        assert_eq!(
            frame(&LgRegisterGameServer {
                result: GsRegisterResult::Error
            }),
            "0300000001"
        );
        let enter = LgPlayerEnter {
            account_id: AccountId(5),
            connection_id: ConnectionId(9),
        };
        assert_eq!(frame(&enter), "0a0001000500000009000000");
        assert_eq!(
            frame(&LgPlayerReconnect { token: 0xDEAD_BEEF }),
            "06000200efbeadde"
        );
        let info = LgRequestInfo {
            connection_id: ConnectionId(3),
            request_id: 4,
            account_id: AccountId(0x8000_0005),
        };
        assert_eq!(frame(&info), "1200030003000000040000000500008000000000");
    }

    #[test]
    fn roundtrip() {
        let info = LgRequestInfo {
            connection_id: ConnectionId(3),
            request_id: 4,
            account_id: AccountId(77),
        };
        let (op, mut s) = split_opcode(info.encode_payload().unwrap()).unwrap();
        assert_eq!(op, opcodes::LG_REQUEST_INFO);
        assert_eq!(LgRequestInfo::read(&mut s).unwrap(), info);
        let enter = LgPlayerEnter {
            account_id: AccountId(5),
            connection_id: ConnectionId(9),
        };
        let (_, mut s) = split_opcode(enter.encode_payload().unwrap()).unwrap();
        assert_eq!(LgPlayerEnter::read(&mut s).unwrap(), enter);
    }
}
