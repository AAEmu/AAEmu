//! Team (party / raid) packets.

use aaemu_commons::PacketStream;

use super::{active_char, cs_packet, sc_packet};
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::systems::social::team::LootingRule;

/// The character fields `TeamMember.Write` / `WritePerson` read (a snapshot).
#[derive(Debug, Clone, PartialEq)]
pub struct TeamMemberInfo {
    pub id: u32,
    pub name: String,
    pub race: u8,
    pub gender: u8,
    pub level: u8,
    pub role: u8,
    pub obj_id: u32,
    pub is_online: bool,
    pub zone_id: u32,
    pub instance_id: u32,
    pub hp: i32,
    pub max_hp: i32,
    pub mp: i32,
    pub max_mp: i32,
    /// `Transform.World.Position`.
    pub position: (f32, f32, f32),
    pub abilities: [u8; 3],
}

impl TeamMemberInfo {
    /// `TeamMember.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.gender)
            .write_u8(self.level)
            .write_u8(self.role)
            .write_bc(self.obj_id);
    }

    /// `TeamMember.WritePerson` (50 bytes).
    pub fn write_person(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.zone_id)
            .write_u32(self.instance_id)
            .write_u8(self.level)
            .write_i32(self.hp)
            .write_i32(self.max_hp)
            .write_i32(self.mp)
            .write_i32(self.max_mp)
            .write_position(self.position.0, self.position.1, self.position.2)
            .write_bc(if self.is_online { self.obj_id } else { 0 })
            .write_u8(0)
            .write_u8(self.abilities[0])
            .write_u8(self.abilities[1])
            .write_u8(self.abilities[2])
            .write_u8(0)
            .write_u8(0)
            .write_u8(0)
            .write_u8(0)
            .write_u8(0);
    }
}

/// What `Team.Write` reads.
#[derive(Debug, Clone, PartialEq)]
pub struct TeamInfo {
    pub id: u32,
    pub owner_id: u32,
    pub is_party: bool,
    pub party_counts: [u8; 10],
    /// The 50 slots: (character id or 0, online).
    pub slots: Vec<(u32, bool)>,
    pub marks: [(u8, u32); 12],
    pub looting_rule: LootingRule,
}

impl TeamInfo {
    /// `Team.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.owner_id)
            .write_bool(self.is_party);
        for c in self.party_counts {
            s.write_u8(c);
        }
        for &(id, online) in &self.slots {
            s.write_u32(id).write_bool(online);
        }
        for &(kind, obj) in &self.marks {
            s.write_u8(kind);
            if kind == 1 {
                s.write_u32(obj);
            } else if kind == 2 {
                s.write_bc(obj);
            }
        }
        self.looting_rule.write(s);
    }
}

sc_packet!(
    /// `SCAskToJoinTeamPacket(teamId, senderId, senderName, isParty)`.
    SCAskToJoinTeamPacket { team_id: u32, sender_id: u32, sender_name: String, is_party: bool } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.sender_id)
            .write_str(&self.sender_name)
            .write_bool(self.is_party);
    }
);

sc_packet!(
    /// `SCAskToJoinTeamAreaPacket(teamId, senderId, senderName, isParty)` (never sent by the
    /// 1.2 C#).
    SCAskToJoinTeamAreaPacket { team_id: u32, sender_id: u32, sender_name: String, is_party: bool } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.sender_id)
            .write_str(&self.sender_name)
            .write_bool(self.is_party);
    }
);

sc_packet!(
    /// `SCJoinedTeamPacket(team)`: `Team.Write` + every member's `TeamMember.Write`.
    SCJoinedTeamPacket { team: TeamInfo, members: Vec<TeamMemberInfo> } |self, s| {
        self.team.write(s);
        for m in &self.members {
            m.write(s);
        }
    }
);

sc_packet!(
    /// `SCLeavedTeamPacket(teamId, e, d)` (kicked, dismissed).
    SCLeavedTeamPacket { team_id: u32, kicked: bool, dismissed: bool } |self, s| {
        s.write_u32(self.team_id)
            .write_bool(self.kicked)
            .write_bool(self.dismissed);
    }
);

