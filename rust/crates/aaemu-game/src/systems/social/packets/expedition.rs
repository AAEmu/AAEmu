//! Expedition (guild) packets.

#[cfg(test)]
use aaemu_commons::PacketStream;

use super::{active_char, cs_packet, sc_packet};
use crate::data::world_data::SystemFaction;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::packets::world::write_system_faction;
use crate::systems::social::expedition::{self, ExpeditionRolePolicy};

/// `ItemTaskType.ExpeditionCreation`.
pub const ITEM_TASK_EXPEDITION_CREATION: u8 = 73;
sc_packet!(
    /// `SCExpeditionDismissedPacket(id, success)`.
    SCExpeditionDismissedPacket { id: u32, success: bool } |self, s| {
        s.write_u32(self.id).write_bool(self.success);
    }
);

sc_packet!(
    /// `SCExpeditionInvitationPacket(invitorId, invitorName, factionId, factionName)`.
    SCExpeditionInvitationPacket {
        invitor_id: u32,
        invitor_name: String,
        faction_id: u32,
        faction_name: String,
    } |self, s| {
        s.write_u32(self.invitor_id)
            .write_str(&self.invitor_name)
            .write_u32(self.faction_id)
            .write_str(&self.faction_name);
    }
);

sc_packet!(
    /// `SCExpeditionOwnerChangedPacket(id, id2, charName)`.
    SCExpeditionOwnerChangedPacket { id: u32, id2: u32, char_name: String } |self, s| {
        s.write_u32(self.id).write_u32(self.id2).write_str(&self.char_name);
    }
);

sc_packet!(
    /// `SCExpeditionRoleChangedPacket(id, role, charName)`.
    SCExpeditionRoleChangedPacket { id: u32, role: u8, char_name: String } |self, s| {
        s.write_u32(self.id).write_u8(self.role).write_str(&self.char_name);
    }
);

sc_packet!(
    /// `SCExpeditionRolePolicyChangedPacket(rolePolicy, success)`.
    SCExpeditionRolePolicyChangedPacket { policy: ExpeditionRolePolicy, success: bool } |self, s| {
        s.write(&self.policy).write_bool(self.success);
    }
);

sc_packet!(
    /// `SCExpeditionShowRenameUIPacket()` (empty body; never sent by the 1.2 C#).
    SCExpeditionShowRenameUIPacket {} |self, s| {}
);

sc_packet!(
    /// `SCExpeditionSponsorChangedPacket(faction, success)` (never sent by the 1.2 C#).
    SCExpeditionSponsorChangedPacket { faction: SystemFaction, success: bool } |self, s| {
        write_system_faction(&self.faction, s);
        s.write_bool(self.success);
    }
);

sc_packet!(
    /// `SCFactionCreatedPacket(faction, ownerObjId, members)`.
    SCFactionCreatedPacket {
        faction: SystemFaction,
        owner_obj_id: u32,
        /// (member obj id, member id, name)
        members: Vec<(u32, u32, String)>,
    } |self, s| {
        write_system_faction(&self.faction, s);
        s.write_bc(self.owner_obj_id).write_u8(self.members.len() as u8);
        for (obj, id, name) in &self.members {
            s.write_bc(*obj).write_u32(*id).write_str(name);
        }
    }
);

sc_packet!(
    /// `SCUnitExpeditionChangedPacket(unitId, characterId, kicker, unitName, id, expeditionId,
    /// expel)`.
    SCUnitExpeditionChangedPacket {
        obj_id: u32,
        character_id: u32,
        kicker: String,
        unit_name: String,
        id: u32,
        expedition_id: u32,
        expel: bool,
    } |self, s| {
        s.write_bc(self.obj_id)
            .write_u32(self.character_id)
            .write_str(&self.kicker)
            .write_str(&self.unit_name)
            .write_u32(self.id)
            .write_u32(self.expedition_id)
            .write_bool(self.expel);
    }
);

/// `SCItemTaskSuccessPacket(action, [new MoneyChange(amount)], [])`: the money-only form
/// `CreateExpedition` sends, built on the economy subsystem's definition (one per opcode).
pub fn item_task_money_packet(
    action: u8,
    money_change: i32,
) -> crate::systems::economy::item_packets::SCItemTaskSuccessPacket {
    crate::systems::economy::item_packets::SCItemTaskSuccessPacket {
        action,
        tasks: vec![crate::models::item_task::ItemTask::MoneyChange(
            money_change,
        )],
        force_remove: Vec::new(),
    }
}

