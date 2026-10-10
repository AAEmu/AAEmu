//! The S->C packet only the AI sends: `SCPlaySoundPacket` (NPC greeting voice).

use aaemu_commons::{Marshal, PacketStream};

use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `SCPlaySoundPacket(kind, bubbleId, npcObjId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPlaySoundPacket {
    pub kind: u8,
    pub bubble_id: u32,
    pub npc_obj_id: u32,
}
packet_def!(
    SCPlaySoundPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPlaySoundPacket
);
impl Marshal for SCPlaySoundPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind)
            .write_u32(self.bubble_id)
            .write_u32(self.npc_obj_id);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn play_sound_bytes() {
        let body = crate::systems::skills::packets::body_of(&SCPlaySoundPacket {
            kind: 2,
            bubble_id: 0x0102_0304,
            npc_obj_id: 0x00AB_CDEF,
        });
        assert_eq!(body, vec![2, 4, 3, 2, 1, 0xEF, 0xCD, 0xAB, 0x00]);
    }
}
