//! Port of `Models/Game/Char/CharacterMails` (`character.Mails`): mailbox listing, reading,
//! player-to-player sending, taking attachments, deleting and returning mails. The unread
//! counters are the character's `unread_mail` (`Mails.UnreadMailCount`).

use crate::data::items::ITEM_COINS;
use crate::models::container::slot_type;
use crate::models::item_task::{task_type, ItemFxList};
use crate::world::PlayerRef;

use super::error_message;
use super::inventory::{self, Inv, ItemTxn};
use super::mail::{
    self, mail_result, mail_status, mail_type, ACTABILITY_COMMERCE, MAX_MAIL_ATTACHMENTS,
    NORMAL_MAIL_DELAY_SECS,
};
use super::mail_kinds::MailPlayerToPlayer;
use super::mail_packets::{
    ItemIdAndLocation, SCAttachmentTakenPacket, SCCountUnreadMailPacket, SCMailBodyPacket,
    SCMailDeletedPacket, SCMailListEndPacket, SCMailListPacket, SCMailSentPacket,
    SCMailStatusUpdatedPacket,
};

/// `SendUnreadMailCount()`.
pub fn send_unread_mail_count(fx: &mut ItemFxList, player: &PlayerRef) {
    let count = player.data().unread_mail;
    fx.send(player.character_id, SCCountUnreadMailPacket { count });
}

/// `OpenMailbox()`.
pub fn open_mailbox(txn: &mut ItemTxn<'_>, player: &PlayerRef) {
    let self_id = player.character_id;
    let mut total = 0;
    let ids = mail::get_current_mail_list(txn, self_id);
    let mails = &txn.server.systems.economy.mail;
    for id in ids {
        let Some(m) = mails.get_mail_by_id(id) else {
            continue;
        };
        let is_sent = if m.header.sender_id == self_id && m.header.receiver_id == self_id {
            Some(false)
        } else if m.header.sender_id == self_id {
            Some(true)
        } else if m.header.receiver_id == self_id {
            Some(false)
        } else {
            None
        };
        if let Some(is_sent) = is_sent {
            txn.env.fx.send(
                self_id,
                SCMailListPacket {
                    is_sent,
                    mails: vec![m.header_wire()],
                },
            );
            total += 1;
        }
    }
    txn.env.fx.send(
        self_id,
        SCMailListEndPacket {
            total_headers: total,
            total_bodies: 0,
        },
    );
}

/// `ReadMail(isSent, id)`.
pub fn read_mail(txn: &mut ItemTxn<'_>, player: &PlayerRef, is_sent: bool, id: i64) {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    let mut st = mails.state();
    let Some(m) = st.all.get_mut(id) else { return };
    let owner = if is_sent {
        m.header.sender_id
    } else {
        m.header.receiver_id
    };
    if owner != player.character_id {
        return;
    }
    if m.header.status == mail_status::UNREAD && !is_sent {
        player.data().unread_mail.update_received(m.mail_type, -1);
        m.open_date = mail::now();
        m.is_dirty = true;
        m.header.status = mail_status::READ;
        m.is_delivered = true;
    }
    let count = player.data().unread_mail;
    let body = m.body_wire(server);
    let status = m.header.status;
    drop(st);
    let cid = player.character_id;
    txn.env.fx.send(
        cid,
        SCMailBodyPacket {
            is_prepare: false,
            is_sent,
            body,
            is_open_date_modified: true,
            count,
        },
    );
    txn.env.fx.send(
        cid,
        SCMailStatusUpdatedPacket {
            is_sent,
            mail_id: id,
            status,
        },
    );
    send_unread_mail_count(&mut txn.env.fx, player);
}

