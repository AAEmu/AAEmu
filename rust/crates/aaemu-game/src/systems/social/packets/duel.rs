//! Duel packets (`CSChallengeDuelPacket`, `CSStartDuelPacket` and the S->C packets the
//! `DuelManager` sends).

use super::{cs_packet, sc_packet};
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;

sc_packet!(
    /// `SCDuelChallengedPacket(challengerId)` (sent to the challenged character only; the C#
    /// parameter is misnamed `challengedId`).
    SCDuelChallengedPacket { challenger_id: u32 } |self, s| {
        s.write_u32(self.challenger_id);
    }
);

sc_packet!(
    /// `SCDuelEndedPacket(challengerId, challengedId, challengerObjId, challengedObjId, det)`
    /// (`det`: [`crate::systems::social::duel::DuelDetType`]).
    SCDuelEndedPacket {
        challenger_id: u32,
        challenged_id: u32,
        challenger_obj_id: u32,
        challenged_obj_id: u32,
        det: u8,
    } |self, s| {
        s.write_u32(self.challenger_id)
            .write_u32(self.challenged_id)
            .write_bc(self.challenger_obj_id)
            .write_bc(self.challenged_obj_id)
            .write_u8(self.det);
    }
);

sc_packet!(
    /// `SCDuelStartCountdownPacket()` (no body).
    SCDuelStartCountdownPacket {} |self, s| {}
);

sc_packet!(
    /// `SCDuelStartedPacket(challengerObjId, challengedObjId)`.
    SCDuelStartedPacket { challenger_obj_id: u32, challenged_obj_id: u32 } |self, s| {
        s.write_bc(self.challenger_obj_id).write_bc(self.challenged_obj_id);
    }
);

sc_packet!(
    /// `SCDuelStatePacket(challengerObjId, flagObjId)` (flag 0 = the duel ended).
    SCDuelStatePacket { challenger_obj_id: u32, flag_obj_id: u32 } |self, s| {
        s.write_bc(self.challenger_obj_id).write_bc(self.flag_obj_id);
    }
);

sc_packet!(
    /// `SCAreaChatBubblePacket(enter, unitObjId, type)`.
    SCAreaChatBubblePacket { enter: bool, unit_obj_id: u32, bubble_type: u32 } |self, s| {
        s.write_bool(self.enter)
            .write_bc(self.unit_obj_id)
            .write_u32(self.bubble_type);
    }
);

/// `SCCombatEngagedPacket` (skills) and `SCDoodadPhaseChangedPacket` (npcs): one definition per
/// opcode.
pub use crate::systems::npcs::doodad::packets::SCDoodadPhaseChangedPacket;
pub use crate::systems::skills::packets::SCCombatEngagedPacket;

cs_packet!(
    /// `CSChallengeDuelPacket`: the character id of the challenged player.
    CSChallengeDuelPacket { challenged_id: u32 }
    read |s| { Ok(Self { challenged_id: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = super::active_char!(conn);
        server.systems.social.duels.duel_request(server, &p, self.challenged_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSStartDuelPacket`: the challenger's character id and the answer (0 = accepted,
    /// 507 `TargetRejectedDuel` = refused).
    CSStartDuelPacket { challenger_id: u32, error_message: i16 }
    read |s| { Ok(Self { challenger_id: s.read_u32()?, error_message: s.read_i16()? }) }
    handle |self, server, conn| {
        tracing::warn!("StartDuel, Id: {}, ErrorMessage: {}", self.challenger_id, self.error_message);
        if self.error_message != 0 {
            server.systems.social.duels.duel_cancel(server, self.challenger_id, self.error_message);
            return Ok(());
        }
        // `DuelAccepted(Connection.ActiveChar, ..)` throws `ArgumentNullException` without a
        // character (caught by the network layer).
        let p = super::active_char!(conn);
        server.systems.social.duels.duel_accepted(server, &p, self.challenger_id).await;
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSChallengeDuelPacket>(LEVEL_PLAIN, cs::CSChallengeDuelPacket);
    reg.register::<CSStartDuelPacket>(LEVEL_PLAIN, cs::CSStartDuelPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::doodad::Doodad;
    use crate::net::registry::ClientPacket;
    use crate::systems::social::packets::body;
    use aaemu_commons::PacketStream;

    #[test]
    fn duel_packets_are_byte_exact() {
        assert_eq!(
            body(&SCDuelChallengedPacket {
                challenger_id: 0x01020304
            }),
            vec![4, 3, 2, 1]
        );
        assert_eq!(
            body(&SCDuelEndedPacket {
                challenger_id: 1,
                challenged_id: 2,
                challenger_obj_id: 0x030201,
                challenged_obj_id: 0x10,
                det: 3
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 1, 2, 3, 0x10, 0, 0, 3]
        );
        assert!(body(&SCDuelStartCountdownPacket {}).is_empty());
        assert_eq!(
            body(&SCDuelStartedPacket {
                challenger_obj_id: 0x11,
                challenged_obj_id: 0x222222
            }),
            vec![0x11, 0, 0, 0x22, 0x22, 0x22]
        );
        assert_eq!(
            body(&SCDuelStatePacket {
                challenger_obj_id: 0x11,
                flag_obj_id: 0
            }),
            vec![0x11, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCAreaChatBubblePacket {
                enter: true,
                unit_obj_id: 0x11,
                bubble_type: 543
            }),
            vec![1, 0x11, 0, 0, 0x1F, 0x02, 0, 0]
        );
        assert_eq!(
            body(&SCCombatEngagedPacket { obj_id: 0x0A0B0C }),
            vec![0x0C, 0x0B, 0x0A]
        );
        let mut d = Doodad::new(5014);
        d.func_group_id = 7;
        d.item_template_id = 27606;
        d.growth_time_ms = 10_500;
        let p = SCDoodadPhaseChangedPacket::new(0x33, &d, 10_000);
        assert_eq!(p.time_left, 500);
        let mut e = PacketStream::new();
        e.write_bc(0x33)
            .write_u32(7)
            .write_u32(500)
            .write_i32(-1)
            .write_u32(27606);
        assert_eq!(body(&p), e.into_bytes());
    }

    #[test]
    fn duel_requests_decode() {
        let mut s = PacketStream::from_bytes(vec![7, 0, 0, 0]);
        assert_eq!(
            CSChallengeDuelPacket::read(&mut s).unwrap(),
            CSChallengeDuelPacket { challenged_id: 7 }
        );
        let mut s = PacketStream::from_bytes(vec![9, 0, 0, 0, 0xFB, 0x01]);
        assert_eq!(
            CSStartDuelPacket::read(&mut s).unwrap(),
            CSStartDuelPacket {
                challenger_id: 9,
                error_message: 507
            }
        );
    }
}
