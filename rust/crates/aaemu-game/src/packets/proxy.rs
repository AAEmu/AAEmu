//! Proxy (level 2) packets (`AAEmu.Game.Core.Packets.Proxy`).

use std::sync::OnceLock;
use std::time::Instant;

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use super::packet_def;
use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PROXY;
use crate::net::registry::ClientPacket;
use crate::opcodes::pp;
use crate::packets::g2c::*;
use crate::server::GameServer;

// ---- S->C --------------------------------------------------------------------------------------

/// `ChangeStatePacket` (0x000).
#[derive(Debug, Clone)]
pub struct ChangeStatePacket {
    pub state: i32,
}
packet_def!(
    ChangeStatePacket,
    level = LEVEL_PROXY,
    opcode = pp::ChangeStatePacket
);

impl Marshal for ChangeStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.state);
    }
}

/// `SetGameTypePacket` (0x00F).
#[derive(Debug, Clone)]
pub struct SetGameTypePacket {
    pub level: String,
    pub checksum: u64,
    pub immersive: u8,
}
packet_def!(
    SetGameTypePacket,
    level = LEVEL_PROXY,
    opcode = pp::SetGameTypePacket
);

impl Marshal for SetGameTypePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.level)
            .write_u64(self.checksum)
            .write_u8(self.immersive);
    }
}

/// `PongPacket` (0x013).
#[derive(Debug, Clone)]
pub struct PongPacket {
    pub tm: i64,
    pub when: i64,
    pub local: u32,
    /// `Environment.TickCount & int.MaxValue`.
    pub world: u32,
}
packet_def!(PongPacket, level = LEVEL_PROXY, opcode = pp::PongPacket);

impl Marshal for PongPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.tm)
            .write_i64(self.when)
            .write_i64(0) // elapsed
            .write_i64(i64::from(self.world) * 1000) // remote
            .write_u32(self.local)
            .write_u32(self.world);
    }
}

/// `FastPongPacket` (0x016).
#[derive(Debug, Clone)]
pub struct FastPongPacket {
    pub sent: u32,
}
packet_def!(
    FastPongPacket,
    level = LEVEL_PROXY,
    opcode = pp::FastPongPacket
);

impl Marshal for FastPongPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.sent);
    }
}

/// Millisecond tick counter standing in for `Environment.TickCount` (time since process start
/// instead of since boot; only used as an opaque timestamp by the client).
fn tick_count() -> u32 {
    static START: OnceLock<Instant> = OnceLock::new();
    (START.get_or_init(Instant::now).elapsed().as_millis() as u64 & u64::from(i32::MAX as u32))
        as u32
}

// ---- C->S --------------------------------------------------------------------------------------

/// `FinishStatePacket` (0x001): drives the CryNetwork state machine and, for state 0, sends the
/// lobby configuration burst.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FinishStatePacket {
    pub state: i32,
}

impl FinishStatePacket {
    /// Test URLs (C#: "TODO - config files"). The client treats them as folders.
    pub const AUTH_URL: &'static str = "http://localhost/aaemu/login";
    pub const PLATFORM_URL: &'static str = "http://localhost/aaemu/platform";
    pub const COMMERCE_URL: &'static str = "http://localhost/aaemu/shop";
    /// `levelname` when `Connection.ActiveChar` is null (always the case in the lobby).
    pub const LOBBY_LEVEL: &'static str = "w_hanuimaru_1";
    /// `_scAccountInitPacket`.
    pub const ACCOUNT_INIT: [bool; 2] = [false, true];
    /// `_scLevelRestrictionInitPacket`.
    pub const LEVEL_RESTRICTION_INIT: [u8; 15] = [0, 15, 15, 15, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0, 15];
}

impl ClientPacket for FinishStatePacket {
    const NAME: &'static str = "FinishStatePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            state: s.read_i32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            match self.state {
                0 => {
                    conn.send(&ChangeStatePacket { state: 1 });
                    // Later phase: with an ActiveChar the level name comes from
                    // ZoneManager.GetZoneByKey(ActiveChar.Transform.ZoneId)?.Name; ActiveChar is
                    // never set in this phase.
                    conn.send(&SetGameTypePacket {
                        level: Self::LOBBY_LEVEL.into(),
                        checksum: 0,
                        immersive: 1,
                    });
                    conn.send(&SCInitialConfigPacket {
                        fsets: server.game_data.features.clone(),
                    });
                    conn.send(&SCTrionConfigPacket {
                        activate: true,
                        auth_url: Self::AUTH_URL.into(),
                        platform_url: Self::PLATFORM_URL.into(),
                        commerce_url: Self::COMMERCE_URL.into(),
                    });
                    let p = conn.payment.clone();
                    conn.send(&SCAccountInfoPacket {
                        pay_method: p.method as i32,
                        pay_location: p.location,
                        pay_start: p.start_time,
                        pay_end: p.end_time,
                    });
                    conn.send(&SCChatSpamDelayPacket);
                    conn.send(&SCAccountAttributeConfigPacket {
                        used: Self::ACCOUNT_INIT,
                    });
                    conn.send(&SCLevelRestrictionConfigPacket {
                        search_level: 10,
                        bid_level: 10,
                        post_level: 10,
                        trade: 10,
                        mail: 10,
                        limit_levels: Self::LEVEL_RESTRICTION_INIT,
                    });
                }
                1 => conn.send(&ChangeStatePacket { state: 2 }),
                2 => conn.send(&ChangeStatePacket { state: 3 }),
                3..=6 => conn.send(&ChangeStatePacket {
                    state: self.state + 1,
                }),
                7 => conn.send(&SCUpdatePremiumPointPacket {
                    point: 1,
                    old_pg: 1,
                    pg: 1,
                }),
                s => tracing::info!("Unknown state: {s}"),
            }
            Ok(())
        })
    }
}

