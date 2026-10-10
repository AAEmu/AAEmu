//! Portal / teleport packets (`Core/Packets/C2G`, `Core/Packets/G2C`, 1.2 wire formats).

use aaemu_commons::{helpers, Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::PortalWire;
use crate::server::GameServer;

// ------------------------------------------------------------------------------------------
// C -> S
// ------------------------------------------------------------------------------------------

/// `CSUsePortalPacket` (0x0de in 1.2).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSUsePortalPacket {
    pub obj_id: u32,
    pub only_my_portal: bool,
}

impl ClientPacket for CSUsePortalPacket {
    const NAME: &'static str = "CSUsePortalPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            only_my_portal: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "UsePortal, ObjId: {}, OnlyMyPortal: {}",
                self.obj_id,
                self.only_my_portal
            );
            // `Connection.ActiveChar` is null outside the world: the C# throws (NRE) in
            // `UsePortal`, which only ends this packet.
            if let Some(me) = conn.active_char.clone() {
                super::use_portal(server, &me, self.obj_id);
            }
            Ok(())
        })
    }
}

/// `CSDeletePortalPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSDeletePortalPacket {
    pub portal_type: u8,
    pub portal_id: u32,
}

impl ClientPacket for CSDeletePortalPacket {
    const NAME: &'static str = "CSDeletePortalPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            portal_type: s.read_u8()?,
            portal_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "DeletePortal, PortalType: {}, PortalId: {}",
                self.portal_type,
                self.portal_id
            );
            if let Some(me) = conn.active_char.clone() {
                super::delete_portal(server, &me, self.portal_type, self.portal_id);
            }
            Ok(())
        })
    }
}

/// `CSNaviOpenPortalPacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSNaviOpenPortalPacket {
    pub obj_id: u32,
}

impl ClientPacket for CSNaviOpenPortalPacket {
    const NAME: &'static str = "CSNaviOpenPortalPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("NaviOpenPortal, ObjId: {}", self.obj_id);
            Ok(())
        })
    }
}

/// `CSNaviTeleportPacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSNaviTeleportPacket {
    pub obj_id: u32,
}

impl ClientPacket for CSNaviTeleportPacket {
    const NAME: &'static str = "CSNaviTeleportPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("NaviTeleport, ObjId: {}", self.obj_id);
            Ok(())
        })
    }
}

/// `CSUseTeleportPacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSUseTeleportPacket {
    pub return_point_id: u32,
    pub money_amount: i32,
    pub npc_unit_id: u32,
}

impl ClientPacket for CSUseTeleportPacket {
    const NAME: &'static str = "CSUseTeleportPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            return_point_id: s.read_u32()?,
            money_amount: s.read_i32()?,
            npc_unit_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "CSUseTeleport, ReturnPointId: {}, MoneyAmount: {}, NpcUnitId: {}",
                self.return_point_id,
                self.money_amount,
                self.npc_unit_id
            );
            Ok(())
        })
    }
}

/// `CSTeleportEndedPacket`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CSTeleportEndedPacket {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub ori: [u8; 16],
}

impl ClientPacket for CSTeleportEndedPacket {
    const NAME: &'static str = "CSTeleportEndedPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        let x = helpers::convert_long_to_coord(s.read_i64()?);
        let y = helpers::convert_long_to_coord(s.read_i64()?);
        let z = s.read_f32()?;
        let mut ori = [0u8; 16];
        for b in ori.iter_mut() {
            *b = s.read_u8()?;
        }
        Ok(Self { x, y, z, ori })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            me.data().disabled_set_position = false;
            tracing::warn!("TeleportEnded, X: {}, Y: {}, Z: {}", self.x, self.y, self.z);
            super::resend_visible_objects_to_character(server, &me);
            Ok(())
        })
    }
}