/// `SendMailToPlayer(mailType, receiverName, title, text, attachments, money0, money1, money2,
/// extra, itemSlots)`.
#[allow(clippy::too_many_arguments)]
pub fn send_mail_to_player(
    txn: &mut ItemTxn<'_>,
    player: &PlayerRef,
    mail_type: u8,
    receiver_name: &str,
    title: &str,
    text: &str,
    attachments: u8,
    money0: i32,
    money1: i32,
    money2: i32,
    extra: i64,
    item_slots: &[(u8, u8)],
) -> u8 {
    let server = txn.server;
    if receiver_name.trim().is_empty() || server.names.get_character_id(receiver_name) == 0 {
        return mail_result::UNABLE_TO_FIND_RECIPIENT;
    }

    let mut mail = MailPlayerToPlayer::new(server, player, receiver_name);
    let now = mail::now();
    mail.mail.mail_type = mail_type;
    mail.mail.title = title.to_string();
    mail.mail.header.attachments = attachments;
    mail.mail.header.extra = extra;
    mail.mail.body.text = text.to_string();
    mail.mail.body.send_date = now;
    mail.mail.body.recv_date = now;

    mail.mail.attach_money(money0, money1, money2);

    // First verify source items, and add them to the attachments of body
    if !mail.prepare_attachment_items(item_slots) {
        return mail_result::INVALID_SLOT;
    }

    // With attachments in place, we can calculate the send fee
    let mail_fee = mail.get_mail_fee();
    if i64::from(mail_fee + money0) > player.data().money {
        return mail_result::INSUFFICIENT_COINS;
    }

    if !mail.finalize_attachments(txn) {
        return mail_result::INVALID_SLOT; // Should never fail at this point
    }

    // Add delay if not a normal snail mail
    if mail_type == mail_type::NORMAL {
        mail.mail.body.recv_date = mail::now() + NORMAL_MAIL_DELAY_SECS;
    }

    // Send it
    let Some(id) = mail::send_mail(txn, mail.mail) else {
        return mail_result::MAIL_ERROR_OCCURRED;
    };
    let Some(header) = server
        .systems
        .economy
        .mail
        .get_mail_by_id(id)
        .map(|m| m.header_wire())
    else {
        return mail_result::MAIL_ERROR_OCCURRED;
    };
    txn.env.fx.send(
        player.character_id,
        SCMailSentPacket {
            mail: header,
            items: item_slots.to_vec(),
        },
    );
    // Take the fee
    inventory::subtract_money(
        &mut txn.out(),
        player,
        slot_type::INVENTORY,
        mail_fee + money0,
        task_type::DEPOSIT_MONEY,
    );
    mail_result::SUCCESS
}

/// `GameException("GetAttachmentFailedAddToBag")`: an attachment could not be moved to the bag
/// after the free space check.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GetAttachmentFailedAddToBag;

