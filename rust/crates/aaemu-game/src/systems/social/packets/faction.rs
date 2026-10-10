//! Faction packets (`SCFactionListPacket` / `SCFactionRelationListPacket` live in
//! `packets::world`, used by the select path).

use super::{cs_packet, sc_packet};
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;

sc_packet!(
    /// `SCUnitFactionChangedPacket(unitId, unitName, id, id2, temp)`.
    SCUnitFactionChangedPacket { obj_id: u32, unit_name: String, id: u32, id2: u32, temp: bool } |self, s| {
        s.write_bc(self.obj_id)
            .write_str(&self.unit_name)
            .write_u32(self.id)
            .write_u32(self.id2)
            .write_bool(self.temp);
    }
);

cs_packet!(
    /// `CSFactionImmigrationInvitePacket` (logged only, as in C#).
    CSFactionImmigrationInvitePacket { invitee: String }
    read |s| { Ok(Self { invitee: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("FactionImmigrationInvite, {}", self.invitee);
        Ok(())
    }
);

cs_packet!(
    /// `CSFactionImmigrationInviteReplyPacket` (logged only, as in C#).
    CSFactionImmigrationInviteReplyPacket { unk_id: u32, unk2_id: u32, answer: bool }
    read |s| { Ok(Self { unk_id: s.read_u32()?, unk2_id: s.read_u32()?, answer: s.read_bool()? }) }
    handle |self, server, conn| {
        tracing::debug!("FactionImmigrationInviteReply, Id: {}, Id2: {}, Answer: {}", self.unk_id, self.unk2_id, self.answer);
        Ok(())
    }
);

cs_packet!(
    /// `CSFactionImmigrateToOriginPacket` (empty struct, logged only).
    CSFactionImmigrateToOriginPacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        tracing::debug!("FactionImmigrateToOrigin");
        Ok(())
    }
);

cs_packet!(
    /// `CSFactionKickToOriginPacket` (logged only, as in C#).
    CSFactionKickToOriginPacket { name: String }
    read |s| { Ok(Self { name: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("FactionKickToOrigin, Name: {}", self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSFactionDeclareHostilePacket` (logged only, as in C#).
    CSFactionDeclareHostilePacket { id: u32 }
    read |s| { Ok(Self { id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("FactionDeclareHostile, Id: {}", self.id);
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSFactionImmigrationInvitePacket>(
        LEVEL_PLAIN,
        cs::CSFactionImmigrationInvitePacket,
    );
    reg.register::<CSFactionImmigrationInviteReplyPacket>(
        LEVEL_PLAIN,
        cs::CSFactionImmigrationInviteReplyPacket,
    );
    reg.register::<CSFactionImmigrateToOriginPacket>(
        LEVEL_PLAIN,
        cs::CSFactionImmigrateToOriginPacket,
    );
    reg.register::<CSFactionKickToOriginPacket>(LEVEL_PLAIN, cs::CSFactionKickToOriginPacket);
    reg.register::<CSFactionDeclareHostilePacket>(LEVEL_PLAIN, cs::CSFactionDeclareHostilePacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::social::packets::body;

    #[test]
    fn unit_faction_changed_is_byte_exact() {
        assert_eq!(
            body(&SCUnitFactionChangedPacket {
                obj_id: 0x10,
                unit_name: "A".into(),
                id: 101,
                id2: 161,
                temp: false
            }),
            vec![0x10, 0, 0, 1, 0, b'A', 101, 0, 0, 0, 161, 0, 0, 0, 0]
        );
    }
}