/// `PingPacket` (0x012).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PingPacket {
    pub tm: i64,
    pub when: i64,
    pub local: u32,
}

impl ClientPacket for PingPacket {
    const NAME: &'static str = "PingPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            tm: s.read_i64()?,
            when: s.read_i64()?,
            local: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            conn.last_ping = Instant::now();
            conn.send(&PongPacket {
                tm: self.tm,
                when: self.when,
                local: self.local,
                world: tick_count(),
            });
            Ok(())
        })
    }
}

/// `FastPingPacket` (0x015).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FastPingPacket {
    pub sent: u32,
}

impl ClientPacket for FastPingPacket {
    const NAME: &'static str = "FastPingPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            sent: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            conn.send(&FastPongPacket { sent: self.sent });
            Ok(())
        })
    }
}

/// C->S proxy packets whose C# `Read` only consumes (or ignores) fields.
macro_rules! inert_packet {
    ($(#[$m:meta])* $name:ident, |$s:ident| $read:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq)]
        pub struct $name;

        impl ClientPacket for $name {
            const NAME: &'static str = stringify!($name);

            fn read($s: &mut PacketStream) -> Result<Self> {
                $read
                Ok(Self)
            }

            fn handle<'a>(self, _server: &'a GameServer, _conn: &'a mut GameConnection) -> BoxFuture<'a, Result<()>> {
                Box::pin(async { Ok(()) })
            }
        }
    };
    ($(#[$m:meta])* $name:ident) => {
        inert_packet!($(#[$m])* $name, |_s| {
            // C#: PacketMarshaler.Read warns "doesn't inherit Read()".
            tracing::trace!("{} doesn't inherit Read()", stringify!($name));
        });
    };
}

inert_packet!(
    /// `ChangeStatePacket` received from the client (0x000).
    ChangeStateClientPacket
);
inert_packet!(
    /// 0x002
    FlushMsgsPacket
);
inert_packet!(
    /// 0x004
    UpdatePhysicsTimePacket,
    |s| {
        let _tm = s.read_i64()?;
    }
);
inert_packet!(BeginUpdateObjPacket);
inert_packet!(EndUpdateObjPacket);
inert_packet!(BeginBindObjPacket);
inert_packet!(EndBindObjPacket);
inert_packet!(UnbindPredictedObjPacket);
inert_packet!(RemoveStaticObjPacket);
inert_packet!(VoiceDataPacket);
inert_packet!(UpdateAspectPacket);
inert_packet!(SetAspectProfilePacket);
inert_packet!(PartialAspectPacket);
inert_packet!(
    /// 0x00F received from the client.
    SetGameTypeClientPacket
);
inert_packet!(
    /// 0x010
    ChangeCVarPacket,
    |s| {
        let key_len = s.read_u16()?;
        let _key = s.read_bytes(usize::from(key_len))?;
        let value_len = s.read_u16()?;
        let _value = s.read_bytes(usize::from(value_len))?;
    }
);
inert_packet!(
    /// 0x011
    EntityClassRegistrationPacket,
    |s| {
        let _i = s.read_u16()?;
        let _name = s.read_string()?;
    }
);
inert_packet!(
    /// 0x013 received from the client.
    PongClientPacket
);
inert_packet!(
    /// 0x014
    PacketSeqChange,
    |s| {
        let _seq = s.read_u8()?;
    }
);
inert_packet!(
    /// 0x016 received from the client.
    FastPongClientPacket
);

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    #[test]
    fn proxy_frames() {
        assert_eq!(
            encode(&ChangeStatePacket { state: 1 }),
            vec![0xDD, 0x02, 0x00, 0x00, 1, 0, 0, 0]
        );
        let sgt = encode(&SetGameTypePacket {
            level: "login".into(),
            checksum: 0,
            immersive: 1,
        });
        assert_eq!(
            sgt,
            vec![
                0xDD, 0x02, 0x0F, 0x00, 5, 0, b'l', b'o', b'g', b'i', b'n', 0, 0, 0, 0, 0, 0, 0, 0,
                1
            ]
        );
        let pong = encode(&PongPacket {
            tm: 1,
            when: 2,
            local: 3,
            world: 4,
        });
        let mut want = vec![0xDD, 0x02, 0x13, 0x00];
        for v in [1i64, 2, 0, 4000] {
            want.extend_from_slice(&v.to_le_bytes());
        }
        want.extend_from_slice(&3u32.to_le_bytes());
        want.extend_from_slice(&4u32.to_le_bytes());
        assert_eq!(pong, want);
        assert_eq!(
            encode(&FastPongPacket { sent: 9 }),
            vec![0xDD, 0x02, 0x16, 0x00, 9, 0, 0, 0]
        );
    }
}