cs_packet!(
    /// `CSCreateExpeditionPacket`.
    CSCreateExpeditionPacket { name: String, id: u32 }
    read |s| { Ok(Self { name: s.read_string()?, id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("CreateExpedition, name: {}, id: {}", self.name, self.id);
        let p = active_char!(conn);
        expedition::create_expedition(server, &p, &self.name).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeExpeditionRolePolicyPacket`.
    CSChangeExpeditionRolePolicyPacket { policy: ExpeditionRolePolicy }
    read |s| { Ok(Self { policy: ExpeditionRolePolicy::read(s)? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeExpeditionRolePolicy, Id: {}, Role: {}", self.policy.expedition_id, self.policy.role);
        let p = active_char!(conn);
        expedition::change_role_policy(server, &p, self.policy).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeExpeditionMemberRolePacket`.
    CSChangeExpeditionMemberRolePacket { role: u8, id: u32 }
    read |s| { Ok(Self { role: s.read_u8()?, id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeExpeditionMemberRole, Id: {}, Role: {}", self.id, self.role);
        let p = active_char!(conn);
        expedition::change_member_role(server, &p, self.role, self.id).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeExpeditionOwnerPacket`.
    CSChangeExpeditionOwnerPacket { id: u32 }
    read |s| { Ok(Self { id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeExpeditionOwner, Id: {}", self.id);
        let p = active_char!(conn);
        expedition::change_owner(server, &p, self.id).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSRenameExpeditionPacket` (logged only, as in C#).
    CSRenameExpeditionPacket { id: u32, name: String, is_expedition: bool }
    read |s| { Ok(Self { id: s.read_u32()?, name: s.read_string()?, is_expedition: s.read_bool()? }) }
    handle |self, server, conn| {
        tracing::debug!("RenameExpedition, Id: {}, Name: {}, IsExpedition: {}", self.id, self.name, self.is_expedition);
        Ok(())
    }
);

cs_packet!(
    /// `CSDismissExpeditionPacket` (empty struct).
    CSDismissExpeditionPacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        tracing::debug!("DismissExpedition");
        let p = active_char!(conn);
        expedition::disband(server, &p).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSInviteToExpeditionPacket`.
    CSInviteToExpeditionPacket { name: String }
    read |s| { Ok(Self { name: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("InviteToExpedition, Name: {}", self.name);
        let p = active_char!(conn);
        expedition::invite(server, &p, &self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSReplyExpeditionInvitationPacket`.
    CSReplyExpeditionInvitationPacket { id: u32, id2: u32, join: bool }
    read |s| { Ok(Self { id: s.read_u32()?, id2: s.read_u32()?, join: s.read_bool()? }) }
    handle |self, server, conn| {
        tracing::debug!("ReplyExpeditionInvitation, Id: {}, Id2: {}, Join: {}", self.id, self.id2, self.join);
        let p = active_char!(conn);
        expedition::reply_invite(server, &p, self.id, self.id2, self.join).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSLeaveExpeditionPacket`.
    CSLeaveExpeditionPacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        tracing::debug!("LeaveExpedition");
        let p = active_char!(conn);
        expedition::leave(server, &p).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSKickFromExpeditionPacket`.
    CSKickFromExpeditionPacket { id: u32 }
    read |s| { Ok(Self { id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::debug!("KickFromExpedition, Id: {}", self.id);
        let p = active_char!(conn);
        expedition::kick(server, &p, self.id).await;
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSCreateExpeditionPacket>(LEVEL_PLAIN, cs::CSCreateExpeditionPacket);
    // CSChangeExpeditionSponsorPacket has no 1.2 opcode (0xfff).
    reg.register::<CSChangeExpeditionRolePolicyPacket>(
        LEVEL_PLAIN,
        cs::CSChangeExpeditionRolePolicyPacket,
    );
    reg.register::<CSChangeExpeditionMemberRolePacket>(
        LEVEL_PLAIN,
        cs::CSChangeExpeditionMemberRolePacket,
    );
    reg.register::<CSChangeExpeditionOwnerPacket>(LEVEL_PLAIN, cs::CSChangeExpeditionOwnerPacket);
    reg.register::<CSRenameExpeditionPacket>(LEVEL_PLAIN, cs::CSRenameExpeditionPacket);
    reg.register::<CSDismissExpeditionPacket>(LEVEL_PLAIN, cs::CSDismissExpeditionPacket);
    reg.register::<CSInviteToExpeditionPacket>(LEVEL_PLAIN, cs::CSInviteToExpeditionPacket);
    reg.register::<CSReplyExpeditionInvitationPacket>(
        LEVEL_PLAIN,
        cs::CSReplyExpeditionInvitationPacket,
    );
    reg.register::<CSLeaveExpeditionPacket>(LEVEL_PLAIN, cs::CSLeaveExpeditionPacket);
    reg.register::<CSKickFromExpeditionPacket>(LEVEL_PLAIN, cs::CSKickFromExpeditionPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::social::packets::body;

    #[test]
    fn expedition_packets_are_byte_exact() {
        let mut s = PacketStream::new();
        s.write_bc(0x123456)
            .write_u32(7)
            .write_str("K")
            .write_str("Bob")
            .write_u32(5)
            .write_u32(0)
            .write_bool(true);
        assert_eq!(
            body(&SCUnitExpeditionChangedPacket {
                obj_id: 0x123456,
                character_id: 7,
                kicker: "K".into(),
                unit_name: "Bob".into(),
                id: 5,
                expedition_id: 0,
                expel: true,
            }),
            s.into_bytes()
        );
        assert_eq!(
            body(&item_task_money_packet(
                ITEM_TASK_EXPEDITION_CREATION,
                -10000
            )),
            vec![73, 1, 1, 0xF0, 0xD8, 0xFF, 0xFF, 0, 0, 0, 0, 0]
        );
        let f = SystemFaction {
            id: 9,
            name: "G".into(),
            mother_id: 101,
            owner_id: 3,
            owner_name: "O".into(),
            political_system: 1,
            ..SystemFaction::default()
        };
        let mut s = PacketStream::new();
        write_system_faction(&f, &mut s);
        s.write_bc(0x10)
            .write_u8(1)
            .write_bc(0x10)
            .write_u32(3)
            .write_str("O");
        assert_eq!(
            body(&SCFactionCreatedPacket {
                faction: f,
                owner_obj_id: 0x10,
                members: vec![(0x10, 3, "O".into())],
            }),
            s.into_bytes()
        );
        assert_eq!(
            body(&SCExpeditionRoleChangedPacket {
                id: 1,
                role: 255,
                char_name: "A".into()
            }),
            vec![1, 0, 0, 0, 255, 1, 0, b'A']
        );
        assert_eq!(
            body(&SCExpeditionOwnerChangedPacket {
                id: 1,
                id2: 2,
                char_name: "".into()
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCExpeditionDismissedPacket {
                id: 2,
                success: true
            }),
            vec![2, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCExpeditionInvitationPacket {
                invitor_id: 1,
                invitor_name: "a".into(),
                faction_id: 2,
                faction_name: "b".into()
            }),
            vec![1, 0, 0, 0, 1, 0, b'a', 2, 0, 0, 0, 1, 0, b'b']
        );
        assert!(body(&SCExpeditionShowRenameUIPacket {}).is_empty());
    }

    #[test]
    fn role_policy_round_trip() {
        let p = ExpeditionRolePolicy {
            expedition_id: 4,
            role: 2,
            name: "Officer".into(),
            dominion_declare: true,
            invite: true,
            expel: false,
            promote: false,
            dismiss: false,
            chat: true,
            manager_chat: false,
            siege_master: false,
            join_siege: true,
        };
        let bytes = body(&SCExpeditionRolePolicyChangedPacket {
            policy: p.clone(),
            success: true,
        });
        let mut s = PacketStream::from_bytes(bytes.clone());
        let read = ExpeditionRolePolicy::read(&mut s).unwrap();
        assert_eq!(read, p);
        assert!(s.read_bool().unwrap());
        assert_eq!(bytes.len(), 4 + 1 + 2 + 7 + 9 + 1);
    }
}
