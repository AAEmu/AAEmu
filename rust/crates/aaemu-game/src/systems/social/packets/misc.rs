//! Remaining social packets: dominion / national tax, renames, chat bubbles / NPC chat / spam
//! reports and achievements. Most of them have no sender in the 1.2 C# (noted on each); they
//! exist so the owning features can be ported against them.

use chrono::{DateTime, Utc};

use super::{cs_packet, sc_packet};
use crate::models::char_data::CountUnreadMail;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::systems::social::achievement::AchievementInfo;
use crate::systems::social::dominion::DominionData;
use aaemu_commons::helpers::convert_coord_to_long;

// ---- Dominion ----

sc_packet!(
    /// `SCDominionDataPacket(dominionData, newlyDeclared, finalDataByRequest)` (sent by the
    /// `DeclareDominion` special effect).
    SCDominionDataPacket { dominion: DominionData, newly_declared: bool, final_data_by_request: bool } |self, s| {
        s.write(&self.dominion)
            .write_bool(self.newly_declared)
            .write_bool(self.final_data_by_request);
    }
);

sc_packet!(
    /// `SCDominionDeletedPacket(id)` (not sent by the 1.2 C#).
    SCDominionDeletedPacket { id: u16 } |self, s| {
        s.write_u16(self.id);
    }
);

sc_packet!(
    /// `SCDominionOwnerChangedPacket(id, unkId, rst, bestowed)` (not sent by the 1.2 C#).
    SCDominionOwnerChangedPacket { id: u16, unk_id: u32, rst: u64, bestowed: bool } |self, s| {
        s.write_u16(self.id)
            .write_u32(self.unk_id)
            .write_u64(self.rst)
            .write_bool(self.bestowed);
    }
);

sc_packet!(
    /// `SCDominionTaxBalancedPacket(id, peaceTaxMoney, peaceTaxAaPoint)` (not sent by the 1.2
    /// C#).
    SCDominionTaxBalancedPacket { id: u16, peace_tax_money: i32, peace_tax_aa_point: i32 } |self, s| {
        s.write_u16(self.id)
            .write_i32(self.peace_tax_money)
            .write_i32(self.peace_tax_aa_point);
    }
);

sc_packet!(
    /// `SCDominionTaxRatePacket(id, taxRate)` (not sent by the 1.2 C#).
    SCDominionTaxRatePacket { id: u16, tax_rate: i32 } |self, s| {
        s.write_u16(self.id).write_i32(self.tax_rate);
    }
);

sc_packet!(
    /// `SCNationalMonumentChangedPacket(id, type, x, y, z)` (not sent by the 1.2 C#).
    SCNationalMonumentChangedPacket { id: u16, monument_type: i64, x: f32, y: f32, z: f32 } |self, s| {
        s.write_u16(self.id)
            .write_i64(self.monument_type)
            .write_i64(convert_coord_to_long(self.x))
            .write_i64(convert_coord_to_long(self.y))
            .write_f32(self.z);
    }
);

sc_packet!(
    /// `SCNationalTaxRatePacket(id, taxRate, prevTaxRate, changedTime)` (not sent by the 1.2
    /// C#).
    SCNationalTaxRatePacket { id: u16, tax_rate: i32, prev_tax_rate: i32, changed_time: DateTime<Utc> } |self, s| {
        s.write_u16(self.id)
            .write_i32(self.tax_rate)
            .write_i32(self.prev_tax_rate)
            .write_date_time(self.changed_time);
    }
);

cs_packet!(
    /// `CSUpdateDominionTaxRatePacket` (logged only, as in C#). `CSUpdateNationalTaxRatePacket`
    /// has no 1.2 opcode (0xfff) and is not registered.
    CSUpdateDominionTaxRatePacket { id: u16, tax_rate: i32 }
    read |s| { Ok(Self { id: s.read_u16()?, tax_rate: s.read_i32()? }) }
    handle |self, server, conn| {
        tracing::debug!("UpdateDominionTaxRate, Id: {}, TaxRate: {}", self.id, self.tax_rate);
        Ok(())
    }
);

// ---- Names ----

sc_packet!(
    /// `SCCharacterRenamedPacket(characterId, oldName, newName)` (not sent by the 1.2 C#).
    SCCharacterRenamedPacket { character_id: u32, old_name: String, new_name: String } |self, s| {
        s.write_u32(self.character_id)
            .write_str(&self.old_name)
            .write_str(&self.new_name);
    }
);

