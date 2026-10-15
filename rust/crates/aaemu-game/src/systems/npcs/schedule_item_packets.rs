//! Schedule-item (event timer bar) client packet: the take.
//!
//! `CSTakeScheduleItemPacket` (0x13b in 1.2): the HUD timer button's `TakeScheduleItem(type)`.
//! The opcode and the single `u32` body (the schedule row id, `schedule_items.id`) are read off
//! the 1.2 client (`x2game.dll`: the Lua binding validates the row, then builds a packet object
//! with opcode field `0x13b` and the id at `+0xc`); the 5.0 C# `CSTakeScheduleItemPacket` has the
//! same `ReadUInt32` body. The answer is `SCScheduleItemSentPacket` (0x151).

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::cs;
use crate::server::GameServer;

use super::schedule_item_session;

/// `CSTakeScheduleItemPacket(uint scheduleType)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSTakeScheduleItemPacket {
    pub schedule_type: u32,
}

impl ClientPacket for CSTakeScheduleItemPacket {
    const NAME: &'static str = "CSTakeScheduleItemPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            schedule_type: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("TakeScheduleItem, ScheduleType: {}", self.schedule_type);
            // Outside the world there is no character to pay.
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            let Ok(schedule_id) = i32::try_from(self.schedule_type) else {
                return Ok(());
            };
            let outcome = schedule_item_session::take(server, &me, schedule_id);
            tracing::debug!("TakeScheduleItem {schedule_id}: {outcome:?}");
            Ok(())
        })
    }
}

/// Registers the schedule-item C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSTakeScheduleItemPacket>(LEVEL_PLAIN, cs::CSTakeScheduleItemPacket);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_schedule_type() {
        let mut s = PacketStream::from_bytes(vec![0x1c, 0x00, 0x00, 0x00]);
        assert_eq!(
            CSTakeScheduleItemPacket::read(&mut s).unwrap(),
            CSTakeScheduleItemPacket { schedule_type: 28 }
        );
        let mut short = PacketStream::from_bytes(vec![0x1c, 0x00]);
        assert!(CSTakeScheduleItemPacket::read(&mut short).is_err());
    }
}