/// Registers the portal C->S packets (`GameNetwork` 1.2 registrations).
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSUseTeleportPacket>(LEVEL_PLAIN, cs::CSUseTeleportPacket);
    reg.register::<CSTeleportEndedPacket>(LEVEL_PLAIN, cs::CSTeleportEndedPacket);
    reg.register::<CSUsePortalPacket>(LEVEL_PLAIN, cs::CSUsePortalPacket);
    reg.register::<CSDeletePortalPacket>(LEVEL_PLAIN, cs::CSDeletePortalPacket);
    reg.register::<CSNaviTeleportPacket>(LEVEL_PLAIN, cs::CSNaviTeleportPacket);
    reg.register::<CSNaviOpenPortalPacket>(LEVEL_PLAIN, cs::CSNaviOpenPortalPacket);
}

// ------------------------------------------------------------------------------------------
// S -> C
// ------------------------------------------------------------------------------------------

/// `TeleportReason` (values used here).
pub mod teleport_reason {
    pub const PORTAL: u8 = 0;
}

/// `SCTeleportUnitPacket`, `SCLoadInstancePacket`, `SCUnitDeathPacket`: the skills subsystem's
/// definitions (one per opcode).
pub use crate::systems::skills::packets::SCUnitDeathPacket;
pub use crate::systems::skills::packets_special::{SCLoadInstancePacket, SCTeleportUnitPacket};

/// `SCPortalDeletedPacket(portalType, portalId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPortalDeletedPacket {
    pub portal_type: u8,
    pub portal_id: i32,
}
packet_def!(
    SCPortalDeletedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPortalDeletedPacket
);
impl Marshal for SCPortalDeletedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.portal_type).write_i32(self.portal_id);
    }
}

/// `SCPortalInfoSavedPacket(portal)` (`Portal.Write`).
#[derive(Debug, Clone, PartialEq)]
pub struct SCPortalInfoSavedPacket {
    pub portal: PortalWire,
}
packet_def!(
    SCPortalInfoSavedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPortalInfoSavedPacket
);
impl Marshal for SCPortalInfoSavedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.portal.write(s);
    }
}

/// `SCUnitPortalUsedPacket(unitId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitPortalUsedPacket {
    pub unit_id: u32,
}
packet_def!(
    SCUnitPortalUsedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitPortalUsedPacket
);
impl Marshal for SCUnitPortalUsedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_id);
    }
}

/// `SCNaviTeleportPacket(x, y, z)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCNaviTeleportPacket {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}
packet_def!(
    SCNaviTeleportPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCNaviTeleportPacket
);
impl Marshal for SCNaviTeleportPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(helpers::convert_coord_to_long(self.x))
            .write_i64(helpers::convert_coord_to_long(self.y))
            .write_f32(self.z);
    }
}

/// `SCAboxTeleportPacket(x, y)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAboxTeleportPacket {
    pub x: i32,
    pub y: i32,
}
packet_def!(
    SCAboxTeleportPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAboxTeleportPacket
);
impl Marshal for SCAboxTeleportPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.x).write_i32(self.y);
    }
}