sc_packet!(
    /// `SCUnitNameChangedPacket(objId, name)` (house / mate renames).
    SCUnitNameChangedPacket { obj_id: u32, name: String } |self, s| {
        s.write_bc(self.obj_id).write_str(&self.name);
    }
);

sc_packet!(
    /// `SCHouseOwnerNameChangedPacket(tl, newName)` (not sent by the 1.2 C#).
    SCHouseOwnerNameChangedPacket { tl: u16, new_name: String } |self, s| {
        s.write_u16(self.tl).write_str(&self.new_name);
    }
);

// ---- Chat ----

sc_packet!(
    /// `SCChatBubblePacket(bc, kind1, kind2, type, text)`: a bubble above a unit; `kind1` is the
    /// bubble style (1 normal, 2 think, 4 no bubble), `kind2` 1 = `text`, otherwise `type`
    /// (bubble id). Sent by the `BubbleEffect` skill effect.
    SCChatBubblePacket { obj_id: u32, kind1: u8, kind2: u8, bubble_type: u32, text: String } |self, s| {
        s.write_bc(self.obj_id).write_u8(self.kind1).write_u8(self.kind2);
        if self.kind2 != 1 {
            s.write_u32(self.bubble_type);
        } else {
            s.write_str(&self.text);
        }
    }
);

/// The `Npc` part of `SCNpcChatMessagePacket` (`npc?.Faction.Id`, `npc?.ObjId`, the name).
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct NpcChatSender {
    pub obj_id: u32,
    pub faction_id: u32,
    /// `npc.Name`, or when empty `LocalizationManager.Get("npcs", "name", templateId, "")`
    /// (resolved by the caller: the npc names are not loaded by the social subsystem).
    pub name: String,
}

sc_packet!(
    /// `SCNpcChatMessagePacket(chatType, npc, character, kind, type, message)` (the only C#
    /// constructor; `npc` / `character` may be null). `kind` 1 writes `type`, otherwise
    /// `message`. Sent by the AI `TalkBehavior`.
    SCNpcChatMessagePacket {
        chat_type: i16,
        npc: Option<NpcChatSender>,
        /// `character?.ObjId ?? 0`.
        character_obj_id: u32,
        kind: u8,
        msg_type: u32,
        message: String,
    } |self, s| {
        let faction_id = self.npc.as_ref().map_or(0, |n| n.faction_id);
        s.write_i16(self.chat_type)
            .write_i16(faction_id as i16) // subType
            .write_u32(faction_id)
            .write_bc(self.npc.as_ref().map_or(0, |n| n.obj_id))
            .write_str(self.npc.as_ref().map_or("", |n| n.name.as_str()))
            .write_bc(self.character_obj_id)
            .write_u8(self.kind);
        if self.kind == 1 {
            s.write_u32(self.msg_type);
        } else {
            s.write_str(&self.message);
        }
    }
);

sc_packet!(
    /// `SCSpamReportedPacket(mailId, isUnreadMailCountModified, count)` (not sent by the 1.2
    /// C#).
    SCSpamReportedPacket { mail_id: i64, is_unread_mail_count_modified: bool, count: CountUnreadMail } |self, s| {
        s.write_i64(self.mail_id)
            .write_bool(self.is_unread_mail_count_modified)
            .write(&self.count);
    }
);

cs_packet!(
    /// `CSReportSpamPacket` (logged only, as in C#).
    CSReportSpamPacket { mail_id: i64 }
    read |s| { Ok(Self { mail_id: s.read_i64()? }) }
    handle |self, server, conn| {
        tracing::debug!("ReportSpam, mailId: {}", self.mail_id);
        Ok(())
    }
);

// ---- Achievements (no achievement manager in the 1.2 C#: none of these is sent) ----

sc_packet!(
    /// `SCAchievementsPacket(achievements)`: i32 count, then id / amount / complete (the C#
    /// comment says at most 50 per packet; nothing enforces it).
    SCAchievementsPacket { achievements: Vec<AchievementInfo> } |self, s| {
        s.write_i32(self.achievements.len() as i32);
        for a in &self.achievements {
            s.write_u32(a.id).write_u32(a.amount).write_date_time(a.complete);
        }
    }
);

sc_packet!(
    /// `SCAchievementChangedPacket(id, amount)`.
    SCAchievementChangedPacket { id: u32, amount: i32 } |self, s| {
        s.write_u32(self.id).write_i32(self.amount);
    }
);

sc_packet!(
    /// `SCAchievementCompletedPacket(id)`: `complete` is `DateTime.UtcNow` at write time in C#
    /// ([`SCAchievementCompletedPacket::new`] takes it at construction).
    SCAchievementCompletedPacket { id: u32, complete: DateTime<Utc> } |self, s| {
        s.write_u32(self.id).write_date_time(self.complete);
    }
);

