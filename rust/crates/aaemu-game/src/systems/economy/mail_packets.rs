//! The mail packets. C->S: `CSSendMailPacket`, `CSListMailPacket`, `CSListMailContinuePacket`,
//! `CSReadMailPacket`, `CSTakeAttachmentItemPacket`, `CSTakeAttachmentMoneyPacket`,
//! `CSTakeAttachmentSequentially`, `CSTakeAllAttachmentItemPacket`, `CSPayChargeMoneyPacket`,
//! `CSDeleteMailPacket`, `CSReturnMailPacket` (0xfff in 1.2: never sent by the client).
//! S->C: `SCMailFailedPacket`, `SCMailSentPacket`, `SCGotMailPacket`, `SCMailListPacket`,
//! `SCMailListEndPacket`, `SCMailBodyPacket`, `SCMailReceiverOpenedPacket`,
//! `SCAttachmentTakenPacket`, `SCChargeMoneyPaidPacket`, `SCMailDeletedPacket`,
//! `SCMailReturnedPacket`, `SCMailStatusUpdatedPacket`, `SCMailRemovedPacket`,
//! `SCQuestRewardedByMailPacket` (`SCCountUnreadMailPacket` is the phase-2 one).

use aaemu_commons::{Marshal, PacketStream};

use crate::error::Result;
use crate::models::char_data::CountUnreadMail;
use crate::models::item::UnixTime;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

pub use crate::packets::world::SCCountUnreadMailPacket;

use super::error_message;
use super::inventory::{self, ItemTxn};
use super::item_handlers::{active, sync_client_packet};
use super::mail::{self, mail_result, mail_status, MailBodyWire, MailHeaderWire};
use super::mail_character;

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSSendMailPacket>(LEVEL_PLAIN, cs::CSSendMailPacket);
    reg.register::<CSListMailPacket>(LEVEL_PLAIN, cs::CSListMailPacket);
    reg.register::<CSListMailContinuePacket>(LEVEL_PLAIN, cs::CSListMailContinuePacket);
    reg.register::<CSReadMailPacket>(LEVEL_PLAIN, cs::CSReadMailPacket);
    reg.register::<CSTakeAttachmentItemPacket>(LEVEL_PLAIN, cs::CSTakeAttachmentItemPacket);
    reg.register::<CSTakeAttachmentMoneyPacket>(LEVEL_PLAIN, cs::CSTakeAttachmentMoneyPacket);
    reg.register::<CSTakeAttachmentSequentially>(LEVEL_PLAIN, cs::CSTakeAttachmentSequentially);
    reg.register::<CSPayChargeMoneyPacket>(LEVEL_PLAIN, cs::CSPayChargeMoneyPacket);
    reg.register::<CSDeleteMailPacket>(LEVEL_PLAIN, cs::CSDeleteMailPacket);
    reg.register::<CSReturnMailPacket>(LEVEL_PLAIN, cs::CSReturnMailPacket);
    reg.register::<CSTakeAllAttachmentItemPacket>(LEVEL_PLAIN, cs::CSTakeAllAttachmentItemPacket);
}

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// `SCMailFailedPacket(err, items, money)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCMailFailedPacket {
    pub err: u8,
    pub items: Vec<(u8, u8)>,
    pub money: bool,
}
packet_def!(
    SCMailFailedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailFailedPacket
);
impl Marshal for SCMailFailedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.err);
        for (st, slot) in &self.items {
            s.write_u8(*st).write_u8(*slot);
        }
        s.write_bool(self.money);
    }
}

/// `SCMailSentPacket(mail, items)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCMailSentPacket {
    pub mail: MailHeaderWire,
    pub items: Vec<(u8, u8)>,
}
packet_def!(
    SCMailSentPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailSentPacket
);
impl Marshal for SCMailSentPacket {
    fn write(&self, s: &mut PacketStream) {
        self.mail.write(s);
        for (st, slot) in &self.items {
            s.write_u8(*st).write_u8(*slot);
        }
    }
}