sc_packet!(
    /// `SCOverHeadMarkerSetPacket(teamId, index, isObjId, id)`.
    SCOverHeadMarkerSetPacket { team_id: u32, index: i32, is_obj_id: bool, id: u32 } |self, s| {
        s.write_u32(self.team_id)
            .write_i32(self.index)
            .write_u8(if self.is_obj_id { 2 } else { 1 });
        if self.is_obj_id {
            s.write_bc(self.id);
        } else {
            s.write_u32(self.id);
        }
    }
);

sc_packet!(
    /// `SCRefreshTeamMemberPacket(teamId, memberId, objId)`.
    SCRefreshTeamMemberPacket { team_id: u32, member_id: u32, obj_id: u32 } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.member_id)
            .write_bc(self.obj_id);
    }
);

sc_packet!(
    /// `SCRejectedTeamPacket(name, party)`.
    SCRejectedTeamPacket { name: String, party: bool } |self, s| {
        s.write_str(&self.name).write_bool(self.party);
    }
);

sc_packet!(
    /// `SCTeamAckRiskyActionPacket(teamId, id, ra, w, errorMessage)`.
    SCTeamAckRiskyActionPacket { team_id: u32, id: u32, risky_action: u8, w: i32, error_message: i16 } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.id)
            .write_u8(self.risky_action)
            .write_i32(self.w)
            .write_i16(self.error_message);
    }
);

sc_packet!(
    /// `SCTeamAreaInvitedPacket(r, s)` (never sent by the 1.2 C#).
    SCTeamAreaInvitedPacket { r: u32, s: bool } |self, st| {
        st.write_u32(self.r).write_bool(self.s);
    }
);

sc_packet!(
    /// `SCTeamBecameRaidTeamPacket(teamId)`.
    SCTeamBecameRaidTeamPacket { team_id: u32 } |self, s| {
        s.write_u32(self.team_id);
    }
);

sc_packet!(
    /// `SCTeamDismissedPacket(teamId)`.
    SCTeamDismissedPacket { team_id: u32 } |self, s| {
        s.write_u32(self.team_id);
    }
);

sc_packet!(
    /// `SCTeamLootingRuleChangedPacket(teamId, lootingRule, changeFlags)`.
    SCTeamLootingRuleChangedPacket { team_id: u32, looting_rule: LootingRule, change_flags: u8 } |self, s| {
        s.write_u32(self.team_id);
        self.looting_rule.write(s);
        s.write_u8(self.change_flags);
    }
);

sc_packet!(
    /// `SCTeamMemberDisconnectedPacket(teamId, id, member)` (`WritePerson`).
    SCTeamMemberDisconnectedPacket { team_id: u32, id: u32, member: TeamMemberInfo } |self, s| {
        s.write_u32(self.team_id).write_u32(self.id);
        self.member.write_person(s);
    }
);

sc_packet!(
    /// `SCTeamMemberJoinedPacket(teamId, member, party)`.
    SCTeamMemberJoinedPacket { team_id: u32, member: TeamMemberInfo, party: i32 } |self, s| {
        s.write_u32(self.team_id);
        self.member.write(s);
        s.write_i32(self.party);
    }
);

sc_packet!(
    /// `SCTeamMemberLeavedPacket(teamId, id, e)` (kicked).
    SCTeamMemberLeavedPacket { team_id: u32, id: u32, kicked: bool } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.id)
            .write_bool(self.kicked);
    }
);

sc_packet!(
    /// `SCTeamMemberMovedPacket(teamId, idFrom, idTo, from, to)`.
    SCTeamMemberMovedPacket { team_id: u32, id_from: u32, id_to: u32, from: u8, to: u8 } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.id_from)
            .write_u32(self.id_to)
            .write_u8(self.from)
            .write_u8(self.to);
    }
);

sc_packet!(
    /// `SCTeamMemberRoleChangedPacket(teamId, memberId, role)`.
    SCTeamMemberRoleChangedPacket { team_id: u32, member_id: u32, role: u8 } |self, s| {
        s.write_u32(self.team_id)
            .write_u32(self.member_id)
            .write_u8(self.role);
    }
);

sc_packet!(
    /// `SCTeamOwnerChangedPacket(teamId, id)`.
    SCTeamOwnerChangedPacket { team_id: u32, id: u32 } |self, s| {
        s.write_u32(self.team_id).write_u32(self.id);
    }
);

