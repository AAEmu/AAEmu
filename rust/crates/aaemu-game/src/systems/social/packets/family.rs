//! Family packets.

use super::{active_char, cs_packet, sc_packet};
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::systems::social::family::{self, FamilyInfo};

sc_packet!(
    /// `SCFamilyCreatedPacket(family)`.
    SCFamilyCreatedPacket { family: FamilyInfo } |self, s| {
        self.family.write(s);
    }
);

sc_packet!(
    /// `SCFamilyDescPacket(family)`.
    SCFamilyDescPacket { family: FamilyInfo } |self, s| {
        self.family.write(s);
    }
);

sc_packet!(
    /// `SCFamilyInvitationPacket(invitorId, invitorName, familyId, role)`.
    SCFamilyInvitationPacket { invitor_id: u32, invitor_name: String, family_id: u32, role: String } |self, s| {
        s.write_u32(self.invitor_id)
            .write_str(&self.invitor_name)
            .write_u32(self.family_id)
            .write_str(&self.role);
    }
);

sc_packet!(
    /// `SCFamilyMemberAddedPacket(family, addedIndex)`.
    SCFamilyMemberAddedPacket { family: FamilyInfo, added_index: i32 } |self, s| {
        self.family.write(s);
        s.write_i32(self.added_index);
    }
);

sc_packet!(
    /// `SCFamilyMemberNameChangedPacket(familyId, charId, newName)` (never sent by the 1.2 C#).
    SCFamilyMemberNameChangedPacket { family_id: u32, char_id: u32, new_name: String } |self, s| {
        s.write_u32(self.family_id)
            .write_u32(self.char_id)
            .write_str(&self.new_name);
    }
);

sc_packet!(
    /// `SCFamilyMemberOnlinePacket(familyId, memberId, online)`.
    SCFamilyMemberOnlinePacket { family_id: u32, member_id: u32, online: bool } |self, s| {
        s.write_u32(self.family_id)
            .write_u32(self.member_id)
            .write_bool(self.online);
    }
);

sc_packet!(
    /// `SCFamilyMemberRemovedPacket(familyId, kicked, memberId)` (writes the member id before
    /// the kicked flag).
    SCFamilyMemberRemovedPacket { family_id: u32, kicked: bool, member_id: u32 } |self, s| {
        s.write_u32(self.family_id)
            .write_u32(self.member_id)
            .write_bool(self.kicked);
    }
);

sc_packet!(
    /// `SCFamilyOwnerChangedPacket(familyId, memberId)`.
    SCFamilyOwnerChangedPacket { family_id: u32, member_id: u32 } |self, s| {
        s.write_u32(self.family_id).write_u32(self.member_id);
    }
);

sc_packet!(
    /// `SCFamilyRemovedPacket(familyId)`.
    SCFamilyRemovedPacket { family_id: u32 } |self, s| {
        s.write_u32(self.family_id);
    }
);

sc_packet!(
    /// `SCFamilyTitleChangedPacket(familyId, memberId, title)`.
    SCFamilyTitleChangedPacket { family_id: u32, member_id: u32, title: String } |self, s| {
        s.write_u32(self.family_id)
            .write_u32(self.member_id)
            .write_str(&self.title);
    }
);

sc_packet!(
    /// `SCFamilyTitlePacket(unitId, role, owner, title)` (never sent by the 1.2 C#).
    SCFamilyTitlePacket { unit_id: u32, role: u8, owner: String, title: String } |self, s| {
        s.write_bc(self.unit_id)
            .write_u8(self.role)
            .write_str(&self.owner)
            .write_str(&self.title);
    }
);