impl SCAchievementCompletedPacket {
    pub fn new(id: u32) -> Self {
        Self {
            id,
            complete: Utc::now(),
        }
    }
}

sc_packet!(
    /// `SCAchievementItemSentPacket(id, byMail)`.
    SCAchievementItemSentPacket { id: u32, by_mail: bool } |self, s| {
        s.write_u32(self.id).write_bool(self.by_mail);
    }
);

sc_packet!(
    /// `SCAchievementResetedPacket(id, amount)` (+ `DateTime.UtcNow`, see
    /// [`SCAchievementResetedPacket::new`]).
    SCAchievementResetedPacket { id: u32, amount: i32, complete: DateTime<Utc> } |self, s| {
        s.write_u32(self.id)
            .write_i32(self.amount)
            .write_date_time(self.complete);
    }
);

impl SCAchievementResetedPacket {
    pub fn new(id: u32, amount: i32) -> Self {
        Self {
            id,
            amount,
            complete: Utc::now(),
        }
    }
}

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSUpdateDominionTaxRatePacket>(LEVEL_PLAIN, cs::CSUpdateDominionTaxRatePacket);
    reg.register::<CSReportSpamPacket>(LEVEL_PLAIN, cs::CSReportSpamPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::registry::ClientPacket;
    use crate::systems::social::dominion;
    use crate::systems::social::packets::body;
    use aaemu_commons::PacketStream;
    use chrono::TimeZone;

    fn t(secs: i64) -> DateTime<Utc> {
        Utc.timestamp_opt(secs, 0).unwrap()
    }

    #[test]
    fn dominion_packets_are_byte_exact() {
        let mut e = dominion::tests::sample_bytes();
        e.extend_from_slice(&[1, 0]);
        assert_eq!(
            body(&SCDominionDataPacket {
                dominion: dominion::tests::sample(),
                newly_declared: true,
                final_data_by_request: false
            }),
            e
        );
        assert_eq!(body(&SCDominionDeletedPacket { id: 0x0102 }), vec![2, 1]);
        let mut e = PacketStream::new();
        e.write_u16(3).write_u32(4).write_u64(5).write_bool(true);
        assert_eq!(
            body(&SCDominionOwnerChangedPacket {
                id: 3,
                unk_id: 4,
                rst: 5,
                bestowed: true
            }),
            e.into_bytes()
        );
        assert_eq!(
            body(&SCDominionTaxBalancedPacket {
                id: 1,
                peace_tax_money: 2,
                peace_tax_aa_point: -1
            }),
            vec![1, 0, 2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]
        );
        assert_eq!(
            body(&SCDominionTaxRatePacket {
                id: 1,
                tax_rate: 10
            }),
            vec![1, 0, 10, 0, 0, 0]
        );
        let mut e = PacketStream::new();
        e.write_u16(1)
            .write_i64(2)
            .write_i64(((10.5f32 * 4096.0) as i64) << 32)
            .write_i64(((20.0f32 * 4096.0) as i64) << 32)
            .write_f32(30.0);
        assert_eq!(
            body(&SCNationalMonumentChangedPacket {
                id: 1,
                monument_type: 2,
                x: 10.5,
                y: 20.0,
                z: 30.0
            }),
            e.into_bytes()
        );
        let mut e = PacketStream::new();
        e.write_u16(1).write_i32(5).write_i32(3).write_i64(1000);
        assert_eq!(
            body(&SCNationalTaxRatePacket {
                id: 1,
                tax_rate: 5,
                prev_tax_rate: 3,
                changed_time: t(1000)
            }),
            e.into_bytes()
        );
    }

    #[test]
    fn name_packets_are_byte_exact() {
        assert_eq!(
            body(&SCCharacterRenamedPacket {
                character_id: 7,
                old_name: "A".into(),
                new_name: "Bo".into()
            }),
            vec![7, 0, 0, 0, 1, 0, b'A', 2, 0, b'B', b'o']
        );
        assert_eq!(
            body(&SCUnitNameChangedPacket {
                obj_id: 0x0A0B0C,
                name: "H".into()
            }),
            vec![0x0C, 0x0B, 0x0A, 1, 0, b'H']
        );
        assert_eq!(
            body(&SCHouseOwnerNameChangedPacket {
                tl: 0x0102,
                new_name: "N".into()
            }),
            vec![2, 1, 1, 0, b'N']
        );
    }

    #[test]
    fn chat_packets_are_byte_exact() {
        // kind2 = 1: text.
        assert_eq!(
            body(&SCChatBubblePacket {
                obj_id: 0x11,
                kind1: 2,
                kind2: 1,
                bubble_type: 9,
                text: "hi".into()
            }),
            vec![0x11, 0, 0, 2, 1, 2, 0, b'h', b'i']
        );
        // Otherwise the bubble id.
        assert_eq!(
            body(&SCChatBubblePacket {
                obj_id: 0x11,
                kind1: 1,
                kind2: 2,
                bubble_type: 9,
                text: String::new()
            }),
            vec![0x11, 0, 0, 1, 2, 9, 0, 0, 0]
        );
        let npc = NpcChatSender {
            obj_id: 0x22,
            faction_id: 0x10094,
            name: "Judge".into(),
        };
        let mut e = PacketStream::new();
        e.write_i16(-3)
            .write_i16(0x0094)
            .write_u32(0x10094)
            .write_bc(0x22)
            .write_str("Judge")
            .write_bc(0x33)
            .write_u8(1)
            .write_u32(113);
        assert_eq!(
            body(&SCNpcChatMessagePacket {
                chat_type: -3,
                npc: Some(npc),
                character_obj_id: 0x33,
                kind: 1,
                msg_type: 113,
                message: "x".into()
            }),
            e.into_bytes()
        );
        // No npc / character, kind != 1: the message.
        assert_eq!(
            body(&SCNpcChatMessagePacket {
                chat_type: 1,
                npc: None,
                character_obj_id: 0,
                kind: 0,
                msg_type: 113,
                message: "m".into()
            }),
            vec![1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, b'm']
        );
        let mut e = PacketStream::new();
        e.write_i64(-2)
            .write_bool(true)
            .write_i32(1)
            .write_i32(2)
            .write_i32(3)
            .write_i32(4);
        assert_eq!(
            body(&SCSpamReportedPacket {
                mail_id: -2,
                is_unread_mail_count_modified: true,
                count: CountUnreadMail {
                    sent: 1,
                    received: 2,
                    mia_received: 3,
                    commercial_received: 4
                }
            }),
            e.into_bytes()
        );
    }

    #[test]
    fn achievement_packets_are_byte_exact() {
        let mut e = PacketStream::new();
        e.write_i32(2)
            .write_u32(1)
            .write_u32(10)
            .write_i64(100)
            .write_u32(2)
            .write_u32(0)
            .write_i64(0);
        assert_eq!(
            body(&SCAchievementsPacket {
                achievements: vec![
                    AchievementInfo {
                        id: 1,
                        amount: 10,
                        complete: t(100)
                    },
                    AchievementInfo {
                        id: 2,
                        ..Default::default()
                    }
                ]
            }),
            e.into_bytes()
        );
        assert_eq!(
            body(&SCAchievementChangedPacket { id: 1, amount: -1 }),
            vec![1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]
        );
        let mut e = vec![5, 0, 0, 0];
        e.extend_from_slice(&200i64.to_le_bytes());
        assert_eq!(
            body(&SCAchievementCompletedPacket {
                id: 5,
                complete: t(200)
            }),
            e
        );
        assert_eq!(
            body(&SCAchievementItemSentPacket {
                id: 5,
                by_mail: true
            }),
            vec![5, 0, 0, 0, 1]
        );
        let mut e = vec![5, 0, 0, 0, 3, 0, 0, 0];
        e.extend_from_slice(&200i64.to_le_bytes());
        assert_eq!(
            body(&SCAchievementResetedPacket {
                id: 5,
                amount: 3,
                complete: t(200)
            }),
            e
        );
        let now = Utc::now().timestamp();
        let p = SCAchievementCompletedPacket::new(9);
        assert!((p.complete.timestamp() - now).abs() <= 1);
        assert_eq!(SCAchievementResetedPacket::new(9, 2).amount, 2);
    }

    #[test]
    fn misc_requests_decode() {
        let mut s = PacketStream::from_bytes(vec![2, 1, 10, 0, 0, 0]);
        assert_eq!(
            CSUpdateDominionTaxRatePacket::read(&mut s).unwrap(),
            CSUpdateDominionTaxRatePacket {
                id: 0x0102,
                tax_rate: 10
            }
        );
        let mut s = PacketStream::from_bytes((-5i64).to_le_bytes().to_vec());
        assert_eq!(
            CSReportSpamPacket::read(&mut s).unwrap(),
            CSReportSpamPacket { mail_id: -5 }
        );
    }
}