sc_packet!(
    /// `SCTeamPingPosPacket(hasPing, position, insId)`.
    SCTeamPingPosPacket { has_ping: bool, position: (f32, f32, f32), ins_id: u32 } |self, s| {
        s.write_bool(self.has_ping)
            .write_f32(self.position.0)
            .write_f32(self.position.1)
            .write_f32(self.position.2)
            .write_u32(self.ins_id);
    }
);

sc_packet!(
    /// `SCTeamRemoteMembersExPacket(members)` (`WritePerson` each).
    SCTeamRemoteMembersExPacket { members: Vec<TeamMemberInfo> } |self, s| {
        s.write_i32(self.members.len() as i32);
        for m in &self.members {
            m.write_person(s);
        }
    }
);

cs_packet!(
    /// `CSInviteToTeamPacket`.
    CSInviteToTeamPacket { team_id: u32, is_party: bool, target_name: String }
    read |s| { Ok(Self { team_id: s.read_u32()?, is_party: s.read_bool()?, target_name: s.read_string()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.ask_to_join(server, &p, &self.target_name, self.team_id, self.is_party, None);
        Ok(())
    }
);

cs_packet!(
    /// `CSInviteAreaToTeamPacket`.
    CSInviteAreaToTeamPacket { team_id: u32, is_party: bool }
    read |s| { Ok(Self { team_id: s.read_u32()?, is_party: s.read_bool()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.invite_area_to_team(server, &p, self.team_id, self.is_party);
        Ok(())
    }
);

cs_packet!(
    /// `CSReplyToJoinTeamPacket`.
    CSReplyToJoinTeamPacket {
        team_id: u32,
        is_party: bool,
        owner_id: u32,
        is_reject: bool,
        char_name: String,
        is_area: bool,
    }
    read |s| {
        Ok(Self {
            team_id: s.read_u32()?,
            is_party: s.read_bool()?,
            owner_id: s.read_u32()?,
            is_reject: s.read_bool()?,
            char_name: s.read_string()?,
            is_area: s.read_bool()?,
        })
    }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.reply_to_join_team(
            server,
            &p,
            self.team_id,
            self.is_party,
            self.owner_id,
            self.is_reject,
            &self.char_name,
            self.is_area,
        );
        Ok(())
    }
);

cs_packet!(
    /// `CSLeaveTeamPacket` (logged only, as in C#; leaving goes through
    /// `CSAskRiskyTeamActionPacket`).
    CSLeaveTeamPacket { team_id: u32 }
    read |s| { Ok(Self { team_id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::warn!("LeaveTeam, TeamId: {}", self.team_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSKickTeamMemberPacket` (logged only, as in C#).
    CSKickTeamMemberPacket { team_id: u32, member_id: u32 }
    read |s| { Ok(Self { team_id: s.read_u32()?, member_id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::warn!("KickTeamMember, TeamId: {}, MemberId: {}", self.team_id, self.member_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSDismissTeamPacket` (logged only, as in C#).
    CSDismissTeamPacket { team_id: u32 }
    read |s| { Ok(Self { team_id: s.read_u32()? }) }
    handle |self, server, conn| {
        tracing::warn!("DismissTeam, TeamId: {}", self.team_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSMakeTeamOwnerPacket`.
    CSMakeTeamOwnerPacket { team_id: u32, member_id: u32 }
    read |s| { Ok(Self { team_id: s.read_u32()?, member_id: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.make_team_owner(&p, self.team_id, self.member_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSConvertToRaidTeamPacket`.
    CSConvertToRaidTeamPacket { team_id: u32 }
    read |s| { Ok(Self { team_id: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.convert_to_raid(server, &p, self.team_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSMoveTeamMemberPacket`.
    CSMoveTeamMemberPacket { team_id: u32, target_id: u32, target2_id: u32, from: u8, to: u8 }
    read |s| {
        Ok(Self {
            team_id: s.read_u32()?,
            target_id: s.read_u32()?,
            target2_id: s.read_u32()?,
            from: s.read_u8()?,
            to: s.read_u8()?,
        })
    }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.move_team_member(
            server, &p, self.team_id, self.target_id, self.target2_id, self.from, self.to,
        );
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeLootingRulePacket`.
    CSChangeLootingRulePacket { team_id: u32, looting_rule: LootingRule, change_flags: u8 }
    read |s| {
        Ok(Self {
            team_id: s.read_u32()?,
            looting_rule: LootingRule::read(s)?,
            change_flags: s.read_u8()?,
        })
    }
    handle |self, server, conn| {
        tracing::warn!(
            "ChangeLootingRule, TeamId: {}, Flag: {}, Rule Method:{}, Grade:{}, LootMaster: {}, RollForBoP: {}",
            self.team_id, self.change_flags, self.looting_rule.loot_method, self.looting_rule.minimum_grade,
            self.looting_rule.loot_master, self.looting_rule.roll_for_bind_on_pickup
        );
        let p = active_char!(conn);
        server.systems.social.teams.change_looting_rule(&p, self.team_id, self.change_flags, self.looting_rule);
        Ok(())
    }
);

cs_packet!(
    /// `CSSetTeamMemberRolePacket`.
    CSSetTeamMemberRolePacket { team_id: u32, member_id: u32, role: u8 }
    read |s| { Ok(Self { team_id: s.read_u32()?, member_id: s.read_u32()?, role: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.set_team_member_role(&p, self.team_id, self.member_id, self.role);
        Ok(())
    }
);

cs_packet!(
    /// `CSSetOverHeadMarkerPacket` (type 1: u32 id, type 2: bc obj id).
    CSSetOverHeadMarkerPacket { team_id: u32, index: i32, kind: u8, id: u32 }
    read |s| {
        let team_id = s.read_u32()?;
        let index = s.read_i32()?;
        let kind = s.read_u8()?;
        let mut id = 0;
        if kind == 1 {
            id = s.read_u32()?;
        }
        if kind == 2 {
            id = s.read_bc()?;
        }
        Ok(Self { team_id, index, kind, id })
    }
    handle |self, server, conn| {
        let p = active_char!(conn);
        if self.team_id > 0 {
            server.systems.social.teams.set_over_head_marker(&p, self.team_id, self.index, self.kind, self.id);
        } else {
            p.send(SCOverHeadMarkerSetPacket {
                team_id: 0,
                index: self.index,
                is_obj_id: self.kind == 2,
                id: self.id,
            });
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSSetPingPosPacket`.
    CSSetPingPosPacket { team_id: u32, has_ping: bool, position: (f32, f32, f32), ins_id: u32 }
    read |s| {
        Ok(Self {
            team_id: s.read_u32()?,
            has_ping: s.read_bool()?,
            position: (s.read_f32()?, s.read_f32()?, s.read_f32()?),
            ins_id: s.read_u32()?,
        })
    }
    handle |self, server, conn| {
        let p = active_char!(conn);
        p.data().local_ping_position = self.position;
        if self.team_id > 0 {
            server.systems.social.teams.set_ping_pos(&p, self.team_id, self.has_ping, self.position, self.ins_id);
        } else {
            p.send(SCTeamPingPosPacket {
                has_ping: self.has_ping,
                position: self.position,
                ins_id: self.ins_id,
            });
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSAskRiskyTeamActionPacket`.
    CSAskRiskyTeamActionPacket { team_id: u32, target_id: u32, risky_action: u8 }
    read |s| { Ok(Self { team_id: s.read_u32()?, target_id: s.read_u32()?, risky_action: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        server.systems.social.teams.ask_risky_team(server, &p, self.team_id, self.target_id, self.risky_action);
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSInviteToTeamPacket>(LEVEL_PLAIN, cs::CSInviteToTeamPacket);
    reg.register::<CSInviteAreaToTeamPacket>(LEVEL_PLAIN, cs::CSInviteAreaToTeamPacket);
    reg.register::<CSReplyToJoinTeamPacket>(LEVEL_PLAIN, cs::CSReplyToJoinTeamPacket);
    reg.register::<CSLeaveTeamPacket>(LEVEL_PLAIN, cs::CSLeaveTeamPacket);
    reg.register::<CSKickTeamMemberPacket>(LEVEL_PLAIN, cs::CSKickTeamMemberPacket);
    reg.register::<CSMakeTeamOwnerPacket>(LEVEL_PLAIN, cs::CSMakeTeamOwnerPacket);
    // CSSetTeamOfficerPacket has no 1.2 opcode (0xfff).
    reg.register::<CSConvertToRaidTeamPacket>(LEVEL_PLAIN, cs::CSConvertToRaidTeamPacket);
    reg.register::<CSMoveTeamMemberPacket>(LEVEL_PLAIN, cs::CSMoveTeamMemberPacket);
    reg.register::<CSChangeLootingRulePacket>(LEVEL_PLAIN, cs::CSChangeLootingRulePacket);
    reg.register::<CSDismissTeamPacket>(LEVEL_PLAIN, cs::CSDismissTeamPacket);
    reg.register::<CSSetTeamMemberRolePacket>(LEVEL_PLAIN, cs::CSSetTeamMemberRolePacket);
    reg.register::<CSSetOverHeadMarkerPacket>(LEVEL_PLAIN, cs::CSSetOverHeadMarkerPacket);
    reg.register::<CSSetPingPosPacket>(LEVEL_PLAIN, cs::CSSetPingPosPacket);
    reg.register::<CSAskRiskyTeamActionPacket>(LEVEL_PLAIN, cs::CSAskRiskyTeamActionPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::registry::ClientPacket;
    use crate::systems::social::packets::body;

    fn info() -> TeamMemberInfo {
        TeamMemberInfo {
            id: 7,
            name: "Al".into(),
            race: 1,
            gender: 2,
            level: 30,
            role: 2,
            obj_id: 0x0A0B0C,
            is_online: true,
            zone_id: 100,
            instance_id: 0,
            hp: 1000,
            max_hp: 1200,
            mp: 50,
            max_mp: 60,
            position: (100.0, 200.0, 50.0),
            abilities: [1, 2, 3],
        }
    }

    #[test]
    fn member_write_and_write_person() {
        let m = info();
        let mut s = PacketStream::new();
        m.write(&mut s);
        assert_eq!(
            s.into_bytes(),
            vec![7, 0, 0, 0, 2, 0, b'A', b'l', 1, 2, 30, 2, 0x0C, 0x0B, 0x0A]
        );
        let mut s = PacketStream::new();
        m.write_person(&mut s);
        let b = s.into_bytes();
        assert_eq!(b.len(), 50);
        let mut e = PacketStream::new();
        e.write_u32(7)
            .write_u32(100)
            .write_u32(0)
            .write_u8(30)
            .write_i32(1000)
            .write_i32(1200)
            .write_i32(50)
            .write_i32(60)
            .write_position(100.0, 200.0, 50.0)
            .write_bc(0x0A0B0C)
            .write_raw(&[0, 1, 2, 3, 0, 0, 0, 0, 0]);
        assert_eq!(b, e.into_bytes());
        // Offline members carry obj id 0.
        let off = TeamMemberInfo {
            is_online: false,
            ..info()
        };
        let mut s = PacketStream::new();
        off.write_person(&mut s);
        assert_eq!(&s.as_bytes()[38..41], &[0, 0, 0]);
    }

    #[test]
    fn joined_team_layout() {
        let mut slots = vec![(0u32, false); 50];
        slots[0] = (7, true);
        slots[1] = (8, false);
        let mut marks = [(0u8, 0u32); 12];
        marks[0] = (1, 0x11223344);
        marks[1] = (2, 0x55);
        let team = TeamInfo {
            id: 3,
            owner_id: 7,
            is_party: true,
            party_counts: [2, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            slots,
            marks,
            looting_rule: LootingRule::default(),
        };
        let b = body(&SCJoinedTeamPacket {
            team,
            members: vec![info()],
        });
        let mut e = PacketStream::new();
        e.write_u32(3).write_u32(7).write_bool(true);
        e.write_raw(&[2, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        e.write_u32(7)
            .write_bool(true)
            .write_u32(8)
            .write_bool(false);
        for _ in 2..50 {
            e.write_u32(0).write_bool(false);
        }
        e.write_u8(1)
            .write_u32(0x11223344)
            .write_u8(2)
            .write_bc(0x55);
        for _ in 2..12 {
            e.write_u8(0);
        }
        e.write_u8(1).write_u8(2).write_u32(0).write_bool(true);
        info().write(&mut e);
        assert_eq!(b, e.into_bytes());
    }

    #[test]
    fn small_team_packets_are_byte_exact() {
        assert_eq!(
            body(&SCTeamAckRiskyActionPacket {
                team_id: 1,
                id: 2,
                risky_action: 1,
                w: 0,
                error_message: 0
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCOverHeadMarkerSetPacket {
                team_id: 1,
                index: -2,
                is_obj_id: false,
                id: 0
            }),
            vec![1, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCOverHeadMarkerSetPacket {
                team_id: 1,
                index: 3,
                is_obj_id: true,
                id: 0x010203
            }),
            vec![1, 0, 0, 0, 3, 0, 0, 0, 2, 3, 2, 1]
        );
        assert_eq!(
            body(&SCTeamPingPosPacket {
                has_ping: true,
                position: (1.0, 0.0, 0.0),
                ins_id: 9
            }),
            vec![1, 0, 0, 0x80, 0x3F, 0, 0, 0, 0, 0, 0, 0, 0, 9, 0, 0, 0]
        );
        assert_eq!(
            body(&SCTeamLootingRuleChangedPacket {
                team_id: 1,
                looting_rule: LootingRule::default(),
                change_flags: 15
            }),
            vec![1, 0, 0, 0, 1, 2, 0, 0, 0, 0, 1, 15]
        );
        assert_eq!(
            body(&SCLeavedTeamPacket {
                team_id: 4,
                kicked: false,
                dismissed: true
            }),
            vec![4, 0, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCRejectedTeamPacket {
                name: "x".into(),
                party: true
            }),
            vec![1, 0, b'x', 1]
        );
        assert_eq!(
            body(&SCTeamMemberMovedPacket {
                team_id: 1,
                id_from: 2,
                id_to: 3,
                from: 4,
                to: 5
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 5]
        );
        assert_eq!(
            body(&SCRefreshTeamMemberPacket {
                team_id: 1,
                member_id: 2,
                obj_id: 3
            }),
            vec![1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0]
        );
        let mut e = PacketStream::new();
        e.write_u32(1).write_u32(7);
        info().write_person(&mut e);
        assert_eq!(
            body(&SCTeamMemberDisconnectedPacket {
                team_id: 1,
                id: 7,
                member: info()
            }),
            e.into_bytes()
        );
        let mut e = PacketStream::new();
        e.write_i32(1);
        info().write_person(&mut e);
        assert_eq!(
            body(&SCTeamRemoteMembersExPacket {
                members: vec![info()]
            }),
            e.into_bytes()
        );
        let mut e = PacketStream::new();
        e.write_u32(1);
        info().write(&mut e);
        e.write_i32(1);
        assert_eq!(
            body(&SCTeamMemberJoinedPacket {
                team_id: 1,
                member: info(),
                party: 1
            }),
            e.into_bytes()
        );
    }

    #[test]
    fn client_packets_decode() {
        let mut s = PacketStream::new();
        s.write_u32(5).write_i32(2).write_u8(2).write_bc(0x123);
        let p =
            CSSetOverHeadMarkerPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        assert_eq!((p.team_id, p.index, p.kind, p.id), (5, 2, 2, 0x123));
        let mut s = PacketStream::new();
        s.write_u32(5)
            .write_bool(true)
            .write_u32(9)
            .write_bool(false)
            .write_str("Bo")
            .write_bool(false);
        let p =
            CSReplyToJoinTeamPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        assert_eq!(
            (
                p.team_id,
                p.is_party,
                p.owner_id,
                p.is_reject,
                p.char_name.as_str(),
                p.is_area
            ),
            (5, true, 9, false, "Bo", false)
        );
        let mut s = PacketStream::new();
        s.write_u32(1)
            .write_u8(2)
            .write_u8(3)
            .write_u32(4)
            .write_bool(true)
            .write_u8(9);
        let p =
            CSChangeLootingRulePacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        assert_eq!(p.change_flags, 9);
        assert_eq!(
            p.looting_rule,
            LootingRule {
                loot_method: 2,
                minimum_grade: 3,
                loot_master: 4,
                roll_for_bind_on_pickup: true
            }
        );
    }
}
