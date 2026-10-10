//! C->G packets of `Core/Packets/C2G` whose C# `Read` only logs: `CSEditCharacterPacket`,
//! `CSTurretStatePacket`, and the log-only packets the 1.2 C# defines but never registers (opcode
//! 0xfff, commented out in `GameNetwork`): `CSResetQuestContextPacket`,
//! `CSAcceptCheatQuestContextPacket`, `CSSearchListPacket`, `CSChangeExpeditionSponsorPacket`,
//! `CSChangeSlaveTargetPacket`, `CSSetTeamOfficerPacket`, `CSUpdateNationalTaxRatePacket`.

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::cs;
use crate::server::GameServer;

/// Declares a C->S packet whose `Read` decodes the listed fields and only logs them.
macro_rules! log_only_packet {
    ($(#[$m:meta])* $ty:ident, $level:ident, $fmt:literal { $($field:ident : $fty:ty => $r:ident),* $(,)? }) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $ty {
            $(pub $field: $fty,)*
        }
        impl ClientPacket for $ty {
            const NAME: &'static str = stringify!($ty);

            #[allow(unused_variables)]
            fn read(s: &mut PacketStream) -> Result<Self> {
                Ok(Self { $($field: s.$r()?,)* })
            }

            fn handle<'a>(
                self,
                _server: &'a GameServer,
                _conn: &'a mut GameConnection,
            ) -> BoxFuture<'a, Result<()>> {
                tracing::$level!($fmt, $(self.$field),*);
                Box::pin(async { Ok(()) })
            }
        }
    };
}

log_only_packet!(
    /// `CSEditCharacterPacket` (0x023): `// TODO ... create`, the body is not read.
    CSEditCharacterPacket, error, "CSEditCharacterPacket is not implemented!" {}
);

log_only_packet!(
    /// `CSTurretStatePacket` (0x036).
    CSTurretStatePacket, debug, "TurretState, UnitId: {}, Pitch: {}, Yaw: {}" {
        unit_id: u32 => read_bc,
        pitch: f32 => read_f32,
        yaw: f32 => read_f32,
    }
);

log_only_packet!(
    /// `CSResetQuestContextPacket` (not registered in 1.2).
    CSResetQuestContextPacket, debug, "ResetQuestContext, Id: {}" { quest_id: u32 => read_u32 }
);

log_only_packet!(
    /// `CSAcceptCheatQuestContextPacket` (not registered in 1.2).
    CSAcceptCheatQuestContextPacket, debug, "AcceptCheatQuestContext, Id: {}" {
        quest_id: u32 => read_u32,
    }
);

log_only_packet!(
    /// `CSSearchListPacket` (not registered in 1.2).
    CSSearchListPacket, debug, "SearchList, ZoneType: {}, OnlineType: {}" {
        zone_type: u8 => read_u8,
        online_type: u8 => read_u8,
    }
);

log_only_packet!(
    /// `CSChangeExpeditionSponsorPacket` (not registered in 1.2).
    CSChangeExpeditionSponsorPacket, debug, "ChangeExpeditionSponsor, Id: {}, Id2: {}" {
        unk_id: u32 => read_u32,
        unk2_id: u32 => read_u32,
    }
);

log_only_packet!(
    /// `CSChangeSlaveTargetPacket` (not registered in 1.2).
    CSChangeSlaveTargetPacket, debug, "ChangeSlaveTarget, Target: {}, Slave: {}" {
        target_id: u32 => read_bc,
        slave_id: u32 => read_bc,
    }
);

log_only_packet!(
    /// `CSSetTeamOfficerPacket` (not registered in 1.2).
    CSSetTeamOfficerPacket, warn, "SetTeamOfficer, TeamId: {}, MemberId: {}, Promote: {}" {
        team_id: u32 => read_u32,
        member_id: u32 => read_u32,
        promote: bool => read_bool,
    }
);

log_only_packet!(
    /// `CSUpdateNationalTaxRatePacket` (not registered in 1.2).
    CSUpdateNationalTaxRatePacket, debug, "UpdateNationalTaxRate, Id: {}, TaxRate: {}" {
        id: u16 => read_u16,
        tax_rate: i32 => read_i32,
    }
);

/// Registers the packets of this module that the 1.2 `GameNetwork` registers.
pub fn register(r: &mut PacketRegistry) {
    r.register::<CSEditCharacterPacket>(LEVEL_PLAIN, cs::CSEditCharacterPacket)
        .register::<CSTurretStatePacket>(LEVEL_PLAIN, cs::CSTurretStatePacket);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn stream(f: impl FnOnce(&mut PacketStream)) -> PacketStream {
        let mut s = PacketStream::new();
        f(&mut s);
        PacketStream::from_bytes(s.into_bytes())
    }

    #[test]
    fn reads_match_csharp() {
        let mut s = stream(|s| {
            s.write_bc(0x010203).write_f32(1.5).write_f32(-2.0);
        });
        assert_eq!(
            CSTurretStatePacket::read(&mut s).unwrap(),
            CSTurretStatePacket {
                unit_id: 0x010203,
                pitch: 1.5,
                yaw: -2.0
            }
        );
        let mut s = stream(|s| {
            s.write_u32(77);
        });
        assert_eq!(
            CSResetQuestContextPacket::read(&mut s).unwrap().quest_id,
            77
        );
        let mut s = stream(|s| {
            s.write_u32(78);
        });
        assert_eq!(
            CSAcceptCheatQuestContextPacket::read(&mut s)
                .unwrap()
                .quest_id,
            78
        );
        let mut s = stream(|s| {
            s.write_u8(1).write_u8(2);
        });
        assert_eq!(
            CSSearchListPacket::read(&mut s).unwrap(),
            CSSearchListPacket {
                zone_type: 1,
                online_type: 2
            }
        );
        let mut s = stream(|s| {
            s.write_bc(5).write_bc(6);
        });
        assert_eq!(
            CSChangeSlaveTargetPacket::read(&mut s).unwrap(),
            CSChangeSlaveTargetPacket {
                target_id: 5,
                slave_id: 6
            }
        );
        let mut s = stream(|s| {
            s.write_u32(1).write_u32(2).write_bool(true);
        });
        assert_eq!(
            CSSetTeamOfficerPacket::read(&mut s).unwrap(),
            CSSetTeamOfficerPacket {
                team_id: 1,
                member_id: 2,
                promote: true
            }
        );
        let mut s = stream(|s| {
            s.write_u16(3).write_i32(-4);
        });
        assert_eq!(
            CSUpdateNationalTaxRatePacket::read(&mut s).unwrap(),
            CSUpdateNationalTaxRatePacket {
                id: 3,
                tax_rate: -4
            }
        );
        let mut s = stream(|s| {
            s.write_u32(7).write_u32(8);
        });
        assert_eq!(
            CSChangeExpeditionSponsorPacket::read(&mut s).unwrap(),
            CSChangeExpeditionSponsorPacket {
                unk_id: 7,
                unk2_id: 8
            }
        );
        // CSEditCharacterPacket reads nothing.
        let mut s = stream(|s| {
            s.write_u8(9);
        });
        assert_eq!(
            CSEditCharacterPacket::read(&mut s).unwrap(),
            CSEditCharacterPacket {}
        );
    }
}