/// `GetAttached(mailId, takeMoney, takeItems, takeAllSelected, specifiedItemId)`. `Err` where the
/// C# throws `GameException("GetAttachmentFailedAddToBag")` (the packet handler stops there).
pub fn get_attached(
    txn: &mut ItemTxn<'_>,
    player: &PlayerRef,
    mail_id: i64,
    mut take_money: bool,
    take_items: bool,
    take_all_selected: bool,
    specified_item_id: u64,
) -> Result<bool, GetAttachmentFailedAddToBag> {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    let mut res = true;
    let Some(this_mail) = mails.get_mail_by_id(mail_id) else {
        return Ok(res);
    };
    let cid = player.character_id;
    if this_mail.header.receiver_id != cid {
        return Ok(false);
    }
    let mut took_money = false;
    if this_mail.mail_type == mail_type::AUC_OFF_SUCCESS
        && this_mail.body.copper_coins > 0
        && take_money
    {
        let labor = player.data().labor_power;
        if labor < 1 {
            inventory::send_error_message_to(
                &mut txn.out(),
                player,
                error_message::NOT_ENOUGH_LABOR_POWER,
            );
            take_money = false;
        } else {
            inventory::change_labor(&mut txn.out(), server, player, -1, ACTABILITY_COMMERCE);
        }
    }
    if this_mail.body.copper_coins > 0 && take_money {
        inventory::change_money_at(
            &mut txn.out(),
            player,
            slot_type::INVENTORY,
            this_mail.body.copper_coins,
            task_type::DEPOSIT_MONEY,
        );
        mails.with_mail_mut(mail_id, |m| {
            m.body.copper_coins = 0;
            m.is_dirty = true;
            m.header.attachments = m.header.attachments.wrapping_sub(1);
        });
        took_money = true;
    }

    let mut item_slot_list = Vec::new();
    // Check if items need to be taken, and add them to a list
    if take_items {
        let inv = Inv::of(player);
        let bag = inv.bag();
        let data = &server.game_data.items;
        let mut to_remove = Vec::new();
        for item_id in &this_mail.body.attachments {
            // if not our specified item, skip this slot
            if specified_item_id > 0 && *item_id != specified_item_id {
                continue;
            }
            // Sanity-check
            if *item_id == 0 {
                continue;
            }
            let Some(attachment) = server.items.item_by_id(*item_id) else {
                // C#: the attachment object always exists; a vanished item cannot be moved.
                tracing::error!("GetAttached: attachment {item_id} of mail {mail_id} not found");
                continue;
            };
            let money = player.data().money;
            let max_count = data
                .template(attachment.template_id)
                .map_or(0, |t| t.max_count);
            // Free Space Check
            let (space, found) = {
                let b = bag.lock().expect("container poisoned");
                let space = b.space_left_for_item(&attachment, data, money);
                let found: Vec<(i32, i32, i32)> = if attachment.template_id == ITEM_COINS {
                    // SpaceLeftForMoney: currentItems = [itemToAdd]
                    vec![(attachment.slot, attachment.count, max_count)]
                } else {
                    b.items
                        .iter()
                        .filter(|i| {
                            i.template_id == attachment.template_id && i.grade == attachment.grade
                        })
                        .map(|i| {
                            (
                                i.slot,
                                i.count,
                                data.template(i.template_id).map_or(0, |t| t.max_count),
                            )
                        })
                        .collect()
                };
                (space, found)
            };
            if space >= attachment.count {
                let mut stack_slot = None;
                // Check if we can stack the item onto an existing one
                if max_count > 1 && !found.is_empty() {
                    for (slot, count, fi_max) in &found {
                        if count + attachment.count <= *fi_max {
                            stack_slot = Some(*slot);
                            break;
                        }
                    }
                }
                let location = ItemIdAndLocation {
                    id: attachment.id,
                    slot_type: attachment.slot_type,
                    slot: attachment.slot as u8,
                };
                // Move item to player inventory
                if mail::add_or_move_existing_item(
                    txn,
                    &bag,
                    task_type::MAIL,
                    attachment.id,
                    stack_slot.unwrap_or(-1),
                ) {
                    item_slot_list.push(location);
                    mails.with_mail_mut(mail_id, |m| {
                        m.header.attachments = m.header.attachments.wrapping_sub(1);
                    });
                    to_remove.push(attachment.id);
                } else {
                    // Should technically never fail because of previous free slot check
                    tracing::error!("GetAttachmentFailedAddToBag");
                    return Err(GetAttachmentFailedAddToBag);
                }
            } else {
                // Bag Full
                inventory::send_error_message_to(&mut txn.out(), player, error_message::BAG_FULL);
                res = false;
            }
        }
        // Removed those marked to be taken
        mails.with_mail_mut(mail_id, |m| {
            for ia in &to_remove {
                if let Some(pos) = m.body.attachments.iter().position(|x| x == ia) {
                    m.body.attachments.remove(pos);
                    m.is_dirty = true;
                }
            }
        });
    }

    // Send attachments taken packets (if needed)
    if took_money {
        txn.env.fx.send(
            cid,
            SCAttachmentTakenPacket {
                mail_id,
                money: true,
                aa_point: false,
                take_sequentially: take_all_selected,
                items: Vec::new(),
            },
        );
        mails.with_mail_mut(mail_id, |m| m.is_dirty = true);
    }
    // Items: one packet per item (ZeromusXYZ: fixes partial deliveries)
    for slot in &item_slot_list {
        txn.env.fx.send(
            cid,
            SCAttachmentTakenPacket {
                mail_id,
                money: take_money,
                aa_point: false,
                take_sequentially: take_all_selected,
                items: vec![*slot],
            },
        );
        mails.with_mail_mut(mail_id, |m| m.is_dirty = true);
    }

    // Mark mail as read in case we took at least one item from it
    let mark_read = mails
        .with_mail_mut(mail_id, |m| {
            if m.header.status == mail_status::UNREAD && (took_money || !item_slot_list.is_empty())
            {
                m.header.status = mail_status::READ;
                m.is_dirty = true;
                Some(m.mail_type)
            } else {
                None
            }
        })
        .flatten();
    if let Some(mt) = mark_read {
        player.data().unread_mail.update_received(mt, -1);
        txn.env.fx.send(
            cid,
            SCMailStatusUpdatedPacket {
                is_sent: false,
                mail_id,
                status: mail_status::READ,
            },
        );
        send_unread_mail_count(&mut txn.env.fx, player);
    }
    Ok(res)
}

