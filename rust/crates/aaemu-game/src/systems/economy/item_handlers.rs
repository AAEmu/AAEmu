//! The C->S packets of the core item layer: `CSSwapItemsPacket`, `CSSplitBagItemPacket`,
//! `CSDestroyItemPacket`, `CSExpandSlotsPacket`, `CSDepositMoneyPacket`,
//! `CSWithdrawMoneyPacket`, `CSSplitCofferItemPacket`, `CSSwapCofferItemsPacket`,
//! `CSItemSecurePacket`, `CSItemUnsecurePacket`, `CSEquipmentsSecurePacket`,
//! `CSEquipmentsUnsecurePacket`, `CSThisTimeUnpackItemPacket`.

use aaemu_commons::PacketStream;
use chrono::Utc;

use crate::error::Result;
use crate::models::container::slot_type;
use crate::models::item::ITEM_FLAG_SOUL_BOUND;
use crate::models::item_task::{task_type, ItemTask};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::world::PlayerRef;

use super::error_message;
use super::inventory::{self, Inv, ItemTxn};
use super::item_packets::{SCItemTaskSuccessPacket, SCSyncItemLifespanPacket};

/// `ItemFlag.Secure`.
pub const ITEM_FLAG_SECURE: u8 = 0x04;
/// `ItemFlag.Unpacked`.
pub const ITEM_FLAG_UNPACKED: u8 = 0x10;
/// `ItemFlag.AuctionWin`.
pub const ITEM_FLAG_AUCTION_WIN: u8 = 0x20;

/// Implements `ClientPacket` for a type with inherent `read(&mut PacketStream) -> Result<Self>`
/// and `run(self, &GameServer, &mut GameConnection)` (synchronous `Read` + `Execute`).
macro_rules! sync_client_packet {
    ($ty:ident) => {
        impl $crate::net::registry::ClientPacket for $ty {
            const NAME: &'static str = stringify!($ty);

            fn read(s: &mut aaemu_commons::PacketStream) -> $crate::error::Result<Self> {
                $ty::read_body(s)
            }

            fn handle<'a>(
                self,
                server: &'a $crate::server::GameServer,
                conn: &'a mut $crate::net::connection::GameConnection,
            ) -> futures::future::BoxFuture<'a, $crate::error::Result<()>> {
                Box::pin(async move {
                    self.run(server, conn);
                    Ok(())
                })
            }
        }
    };
}
#[allow(unused_imports)]
pub(crate) use sync_client_packet;

/// `Connection.ActiveChar` (the C# throws a NullReferenceException without one).
pub fn active(conn: &GameConnection) -> Option<PlayerRef> {
    conn.active_char.clone()
}

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSSwapItemsPacket>(LEVEL_PLAIN, cs::CSSwapItemsPacket);
    reg.register::<CSSplitBagItemPacket>(LEVEL_PLAIN, cs::CSSplitBagItemPacket);
    reg.register::<CSDestroyItemPacket>(LEVEL_PLAIN, cs::CSDestroyItemPacket);
    reg.register::<CSExpandSlotsPacket>(LEVEL_PLAIN, cs::CSExpandSlotsPacket);
    reg.register::<CSDepositMoneyPacket>(LEVEL_PLAIN, cs::CSDepositMoneyPacket);
    reg.register::<CSWithdrawMoneyPacket>(LEVEL_PLAIN, cs::CSWithdrawMoneyPacket);
    reg.register::<CSSplitCofferItemPacket>(LEVEL_PLAIN, cs::CSSplitCofferItemPacket);
    reg.register::<CSSwapCofferItemsPacket>(LEVEL_PLAIN, cs::CSSwapCofferItemsPacket);
    reg.register::<CSItemSecurePacket>(LEVEL_PLAIN, cs::CSItemSecurePacket);
    reg.register::<CSItemUnsecurePacket>(LEVEL_PLAIN, cs::CSItemUnsecurePacket);
    reg.register::<CSEquipmentsSecurePacket>(LEVEL_PLAIN, cs::CSEquipmentsSecurePacket);
    reg.register::<CSEquipmentsUnsecurePacket>(LEVEL_PLAIN, cs::CSEquipmentsUnsecurePacket);
    reg.register::<CSThisTimeUnpackItemPacket>(LEVEL_PLAIN, cs::CSThisTimeUnpackItemPacket);
}