/// `SCGotMailPacket(mail, count, isCancel, body)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCGotMailPacket {
    pub mail: MailHeaderWire,
    pub count: CountUnreadMail,
    pub is_cancel: bool,
    pub body: Option<MailBodyWire>,
}
packet_def!(
    SCGotMailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGotMailPacket
);
impl Marshal for SCGotMailPacket {
    fn write(&self, s: &mut PacketStream) {
        self.mail.write(s);
        self.count.write(s);
        s.write_bool(self.body.is_some());
        if let Some(b) = &self.body {
            b.write(s);
        }
        s.write_bool(self.is_cancel);
    }
}

/// `SCMailListPacket(isSent, mails)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCMailListPacket {
    pub is_sent: bool,
    pub mails: Vec<MailHeaderWire>,
}
packet_def!(
    SCMailListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailListPacket
);
impl Marshal for SCMailListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_sent)
            .write_i32(self.mails.len() as i32);
        for m in &self.mails {
            m.write(s);
        }
    }
}

/// `SCMailListEndPacket(totalHeaders, totalBodies)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCMailListEndPacket {
    pub total_headers: i32,
    pub total_bodies: i32,
}
packet_def!(
    SCMailListEndPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailListEndPacket
);
impl Marshal for SCMailListEndPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.total_headers).write_i32(self.total_bodies);
    }
}

/// `SCMailBodyPacket(isPrepare, isSent, body, isOpenDateModified, count)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCMailBodyPacket {
    pub is_prepare: bool,
    pub is_sent: bool,
    pub body: MailBodyWire,
    pub is_open_date_modified: bool,
    pub count: CountUnreadMail,
}
packet_def!(
    SCMailBodyPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailBodyPacket
);
impl Marshal for SCMailBodyPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_prepare).write_bool(self.is_sent);
        self.body.write(s);
        s.write_bool(self.is_open_date_modified);
        self.count.write(s);
    }
}

/// `SCMailReceiverOpenedPacket(mailId, openDate)` (unused in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCMailReceiverOpenedPacket {
    pub mail_id: i64,
    pub open_date: UnixTime,
}
packet_def!(
    SCMailReceiverOpenedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailReceiverOpenedPacket
);
impl Marshal for SCMailReceiverOpenedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id).write_i64(self.open_date);
    }
}

/// `ItemIdAndLocation`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemIdAndLocation {
    pub id: u64,
    pub slot_type: u8,
    pub slot: u8,
}

/// `SCAttachmentTakenPacket(mailId, money, aaPoint, takeSequentially, itemsList)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCAttachmentTakenPacket {
    pub mail_id: i64,
    pub money: bool,
    pub aa_point: bool,
    pub take_sequentially: bool,
    pub items: Vec<ItemIdAndLocation>,
}
packet_def!(
    SCAttachmentTakenPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAttachmentTakenPacket
);
impl Marshal for SCAttachmentTakenPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id)
            .write_bool(self.money)
            .write_bool(self.aa_point)
            .write_bool(self.take_sequentially)
            .write_u8(self.items.len() as u8);
        for i in 0..10 {
            match self.items.get(i) {
                Some(it) => {
                    s.write_u64(it.id).write_u8(it.slot_type).write_u8(it.slot);
                }
                None => {
                    s.write_u64(0).write_u8(0).write_u8(0);
                }
            }
        }
    }
}

/// `SCChargeMoneyPaidPacket(mailId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCChargeMoneyPaidPacket {
    pub mail_id: i64,
}
packet_def!(
    SCChargeMoneyPaidPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCChargeMoneyPaidPacket
);
impl Marshal for SCChargeMoneyPaidPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id);
    }
}

/// `SCMailDeletedPacket(isSent, mailId, isUnreadMailCountModified, count)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCMailDeletedPacket {
    pub is_sent: bool,
    pub mail_id: i64,
    pub is_unread_mail_count_modified: bool,
    pub count: CountUnreadMail,
}
packet_def!(
    SCMailDeletedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailDeletedPacket
);
impl Marshal for SCMailDeletedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_sent)
            .write_i64(self.mail_id)
            .write_bool(self.is_unread_mail_count_modified);
        self.count.write(s);
    }
}