cs_packet!(
    /// `CSFamilyInviteMemberPacket`.
    CSFamilyInviteMemberPacket { name: String, title: String }
    read |s| { Ok(Self { name: s.read_string()?, title: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("FamilyInviteMember, Name: {}, Title: {}", self.name, self.title);
        let p = active_char!(conn);
        family::invite_to_family(server, &p, &self.name, &self.title);
        Ok(())
    }
);

cs_packet!(
    /// `CSFamilyReplyInvitationPacket`.
    CSFamilyReplyInvitationPacket { invitor_id: u32, join: bool, role: String }
    read |s| { Ok(Self { invitor_id: s.read_u32()?, join: s.read_bool()?, role: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("FamilyReplyInvitation, invitorId: {}, join: {}, role: {}", self.invitor_id, self.join, self.role);
        let p = active_char!(conn);
        family::reply_to_invite(server, self.invitor_id, &p, self.join, &self.role).await;
        Ok(())
    }
);

cs_packet!(
    /// `CSFamilyLeavePacket`.
    CSFamilyLeavePacket {}
    read |s| { Ok(Self {}) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        if p.data().family > 0 {
            family::leave_family(server, &p).await;
        }
        tracing::debug!("FamilyLeave");
        Ok(())
    }
);

cs_packet!(
    /// `CSFamilyKickPacket`.
    CSFamilyKickPacket { member_id: u32 }
    read |s| { Ok(Self { member_id: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        family::kick_member(server, &p, self.member_id).await;
        tracing::debug!("FamilyKick, memberId: {}", self.member_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSFamilyChangeTitlePacket`.
    CSFamilyChangeTitlePacket { member_id: u32, title: String }
    read |s| { Ok(Self { member_id: s.read_u32()?, title: s.read_string()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        family::change_title_and_save(server, &p, self.member_id, &self.title).await;
        tracing::debug!("FamilyChangeTitle, memberId: {}, title: {}", self.member_id, self.title);
        Ok(())
    }
);

cs_packet!(
    /// `CSFamilyChangeOwnerPacket`.
    CSFamilyChangeOwnerPacket { id: u32 }
    read |s| { Ok(Self { id: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        family::change_owner_and_save(server, &p, self.id).await;
        tracing::debug!("FamilyChangeOwner, Id: {}", self.id);
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSFamilyInviteMemberPacket>(LEVEL_PLAIN, cs::CSFamilyInviteMemberPacket);
    reg.register::<CSFamilyReplyInvitationPacket>(LEVEL_PLAIN, cs::CSFamilyReplyInvitationPacket);
    reg.register::<CSFamilyLeavePacket>(LEVEL_PLAIN, cs::CSFamilyLeavePacket);
    reg.register::<CSFamilyKickPacket>(LEVEL_PLAIN, cs::CSFamilyKickPacket);
    reg.register::<CSFamilyChangeTitlePacket>(LEVEL_PLAIN, cs::CSFamilyChangeTitlePacket);
    reg.register::<CSFamilyChangeOwnerPacket>(LEVEL_PLAIN, cs::CSFamilyChangeOwnerPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::social::packets::body;

    fn fam() -> FamilyInfo {
        FamilyInfo {
            id: 9,
            members: vec![
                (1, "A".into(), 1, true, "".into()),
                (2, "B".into(), 0, false, "Son".into()),
            ],
        }
    }

    #[test]
    fn family_packets_are_byte_exact() {
        let f = vec![
            9, 0, 0, 0, 2, 0, 0, 0, // id, count
            1, 0, 0, 0, 1, 0, b'A', 1, 1, 0, 0, // member 1
            2, 0, 0, 0, 1, 0, b'B', 0, 0, 3, 0, b'S', b'o', b'n', // member 2
        ];
        assert_eq!(body(&SCFamilyCreatedPacket { family: fam() }), f);
        assert_eq!(body(&SCFamilyDescPacket { family: fam() }), f);
        let mut added = f.clone();
        added.extend([1, 0, 0, 0]);
        assert_eq!(
            body(&SCFamilyMemberAddedPacket {
                family: fam(),
                added_index: 1
            }),
            added
        );
        assert_eq!(
            body(&SCFamilyMemberRemovedPacket {
                family_id: 9,
                kicked: true,
                member_id: 2
            }),
            vec![9, 0, 0, 0, 2, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCFamilyMemberOnlinePacket {
                family_id: 9,
                member_id: 2,
                online: true
            }),
            vec![9, 0, 0, 0, 2, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCFamilyInvitationPacket {
                invitor_id: 1,
                invitor_name: "A".into(),
                family_id: 1,
                role: "x".into()
            }),
            vec![1, 0, 0, 0, 1, 0, b'A', 1, 0, 0, 0, 1, 0, b'x']
        );
        assert_eq!(
            body(&SCFamilyTitleChangedPacket {
                family_id: 9,
                member_id: 2,
                title: "T".into()
            }),
            vec![9, 0, 0, 0, 2, 0, 0, 0, 1, 0, b'T']
        );
        assert_eq!(
            body(&SCFamilyOwnerChangedPacket {
                family_id: 9,
                member_id: 2
            }),
            vec![9, 0, 0, 0, 2, 0, 0, 0]
        );
        assert_eq!(
            body(&SCFamilyRemovedPacket { family_id: 9 }),
            vec![9, 0, 0, 0]
        );
        assert_eq!(
            body(&SCFamilyTitlePacket {
                unit_id: 5,
                role: 1,
                owner: "O".into(),
                title: "T".into()
            }),
            vec![5, 0, 0, 1, 1, 0, b'O', 1, 0, b'T']
        );
        assert_eq!(
            body(&SCFamilyMemberNameChangedPacket {
                family_id: 9,
                char_id: 2,
                new_name: "N".into()
            }),
            vec![9, 0, 0, 0, 2, 0, 0, 0, 1, 0, b'N']
        );
    }
}