/// The `fromItemId, toItemId, fromSlotType, fromSlot, toSlotType, toSlot` block.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct MoveArgs {
    pub from_item_id: u64,
    pub to_item_id: u64,
    pub from_slot_type: u8,
    pub from_slot: u8,
    pub to_slot_type: u8,
    pub to_slot: u8,
}

impl MoveArgs {
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            from_item_id: s.read_u64()?,
            to_item_id: s.read_u64()?,
            from_slot_type: s.read_u8()?,
            from_slot: s.read_u8()?,
            to_slot_type: s.read_u8()?,
            to_slot: s.read_u8()?,
        })
    }
}

/// `CSSwapItemsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSwapItemsPacket {
    pub args: MoveArgs,
}

impl CSSwapItemsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            args: MoveArgs::read(s)?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let a = self.args;
        let inv = Inv::of(&p);
        let ok = {
            let mut txn = ItemTxn::begin(server);
            inventory::split_or_move_item(
                &mut txn,
                &inv,
                task_type::SWAP_ITEMS,
                a.from_item_id,
                a.from_slot_type,
                a.from_slot,
                a.to_item_id,
                a.to_slot_type,
                a.to_slot,
                0,
            )
        };
        if !ok {
            inventory::send_error_message(&p, error_message::INVALID_SLOT, 0, true);
        }
    }
}
sync_client_packet!(CSSwapItemsPacket);

/// `CSSplitBagItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSplitBagItemPacket {
    pub args: MoveArgs,
    pub count: i32,
}

impl CSSplitBagItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            args: MoveArgs::read(s)?,
            count: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let a = self.args;
        let inv = Inv::of(&p);
        let mut txn = ItemTxn::begin(server);
        inventory::split_or_move_item(
            &mut txn,
            &inv,
            task_type::SWAP_ITEMS,
            a.from_item_id,
            a.from_slot_type,
            a.from_slot,
            a.to_item_id,
            a.to_slot_type,
            a.to_slot,
            self.count,
        );
    }
}
sync_client_packet!(CSSplitBagItemPacket);

/// `CSDestroyItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSDestroyItemPacket {
    pub item_id: u64,
    pub slot_type: u8,
    pub slot: u8,
    pub count: i32,
}

impl CSDestroyItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            item_id: s.read_u64()?,
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
            count: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let inv = Inv::of(&p);
        let destroyed = {
            let mut txn = ItemTxn::begin(server);
            let Some(item) = inv.item_at(self.slot_type, self.slot) else {
                tracing::warn!("DestroyItem: Invalid item...");
                return;
            };
            if item.id != self.item_id || item.count < self.count {
                tracing::warn!("DestroyItem: Invalid item...");
                return;
            }
            let mut count = self.count;
            if count <= 0 {
                // SusManager.LogActivity(CategoryCheating, ...)
                tracing::warn!(
                    "CSDestroyItemPacket, player {} attempted to destroy a negative amount of items {count} for item: template {}, id {}",
                    p.data().unit.name,
                    item.template_id,
                    item.id
                );
                count = item.count;
            }
            let Some(container) = inv.get(self.slot_type) else {
                return;
            };
            let mut c = container.lock().expect("container poisoned");
            if item.count > count {
                let Some(i) = c.get_item_by_id_mut(item.id) else {
                    return;
                };
                i.count -= count;
                i.dirty = true;
                let task = ItemTask::count_update(i, -count);
                let snapshot = i.clone();
                drop(c);
                p.send(SCItemTaskSuccessPacket {
                    action: task_type::DESTROY,
                    tasks: vec![task],
                    force_remove: Vec::new(),
                });
                snapshot
            } else {
                let Some(removed) =
                    c.remove_item_task(task_type::DESTROY, item.id, true, &mut txn.env)
                else {
                    tracing::warn!("DestroyItem: Failed to destroy item...");
                    return;
                };
                removed
            }
        };
        inventory::on_item_manually_destroyed(server, &p, &destroyed);
    }
}
sync_client_packet!(CSDestroyItemPacket);