/// `SCMailReturnedPacket(mailId, mail)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCMailReturnedPacket {
    pub mail_id: i64,
    pub mail: MailHeaderWire,
}
packet_def!(
    SCMailReturnedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailReturnedPacket
);
impl Marshal for SCMailReturnedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id);
        self.mail.write(s);
    }
}

/// `SCMailStatusUpdatedPacket(isSent, mailId, status)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCMailStatusUpdatedPacket {
    pub is_sent: bool,
    pub mail_id: i64,
    pub status: u8,
}
packet_def!(
    SCMailStatusUpdatedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailStatusUpdatedPacket
);
impl Marshal for SCMailStatusUpdatedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_sent)
            .write_i64(self.mail_id)
            .write_u8(self.status);
    }
}

/// `SCMailRemovedPacket(isSent, mailId)` (unused in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCMailRemovedPacket {
    pub is_sent: bool,
    pub mail_id: i64,
}
packet_def!(
    SCMailRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCMailRemovedPacket
);
impl Marshal for SCMailRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_sent).write_i64(self.mail_id);
    }
}

/// `SCQuestRewardedByMailPacket(questList)` (sent by `Quest.DistributeRewards`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCQuestRewardedByMailPacket {
    pub quests: Vec<u32>,
}
packet_def!(
    SCQuestRewardedByMailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestRewardedByMailPacket
);
impl Marshal for SCQuestRewardedByMailPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.quests.len() as u8);
        for q in &self.quests {
            s.write_u32(*q);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// `DoodadFuncNaviOpenMailbox`.
const MAILBOX_FUNC: &str = "DoodadFuncNaviOpenMailbox";

/// `CSSendMailPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSendMailPacket {
    pub mail_type: u8,
    pub receiver_char_name: String,
    pub unk_id: u32,
    pub title: String,
    pub text: String,
    pub attachments: u8,
    pub money0: i32,
    pub money1: i32,
    pub money2: i32,
    pub extra: i64,
    pub item_slots: Vec<(u8, u8)>,
    pub doodad_obj_id: u32,
}

impl CSSendMailPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        let mail_type = s.read_u8()?;
        let receiver_char_name = s.read_string()?;
        let unk_id = s.read_u32()?;
        let title = s.read_string()?;
        let text = s.read_string()?;
        let attachments = s.read_u8()?;
        let money0 = s.read_i32()?;
        let money1 = s.read_i32()?;
        let money2 = s.read_i32()?;
        let extra = s.read_i64()?;
        let mut item_slots = Vec::with_capacity(10);
        for _ in 0..10 {
            let st = s.read_u8()?;
            let slot = s.read_u8()?;
            item_slots.push(if st == 0 { (0, 0) } else { (st, slot) });
        }
        let doodad_obj_id = s.read_bc()?;
        Ok(Self {
            mail_type,
            receiver_char_name,
            unk_id,
            title,
            text,
            attachments,
            money0,
            money1,
            money2,
            extra,
            item_slots,
            doodad_obj_id,
        })
    }

    /// Validate if we are near a mailbox: a doodad whose current funcs open the mailbox, within
    /// 5 m (2D).
    fn mailbox_check(&self, server: &GameServer, p: &PlayerRef) -> bool {
        let pobj = WorldObject::from(p);
        let Some(world) = server.world.world_of(&pobj) else {
            return false;
        };
        let Some(doodad) = world.doodad(self.doodad_obj_id) else {
            return false;
        };
        let (has_func, template_id) = {
            let d = doodad.data();
            (
                d.current_func_types.iter().any(|f| f == MAILBOX_FUNC),
                d.template_id,
            )
        };
        if !has_func {
            tracing::warn!(
                "SendMail by {} invalid - doodad ObjId {} ({template_id}) does not have DoodadFuncNaviOpenMailbox func",
                p.data().unit.name,
                doodad.obj_id
            );
            return false;
        }
        let a = world.world_pos_rot(&pobj);
        let b = world.world_pos_rot(&WorldObject::from(&doodad));
        let dx = f64::from(a.x) - f64::from(b.x);
        let dy = f64::from(a.y) - f64::from(b.y);
        let dist = (dx * dx + dy * dy).sqrt() as f32;
        dist <= 5.0 // 5m is kinda generous I guess
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::debug!("SendMail by {}", p.data().unit.name);
        if !self.mailbox_check(server, &p) {
            inventory::send_error_message(&p, error_message::MAIL_FAIL_MAILBOX_NOT_FOUND, 0, true);
            return;
        }
        let mut txn = ItemTxn::begin(server);
        let result = mail_character::send_mail_to_player(
            &mut txn,
            &p,
            self.mail_type,
            &self.receiver_char_name,
            &self.title,
            &self.text,
            self.attachments,
            self.money0,
            self.money1,
            self.money2,
            self.extra,
            &self.item_slots,
        );
        if result == mail_result::SUCCESS {
            inventory::send_error_message_to(&mut txn.out(), &p, error_message::MAIL_SUCCESS);
        } else {
            txn.env.fx.send(
                p.character_id,
                SCMailFailedPacket {
                    err: result,
                    items: self.item_slots.clone(),
                    money: false,
                },
            );
        }
    }
}
sync_client_packet!(CSSendMailPacket);