/// `DeleteMail(id, isSent)`: only the receiver may delete received mail.
pub fn delete_mail(txn: &mut ItemTxn<'_>, player: &PlayerRef, id: i64, is_sent: bool) {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    let mut st = mails.state();
    let Some(m) = st.all.get(id) else { return };
    if is_sent || m.header.receiver_id != player.character_id || m.header.attachments > 0 {
        return;
    }
    let modified = m.header.status != mail_status::READ;
    if modified {
        player.data().unread_mail.update_received(m.mail_type, -1);
    }
    let count = player.data().unread_mail;
    txn.env.fx.send(
        player.character_id,
        SCMailDeletedPacket {
            is_sent,
            mail_id: id,
            is_unread_mail_count_modified: modified,
            count,
        },
    );
    mail::delete_mail_in(server, &mut st, id);
}

/// `ReturnMail(id)`. The C# indexes `Body.Attachments[i]` for i in 0..10, which throws
/// `ArgumentOutOfRangeException` unless the mail has 10 attachments (mirrored: nothing happens).
pub fn return_mail(txn: &mut ItemTxn<'_>, player: &PlayerRef, id: i64) {
    let server = txn.server;
    let Some(this_mail) = server.systems.economy.mail.get_mail_by_id(id) else {
        return;
    };
    if this_mail.header.receiver_id != player.character_id {
        return;
    }
    if this_mail.body.attachments.len() < MAX_MAIL_ATTACHMENTS {
        tracing::error!(
            "ReturnMail({id}): ArgumentOutOfRangeException (fewer than 10 attachments)"
        );
        return;
    }
    let mut item_slots = Vec::with_capacity(MAX_MAIL_ATTACHMENTS);
    for item_id in this_mail.body.attachments.iter().take(MAX_MAIL_ATTACHMENTS) {
        let Some(item) = server.items.item_by_id(*item_id) else {
            tracing::error!("ReturnMail({id}): NullReferenceException (item {item_id})");
            return;
        };
        item_slots.push(if item.slot_type == slot_type::NONE {
            (0, 0)
        } else {
            (item.slot_type, item.slot as u8)
        });
    }
    send_mail_to_player(
        txn,
        player,
        this_mail.mail_type,
        &this_mail.header.sender_name,
        &this_mail.title,
        &this_mail.body.text,
        this_mail.header.attachments,
        this_mail.body.copper_coins,
        this_mail.body.billing_amount,
        this_mail.body.money_amount2,
        this_mail.header.extra,
        &item_slots,
    );
    delete_mail(txn, player, id, false);
}