/// `CSExpandSlotsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSExpandSlotsPacket {
    pub slot_type: i32,
    pub auto_use_aa_point: bool,
}

impl CSExpandSlotsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_i32()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::debug!(
            "ExpandSlots, slotType -> {}, autoUseAAPoint -> {}",
            self.slot_type,
            self.auto_use_aa_point
        );
        let Some(p) = active(conn) else { return };
        let inv = Inv::of(&p);
        let mut txn = ItemTxn::begin(server);
        inventory::expand_slot(&mut txn, &p, &inv, self.slot_type as u8);
    }
}
sync_client_packet!(CSExpandSlotsPacket);

/// `CSDepositMoneyPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSDepositMoneyPacket {
    pub amount: i32,
    pub aa_point: i32,
}

impl CSDepositMoneyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            amount: s.read_i32()?,
            aa_point: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        inventory::change_money(
            &mut inventory::Out::Direct,
            &p,
            slot_type::INVENTORY,
            slot_type::BANK,
            self.amount,
            task_type::DEPOSIT_MONEY,
        );
    }
}
sync_client_packet!(CSDepositMoneyPacket);

/// `CSWithdrawMoneyPacket` (the C# keeps the default `ItemTaskType.DepositMoney`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSWithdrawMoneyPacket {
    pub amount: i32,
    pub aa_point: i32,
}

impl CSWithdrawMoneyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            amount: s.read_i32()?,
            aa_point: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        inventory::change_money(
            &mut inventory::Out::Direct,
            &p,
            slot_type::BANK,
            slot_type::INVENTORY,
            self.amount,
            task_type::DEPOSIT_MONEY,
        );
    }
}
sync_client_packet!(CSWithdrawMoneyPacket);

/// `CSSplitCofferItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSplitCofferItemPacket {
    pub count: i32,
    pub args: MoveArgs,
    pub db_id: u64,
}

impl CSSplitCofferItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            count: s.read_i32()?,
            args: MoveArgs::read(s)?,
            db_id: s.read_u64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let a = self.args;
        let inv = Inv::of(&p);
        let ok = {
            let mut txn = ItemTxn::begin(server);
            inventory::coffer_items(
                &mut txn,
                &inv,
                task_type::SPLIT_COFFER_ITEMS,
                self.count,
                a.from_item_id,
                a.to_item_id,
                a.from_slot_type,
                a.from_slot,
                a.to_slot_type,
                a.to_slot,
                self.db_id,
            )
        };
        if !ok {
            // Not sure what error to send here
            inventory::send_error_message(
                &p,
                error_message::CANNOT_MOVE_SOULBOUND_ITEM_TO_COFFER,
                0,
                true,
            );
        }
    }
}
sync_client_packet!(CSSplitCofferItemPacket);

/// `CSSwapCofferItemsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSwapCofferItemsPacket {
    pub args: MoveArgs,
    pub db_id: u64,
}

impl CSSwapCofferItemsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            args: MoveArgs::read(s)?,
            db_id: s.read_u64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let a = self.args;
        let inv = Inv::of(&p);
        let ok = {
            let mut txn = ItemTxn::begin(server);
            inventory::coffer_items(
                &mut txn,
                &inv,
                task_type::SWAP_COFFER_ITEMS,
                0,
                a.from_item_id,
                a.to_item_id,
                a.from_slot_type,
                a.from_slot,
                a.to_slot_type,
                a.to_slot,
                self.db_id,
            )
        };
        if !ok {
            inventory::send_error_message(
                &p,
                error_message::CANNOT_MOVE_SOULBOUND_ITEM_TO_COFFER,
                0,
                true,
            );
        }
    }
}
sync_client_packet!(CSSwapCofferItemsPacket);

/// `CSItemSecurePacket` / `CSItemUnsecurePacket` (both only log in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSItemSecurePacket {
    pub slot_type: u8,
    pub slot: u8,
    pub item_id: u64,
}