/// `KillReason.PortalTimeout`.
pub const KILL_REASON_PORTAL_TIMEOUT: crate::systems::skills::types::KillReason =
    crate::systems::skills::types::KillReason::PortalTimeout;

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        let frame = encode(p);
        // [dd 01][hash][count][u16 opcode][body]
        assert_eq!(&frame[..2], &[0xdd, 0x01]);
        assert_eq!(u16::from_le_bytes([frame[4], frame[5]]), p.opcode());
        frame[6..].to_vec()
    }

    fn decode<P: ClientPacket>(bytes: &[u8]) -> P {
        let mut s = PacketStream::from_bytes(bytes.to_vec());
        P::read(&mut s).unwrap()
    }

    #[test]
    fn c2s_decode() {
        let p: CSUsePortalPacket = decode(&[0x01, 0x02, 0x03, 0x01]);
        assert_eq!(
            p,
            CSUsePortalPacket {
                obj_id: 0x030201,
                only_my_portal: true
            }
        );
        let p: CSDeletePortalPacket = decode(&[0x02, 0x01, 0x10, 0x00, 0x00]);
        assert_eq!(
            p,
            CSDeletePortalPacket {
                portal_type: 2,
                portal_id: 0x1001
            }
        );
        let p: CSUseTeleportPacket =
            decode(&[0x05, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 0x10, 0x20, 0x30]);
        assert_eq!(
            p,
            CSUseTeleportPacket {
                return_point_id: 5,
                money_amount: -1,
                npc_unit_id: 0x302010
            }
        );
        let p: CSNaviOpenPortalPacket = decode(&[7, 0, 0]);
        assert_eq!(p.obj_id, 7);
        let p: CSNaviTeleportPacket = decode(&[8, 0, 0]);
        assert_eq!(p.obj_id, 8);
        let mut s = PacketStream::new();
        s.write_i64(helpers::convert_coord_to_long(1000.5))
            .write_i64(helpers::convert_coord_to_long(2000.25))
            .write_f32(100.0);
        let ori = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x3f];
        s.write_raw(&ori);
        let p: CSTeleportEndedPacket = decode(s.as_bytes());
        assert_eq!(
            p,
            CSTeleportEndedPacket {
                x: 1000.5,
                y: 2000.25,
                z: 100.0,
                ori
            }
        );
    }

    #[test]
    fn s2c_byte_exact() {
        let mut e = vec![0u8];
        e.extend_from_slice(&0i16.to_le_bytes());
        e.extend_from_slice(&helpers::encode_position(1000.0, 2000.0, 100.0));
        e.extend_from_slice(&1.5f32.to_le_bytes());
        assert_eq!(
            body(&SCTeleportUnitPacket {
                reason: 0,
                error_message: 0,
                x: 1000.0,
                y: 2000.0,
                z: 100.0,
                z2: 1.5
            }),
            e
        );

        let mut e = Vec::new();
        for v in [3u32, 129] {
            e.extend_from_slice(&v.to_le_bytes());
        }
        for f in [1.0f32, 2.0, 3.0, 0.1, 0.2, 0.3] {
            e.extend_from_slice(&f.to_le_bytes());
        }
        assert_eq!(
            body(&SCLoadInstancePacket {
                instance_id: 3,
                zone_id: 129,
                x: 1.0,
                y: 2.0,
                z: 3.0,
                ang_x: 0.1,
                ang_y: 0.2,
                ang_z: 0.3
            }),
            e
        );

        assert_eq!(
            body(&SCPortalDeletedPacket {
                portal_type: 1,
                portal_id: 0x1234
            }),
            vec![1, 0x34, 0x12, 0, 0]
        );

        let mut e = Vec::new();
        e.extend_from_slice(&4097u32.to_le_bytes());
        e.extend_from_slice(&3u16.to_le_bytes());
        e.extend_from_slice(b"abc");
        e.extend_from_slice(&129u32.to_le_bytes());
        for f in [10.0f32, 20.0, 30.0, 45.0] {
            e.extend_from_slice(&f.to_le_bytes());
        }
        assert_eq!(
            body(&SCPortalInfoSavedPacket {
                portal: PortalWire {
                    id: 4097,
                    name: "abc".into(),
                    zone_id: 129,
                    off_x: 10.0,
                    off_y: 20.0,
                    z: 30.0,
                    z_rot: 45.0
                }
            }),
            e
        );

        assert_eq!(
            body(&SCUnitPortalUsedPacket { unit_id: 0x0a0b0c }),
            vec![0x0c, 0x0b, 0x0a]
        );

        let mut e = Vec::new();
        e.extend_from_slice(&((1000.5f32 * 4096.0) as i64).wrapping_shl(32).to_le_bytes());
        e.extend_from_slice(&((2000.0f32 * 4096.0) as i64).wrapping_shl(32).to_le_bytes());
        e.extend_from_slice(&50.0f32.to_le_bytes());
        assert_eq!(
            body(&SCNaviTeleportPacket {
                x: 1000.5,
                y: 2000.0,
                z: 50.0
            }),
            e
        );

        assert_eq!(
            body(&SCAboxTeleportPacket { x: -1, y: 2 }),
            vec![0xff, 0xff, 0xff, 0xff, 2, 0, 0, 0]
        );

        assert_eq!(
            body(&SCUnitDeathPacket {
                obj_id: 0x010203,
                kill_reason: KILL_REASON_PORTAL_TIMEOUT,
                resurrect_wait_time: 0,
                lost_exp: 0,
                durability_loss_rate: 0,
                killer: None,
            }),
            vec![3, 2, 1, 0x0e, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        );
    }
}