/// `CSListMailPacket` (empty).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSListMailPacket;

impl CSListMailPacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let mut txn = ItemTxn::begin(server);
        mail_character::open_mailbox(&mut txn, &p);
    }
}
sync_client_packet!(CSListMailPacket);

/// `CSListMailContinuePacket` (empty, only logged).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSListMailContinuePacket;

impl CSListMailContinuePacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::debug!("ListMailContinue");
    }
}
sync_client_packet!(CSListMailContinuePacket);

/// `CSReadMailPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSReadMailPacket {
    pub is_sent: bool,
    pub mail_id: i64,
}

impl CSReadMailPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            is_sent: s.read_bool()?,
            mail_id: s.read_i64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::debug!("ReadMail, Id: {}, isSent: {}", self.mail_id, self.is_sent);
        let mut txn = ItemTxn::begin(server);
        mail_character::read_mail(&mut txn, &p, self.is_sent, self.mail_id);
    }
}
sync_client_packet!(CSReadMailPacket);

/// `CSTakeAttachmentItemPacket` (the item description is read and ignored, only its id counts).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTakeAttachmentItemPacket {
    pub mail_id: i64,
    pub item_template_id: u32,
    pub id: u64,
    pub grade: u8,
    pub flags: u8,
    pub count: u32,
    pub detail_type: u8,
    pub creation_time: i64,
    pub lifespan_mins: u32,
    pub type2: u32,
    pub world_id: u8,
    pub unsecure_date_time: i64,
    pub unpack_date_time: i64,
    pub slot_type: u8,
    pub slot: u8,
}

impl CSTakeAttachmentItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
            item_template_id: s.read_u32()?,
            id: s.read_u64()?,
            grade: s.read_u8()?,
            flags: s.read_u8()?,
            count: s.read_u32()?,
            detail_type: s.read_u8()?,
            creation_time: s.read_i64()?,
            lifespan_mins: s.read_u32()?,
            type2: s.read_u32()?,
            world_id: s.read_u8()?,
            unsecure_date_time: s.read_i64()?,
            unpack_date_time: s.read_i64()?,
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let mut txn = ItemTxn::begin(server);
        let _ =
            mail_character::get_attached(&mut txn, &p, self.mail_id, false, true, false, self.id);
    }
}
sync_client_packet!(CSTakeAttachmentItemPacket);

/// `CSTakeAttachmentMoneyPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTakeAttachmentMoneyPacket {
    pub mail_id: i64,
}

impl CSTakeAttachmentMoneyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let mut txn = ItemTxn::begin(server);
        let _ = mail_character::get_attached(&mut txn, &p, self.mail_id, true, false, true, 0);
    }
}
sync_client_packet!(CSTakeAttachmentMoneyPacket);

/// `CSTakeAttachmentSequentially`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTakeAttachmentSequentially {
    pub mail_id: i64,
}

impl CSTakeAttachmentSequentially {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::debug!("TakeAttachmentSequentially, mailId: {}", self.mail_id);
        let Some(mail) = server.systems.economy.mail.get_mail_by_id(self.mail_id) else {
            // C#: NullReferenceException on mail.Header
            tracing::error!(
                "TakeAttachmentSequentially: mail {} not found",
                self.mail_id
            );
            return;
        };
        let mut txn = ItemTxn::begin(server);
        if mail.header.receiver_id != p.character_id {
            // just a check for hackers trying to steal mails
            inventory::send_error_message_to(&mut txn.out(), &p, error_message::MAIL_INVALID);
        } else {
            let _ = mail_character::get_attached(&mut txn, &p, self.mail_id, true, true, true, 0);
        }
    }
}
sync_client_packet!(CSTakeAttachmentSequentially);

/// `CSTakeAllAttachmentItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTakeAllAttachmentItemPacket {
    pub mail_id: i64,
}

impl CSTakeAllAttachmentItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let name = p.data().unit.name.clone();
        tracing::debug!("CSTakeAllAttachmentItemPacket {} -> {name}", self.mail_id);
        let mut txn = ItemTxn::begin(server);
        match mail_character::get_attached(&mut txn, &p, self.mail_id, true, true, true, 0) {
            Ok(true) => {
                txn.env.fx.send(
                    p.character_id,
                    SCMailStatusUpdatedPacket {
                        is_sent: false,
                        mail_id: self.mail_id,
                        status: mail_status::READ,
                    },
                );
                mail_character::delete_mail(&mut txn, &p, self.mail_id, false);
                mail_character::send_unread_mail_count(&mut txn.env.fx, &p);
            }
            Ok(false) => tracing::debug!(
                "CSTakeAllAttachmentItemPacket - Failed for: {} -> {name}",
                self.mail_id
            ),
            Err(_) => {}
        }
    }
}
sync_client_packet!(CSTakeAllAttachmentItemPacket);

/// `CSPayChargeMoneyPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPayChargeMoneyPacket {
    pub mail_id: i64,
    pub auto_use_aa_point: bool,
}

impl CSPayChargeMoneyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::debug!(
            "PayChargeMoney, mailId: {}, autoUseAAPoint: {}",
            self.mail_id,
            self.auto_use_aa_point
        );
        let mut txn = ItemTxn::begin(server);
        if !mail::pay_charge_money(&mut txn, &p, self.mail_id, self.auto_use_aa_point) {
            tracing::warn!(
                "PayChargeMoney failed, mailId: {}, autoUseAAPoint: {}",
                self.mail_id,
                self.auto_use_aa_point
            );
        }
    }
}
sync_client_packet!(CSPayChargeMoneyPacket);

/// `CSDeleteMailPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSDeleteMailPacket {
    pub mail_id: i64,
    pub is_sent: bool,
}

impl CSDeleteMailPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
            is_sent: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let mut txn = ItemTxn::begin(server);
        mail_character::delete_mail(&mut txn, &p, self.mail_id, self.is_sent);
    }
}
sync_client_packet!(CSDeleteMailPacket);

/// `CSReturnMailPacket` (opcode 0xfff: not in the 1.2 offsets).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSReturnMailPacket {
    pub mail_id: i64,
}

impl CSReturnMailPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            mail_id: s.read_i64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::debug!("ReturnMail, mailId: {}", self.mail_id);
        let mut txn = ItemTxn::begin(server);
        mail_character::return_mail(&mut txn, &p, self.mail_id);
    }
}
sync_client_packet!(CSReturnMailPacket);