impl CSItemSecurePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        s.read_u8()?;
        let slot_type = s.read_u8()?;
        s.read_u8()?;
        let slot = s.read_u8()?;
        Ok(Self {
            slot_type,
            slot,
            item_id: s.read_u64()?,
        })
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!(
            "ItemSecure, ItemId: {}, SlotType: {}, Slot: {}",
            self.item_id,
            self.slot_type,
            self.slot
        );
    }
}
sync_client_packet!(CSItemSecurePacket);

/// `CSItemUnsecurePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSItemUnsecurePacket(pub CSItemSecurePacket);

impl CSItemUnsecurePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self(CSItemSecurePacket::read_body(s)?))
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!(
            "ItemUnsecure, ItemId: {}, SlotType: {}, Slot: {}",
            self.0.item_id,
            self.0.slot_type,
            self.0.slot
        );
    }
}
sync_client_packet!(CSItemUnsecurePacket);

/// `CSEquipmentsSecurePacket` (empty).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSEquipmentsSecurePacket;

impl CSEquipmentsSecurePacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!("EquipmentsSecure");
    }
}
sync_client_packet!(CSEquipmentsSecurePacket);

/// `CSEquipmentsUnsecurePacket` (empty).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSEquipmentsUnsecurePacket;

impl CSEquipmentsUnsecurePacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!("EquipmentsUnsecure");
    }
}
sync_client_packet!(CSEquipmentsUnsecurePacket);

/// `CSThisTimeUnpackItemPacket` (`ItemManager.UnwrapItem`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSThisTimeUnpackItemPacket {
    pub slot_type: u8,
    pub slot: u8,
    pub item_id: u64,
}

impl CSThisTimeUnpackItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
            item_id: s.read_u64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        if !unwrap_item(server, &p, self.slot_type, self.slot, self.item_id) {
            inventory::send_error_message(&p, error_message::ITEM_UPDATE_FAIL, 0, true);
        }
    }
}
sync_client_packet!(CSThisTimeUnpackItemPacket);

/// `ItemManager.UnwrapItem(character, slotType, slot, itemId)`.
pub fn unwrap_item(
    server: &GameServer,
    p: &PlayerRef,
    slot_type: u8,
    slot: u8,
    item_id: u64,
) -> bool {
    let _txn = ItemTxn::begin(server);
    let Some(container) = Inv::of(p)
        .holder_of_any(item_id)
        .or_else(|| server.items.holding_container(item_id))
    else {
        return false;
    };
    let mut c = container.lock().expect("container poisoned");
    if c.owner_id != p.character_id {
        return false;
    }
    let Some(item) = c.get_item_by_id_mut(item_id) else {
        return false;
    };
    if item.slot_type != slot_type || item.slot != i32::from(slot) {
        tracing::warn!(
            "UnwrapItem: Requested item position does not match up for {item_id} of user {}",
            p.character_id
        );
        return false;
    }
    let template = server.game_data.items.template(item.template_id);
    item.unpack_time = Utc::now().timestamp();
    item.flags |= ITEM_FLAG_UNPACKED;
    if template.is_some_and(|t| t.bind_type == crate::data::items::bind_type::BIND_ON_UNPACK) {
        item.flags |= ITEM_FLAG_SOUL_BOUND;
    }
    item.dirty = true;
    let secure = item.flags & ITEM_FLAG_SECURE != 0;
    let task = ItemTask::update_security(
        item,
        item.flags,
        secure,
        secure,
        item.flags & ITEM_FLAG_UNPACKED != 0,
    );
    let lifespan = template
        .and_then(|t| t.equip())
        .is_some_and(|e| e.charge_lifetime > 0)
        .then_some(SCSyncItemLifespanPacket {
            added: true,
            item_id: item.id,
            template_id: item.template_id,
            expire_time: item.unpack_time,
        });
    drop(c);
    p.send(SCItemTaskSuccessPacket {
        action: task_type::ITEM_TASK_THISTIME_UNPACK,
        tasks: vec![task],
        force_remove: Vec::new(),
    });
    if let Some(l) = lifespan {
        p.send(l);
    }
    true
}
