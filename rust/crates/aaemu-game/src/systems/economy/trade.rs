//! Port of `Core/Managers/TradeManager.cs` (player to player trade) and its packets:
//! `CSCanStartTradePacket`, `CSCannotStartTradePacket`, `CSStartTradePacket`,
//! `CSCancelTradePacket`, `CSPutupTradeItemPacket`, `CSTakedownTradeItemPacket`,
//! `CSPutupTradeMoneyPacket`, `CSTradeLockPacket`, `CSTradeOkPacket` and `SCCanStartTrade`,
//! `SCCannotStartTrade`, `SCTradeStarted`, `SCTradeCanceled`, `SCTradeItemPutup`,
//! `SCOtherTradeItemPutup`, `SCTradeItemTookdown`, `SCOtherTradeItemTookdown`,
//! `SCTradeMoneyPutup`, `SCOtherTradeMoneyPutup`, `SCTradeLockUpdate`, `SCTradeOkUpdate`,
//! `SCTradeMade`.
//!
//! The trade table lives behind one mutex ([`TradeManager`]); it is never held while an
//! [`ItemTxn`] is started (the finished trade is taken out of the table first) and callers must
//! not hold a character or container lock when calling in.
//!
//! The C# never cancels a trade when a partner logs out or walks away: the entry stays in the
//! table and the partner's next trade packet throws on the missing character. [`cancel_trade`]
//! (`TradeManager.CancelTrade(objId, reason)`) is public for the coordinator should it want to
//! wire it into leave-world / disconnect (that would be a deviation).

use std::sync::{Mutex, MutexGuard};

use aaemu_commons::{Marshal, PacketStream};

use crate::error::Result;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemTask};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::world::PlayerRef;

use super::inventory::{lock_pair, Inv, ItemTxn};
use super::item_handlers::{active, sync_client_packet};

// ---------------------------------------------------------------------------------------------
// Manager
// ---------------------------------------------------------------------------------------------

/// `TradeTemplate`. Items are kept by id (the C# keeps the `Item` objects).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TradeTemplate {
    pub id: u32,
    pub owner_obj_id: u32,
    pub target_obj_id: u32,
    pub lock_owner: bool,
    pub lock_target: bool,
    pub ok_owner: bool,
    pub ok_target: bool,
    pub owner_items: Vec<u64>,
    pub target_items: Vec<u64>,
    pub owner_money_putup: i32,
    pub target_money_putup: i32,
}

/// `TradeManager._trades` (insertion order, like the C# dictionary without removals).
#[derive(Debug, Default)]
pub struct TradeManager {
    trades: Mutex<Vec<TradeTemplate>>,
}

type Trades<'a> = MutexGuard<'a, Vec<TradeTemplate>>;

impl TradeManager {
    fn lock(&self) -> Trades<'_> {
        self.trades.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The running trades (copies, for tests / GM tools).
    pub fn trades(&self) -> Vec<TradeTemplate> {
        self.lock().clone()
    }
}

fn manager(server: &GameServer) -> &TradeManager {
    &server.systems.economy.trade
}

/// `GetTradeId(objId)`.
fn get_trade_id(trades: &Trades<'_>, obj_id: u32) -> u32 {
    trades
        .iter()
        .find(|t| t.owner_obj_id == obj_id || t.target_obj_id == obj_id)
        .map_or(0, |t| t.id)
}

fn trade_mut<'a>(trades: &'a mut Trades<'_>, id: u32) -> Option<&'a mut TradeTemplate> {
    trades.iter_mut().find(|t| t.id == id)
}

fn character(server: &GameServer, obj_id: u32) -> Option<PlayerRef> {
    server.world.character_by_obj_id(obj_id)
}

fn name_of(p: &PlayerRef) -> String {
    p.data().unit.name.clone()
}

/// `IsTrading(objId)`: cancels a running trade of `obj_id` (reason 0) and reports it.
fn is_trading(server: &GameServer, trades: &mut Trades<'_>, obj_id: u32) -> bool {
    let trade_id = get_trade_id(trades, obj_id);
    if trade_id == 0 {
        return false;
    }
    cancel_locked(server, trades, obj_id, 0, trade_id); // TODO (C#) - reason?
    true
}

/// `CancelTrade(objId, reason, tradeId)` with the table locked.
fn cancel_locked(
    server: &GameServer,
    trades: &mut Trades<'_>,
    obj_id: u32,
    reason: i32,
    trade_id: u32,
) {
    // TODO (C#) - All reasons.
    let trade_id = if trade_id == 0 {
        get_trade_id(trades, obj_id)
    } else {
        trade_id
    };
    if trade_id == 0 {
        if let Some(p) = character(server, obj_id) {
            p.send(SCTradeCanceledPacket {
                reason,
                caused_by_me: true,
            });
        }
        return;
    }
    let Some(idx) = trades.iter().position(|t| t.id == trade_id) else {
        // C#: `_trades[tradeId]` throws KeyNotFoundException.
        return;
    };
    let t = trades.remove(idx);
    send_canceled(server, &t, obj_id, reason);
}

/// The packets of `CancelTrade` once the entry is removed. A missing partner makes the C#
/// throw (NullReferenceException in the log line) after the removal: nothing is sent.
fn send_canceled(server: &GameServer, t: &TradeTemplate, obj_id: u32, reason: i32) {
    let (Some(owner), Some(target)) = (
        character(server, t.owner_obj_id),
        character(server, t.target_obj_id),
    ) else {
        return;
    };
    tracing::info!(
        "Trade Id:{} between {}({}) - {}({}) is canceled.",
        t.id,
        name_of(&owner),
        owner.obj_id,
        name_of(&target),
        target.obj_id
    );
    let caused_by_me = owner.obj_id == obj_id;
    owner.send(SCTradeCanceledPacket {
        reason,
        caused_by_me,
    });
    target.send(SCTradeCanceledPacket {
        reason,
        caused_by_me: !caused_by_me,
    });
}

/// `UnlockTrade(owner, target, tradeId)`.
fn unlock_trade(t: &mut TradeTemplate, owner: &PlayerRef, target: &PlayerRef) {
    if !t.lock_owner && !t.lock_target {
        return;
    }
    t.lock_owner = false;
    t.lock_target = false;
    t.ok_owner = false;
    t.ok_target = false;
    owner.send(SCTradeLockUpdatePacket {
        my_lock: false,
        other_lock: false,
    });
    target.send(SCTradeLockUpdatePacket {
        my_lock: false,
        other_lock: false,
    });
    tracing::info!("Trade Id:{} Lockers opened and Ok undone.", t.id);
}

/// The two partners of a trade; `None` where the C# would throw on a missing character.
fn partners(server: &GameServer, t: &TradeTemplate) -> Option<(PlayerRef, PlayerRef)> {
    Some((
        character(server, t.owner_obj_id)?,
        character(server, t.target_obj_id)?,
    ))
}

/// `TradeManager.CanStartTrade(owner, target)`.
pub fn can_start_trade(server: &GameServer, owner: &PlayerRef, target: &PlayerRef) {
    let mut trades = manager(server).lock();
    if is_trading(server, &mut trades, owner.obj_id)
        || is_trading(server, &mut trades, target.obj_id)
    {
        return;
    }
    // TODO (C#) - Check faction and others
    tracing::info!(
        "{}({}) is trying to trade with {}({}).",
        name_of(owner),
        owner.obj_id,
        name_of(target),
        target.obj_id
    );
    target.send(SCCanStartTradePacket {
        obj_id: owner.obj_id,
    });
}

/// `TradeManager.StartTrade(owner, target)`.
pub fn start_trade(server: &GameServer, owner: &PlayerRef, target: &PlayerRef) {
    let mut trades = manager(server).lock();
    if is_trading(server, &mut trades, owner.obj_id)
        || is_trading(server, &mut trades, target.obj_id)
    {
        return;
    }
    let next_id = match server.ids.trade.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("{e}");
            return;
        }
    };
    trades.push(TradeTemplate {
        id: next_id,
        owner_obj_id: owner.obj_id,
        target_obj_id: target.obj_id,
        ..TradeTemplate::default()
    });
    tracing::info!(
        "Trade Id:{next_id} started between {}({}) - {}({}).",
        name_of(owner),
        owner.obj_id,
        name_of(target),
        target.obj_id
    );
    owner.send(SCTradeStartedPacket {
        obj_id: target.obj_id,
    });
    target.send(SCTradeStartedPacket {
        obj_id: owner.obj_id,
    });
}

/// `TradeManager.CancelTrade(objId, reason, tradeId = 0)`.
pub fn cancel_trade(server: &GameServer, obj_id: u32, reason: i32, trade_id: u32) {
    let mut trades = manager(server).lock();
    cancel_locked(server, &mut trades, obj_id, reason, trade_id);
}

/// `TradeManager.AddItem(character, slotType, slot, amount)`. The amount is only checked (the
/// whole item changes hands when the trade is made, as in C#).
pub fn add_item(server: &GameServer, character: &PlayerRef, st: u8, slot: u8, amount: i32) {
    let mut trades = manager(server).lock();
    let trade_id = get_trade_id(&trades, character.obj_id);
    let item = Inv::of(character).item_at(st, slot);
    if trade_id == 0 {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    }
    // C#: `item.Count` throws on an empty slot.
    let Some(item) = item else { return };
    if item.count < amount {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    }
    let Some(t) = trade_mut(&mut trades, trade_id) else {
        return;
    };
    let Some((owner, target)) = partners(server, t) else {
        return;
    };
    if t.owner_obj_id == character.obj_id {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) added item ({st}-{slot}) Amount: {amount}.",
            name_of(&owner),
            owner.obj_id
        );
        t.owner_items.push(item.id);
        owner.send(SCTradeItemPutupPacket {
            slot_type: st,
            slot,
            amount,
        });
        target.send(SCOtherTradeItemPutupPacket { item: item.clone() });
    } else {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) added item ({st}-{slot}) Amount: {amount}.",
            name_of(&target),
            target.obj_id
        );
        t.target_items.push(item.id);
        owner.send(SCOtherTradeItemPutupPacket { item: item.clone() });
        target.send(SCTradeItemPutupPacket {
            slot_type: st,
            slot,
            amount,
        });
    }
    // If trade was Locked, unlock both
    unlock_trade(t, &owner, &target);
}

/// `TradeManager.AddMoney(character, moneyAmount)`.
pub fn add_money(server: &GameServer, character: &PlayerRef, money_amount: i32) {
    let mut trades = manager(server).lock();
    let trade_id = get_trade_id(&trades, character.obj_id);
    let money = character.data().money;
    if trade_id == 0 || money < i64::from(money_amount) {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    }
    let Some(t) = trade_mut(&mut trades, trade_id) else {
        return;
    };
    let Some((owner, target)) = partners(server, t) else {
        return;
    };
    if t.owner_obj_id == character.obj_id {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) changed Money: {money_amount}.",
            name_of(&owner),
            owner.obj_id
        );
        t.owner_money_putup = money_amount;
        owner.send(SCTradeMoneyPutupPacket { money_amount });
        target.send(SCOtherTradeMoneyPutupPacket { money_amount });
    } else {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) changed Money: {money_amount}.",
            name_of(&target),
            target.obj_id
        );
        t.target_money_putup = money_amount;
        owner.send(SCOtherTradeMoneyPutupPacket { money_amount });
        target.send(SCTradeMoneyPutupPacket { money_amount });
    }
    // If trade was Locked, unlock both
    unlock_trade(t, &owner, &target);
}

/// `List.Remove(item)` / `Clear()` of `RemoveItem`.
fn take_down(list: &mut Vec<u64>, item_id: u64) {
    if list.len() <= 1 {
        list.clear();
    } else if let Some(i) = list.iter().position(|&x| x == item_id) {
        list.remove(i);
    }
}

/// `TradeManager.RemoveItem(character, slotType, slot)`.
pub fn remove_item(server: &GameServer, character: &PlayerRef, st: u8, slot: u8) {
    let mut trades = manager(server).lock();
    let trade_id = get_trade_id(&trades, character.obj_id);
    let item = Inv::of(character).item_at(st, slot);
    let Some(item) = item.filter(|_| trade_id != 0) else {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    };
    let Some(t) = trade_mut(&mut trades, trade_id) else {
        return;
    };
    let Some((owner, target)) = partners(server, t) else {
        return;
    };
    if t.owner_obj_id == character.obj_id {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) tookdown item ({st}-{slot}).",
            name_of(&owner),
            owner.obj_id
        );
        take_down(&mut t.owner_items, item.id);
        owner.send(SCTradeItemTookdownPacket {
            slot_type: st,
            slot,
        });
        target.send(SCOtherTradeItemTookdownPacket { item: item.clone() });
    } else {
        tracing::info!(
            "Trade Id:{trade_id} {}({}) tookdown item ({st}-{slot}).",
            name_of(&target),
            target.obj_id
        );
        take_down(&mut t.target_items, item.id);
        owner.send(SCOtherTradeItemTookdownPacket { item: item.clone() });
        target.send(SCTradeItemTookdownPacket {
            slot_type: st,
            slot,
        });
    }
    // If trade was Locked, unlock both
    unlock_trade(t, &owner, &target);
}

/// `TradeManager.LockTrade(character, lock)`.
pub fn lock_trade(server: &GameServer, character: &PlayerRef, lock: bool) {
    let mut trades = manager(server).lock();
    let trade_id = get_trade_id(&trades, character.obj_id);
    if trade_id == 0 {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    }
    let Some(t) = trade_mut(&mut trades, trade_id) else {
        return;
    };
    let is_owner = t.owner_obj_id == character.obj_id;
    // Check if already locked
    if is_owner && t.lock_owner && lock {
        return;
    }
    if !is_owner && t.lock_target && lock {
        return;
    }
    let Some((owner, target)) = partners(server, t) else {
        return;
    };
    if !lock {
        t.lock_owner = false;
        t.lock_target = false;
        tracing::info!(
            "Trade Id:{trade_id} {}({}) - {}({}) unlocked trade.",
            name_of(&owner),
            owner.obj_id,
            name_of(&target),
            target.obj_id
        );
    } else if is_owner {
        t.lock_owner = true;
        tracing::info!(
            "Trade Id:{trade_id} {}({}) locked trade.",
            name_of(&owner),
            owner.obj_id
        );
    } else {
        t.lock_target = true;
        tracing::info!(
            "Trade Id:{trade_id} {}({}) locked trade.",
            name_of(&target),
            target.obj_id
        );
    }
    owner.send(SCTradeLockUpdatePacket {
        my_lock: t.lock_owner,
        other_lock: t.lock_target,
    });
    target.send(SCTradeLockUpdatePacket {
        my_lock: t.lock_target,
        other_lock: t.lock_owner,
    });
}

/// `TradeManager.OkTrade(character)`.
pub fn ok_trade(server: &GameServer, character: &PlayerRef) {
    let mut trades = manager(server).lock();
    let trade_id = get_trade_id(&trades, character.obj_id);
    if trade_id == 0 {
        cancel_locked(server, &mut trades, character.obj_id, 0, trade_id); // TODO (C#) - Reason
        return;
    }
    let Some(t) = trade_mut(&mut trades, trade_id) else {
        return;
    };
    let is_owner = t.owner_obj_id == character.obj_id;
    // Check if both locked (the C# only requires one of them)
    if !t.lock_owner && !t.lock_target {
        return;
    }
    let Some((owner, target)) = partners(server, t) else {
        return;
    };
    if is_owner {
        t.ok_owner = true;
        tracing::info!(
            "Trade Id:{trade_id} {}({}) ok trade.",
            name_of(&owner),
            owner.obj_id
        );
    } else {
        t.ok_target = true;
        tracing::info!(
            "Trade Id:{trade_id} {}({}) ok trade.",
            name_of(&target),
            target.obj_id
        );
    }
    // Send ok status
    owner.send(SCTradeOkUpdatePacket {
        my_ok: t.ok_owner,
        other_ok: t.ok_target,
    });
    target.send(SCTradeOkUpdatePacket {
        my_ok: t.ok_target,
        other_ok: t.ok_owner,
    });

    // If both ok finish trade
    if !(t.ok_owner && t.ok_target) {
        return;
    }
    // Check inventory space. The C# cancels and then throws (KeyNotFoundException) on the
    // removed entry in the next check / FinishTrade: one cancel, no trade.
    let (owner_items, target_items) = (t.owner_items.len(), t.target_items.len());
    if (Inv::of(&owner).free_slot_count(slot_type::INVENTORY) as i64) < target_items as i64 {
        cancel_locked(server, &mut trades, owner.obj_id, 0, trade_id);
        return;
    }
    if (Inv::of(&target).free_slot_count(slot_type::INVENTORY) as i64) < owner_items as i64 {
        cancel_locked(server, &mut trades, target.obj_id, 0, trade_id);
        return;
    }
    // Finish trade (the entry leaves the table before the item operation starts)
    let Some(idx) = trades.iter().position(|t| t.id == trade_id) else {
        return;
    };
    let t = trades.remove(idx);
    drop(trades);
    finish_trade(server, &owner, &target, t);
}

/// `TradeManager.FinishTrade(owner, target, tradeId)`; `trade` was already taken out of the
/// table.
pub fn finish_trade(
    server: &GameServer,
    owner: &PlayerRef,
    target: &PlayerRef,
    trade: TradeTemplate,
) {
    let trade_id = trade.id;
    // Validate Money (custom client protection)
    let owner_money = owner.data().money;
    if i64::from(trade.owner_money_putup) > owner_money {
        send_canceled(server, &trade, owner.obj_id, 0); // Reason?
        let c = owner.data();
        tracing::error!(
            "{} ({}) is putting up more money for trade than have {} > {}, possible exploit or modified client!",
            c.unit.name,
            c.id,
            trade.owner_money_putup,
            c.money
        );
        return;
    }
    let target_money = target.data().money;
    if i64::from(trade.target_money_putup) > target_money {
        send_canceled(server, &trade, target.obj_id, 0); // Reason?
        let c = target.data();
        tracing::error!(
            "{} ({}) is putting up more money for trade than have {} > {}, possible exploit or modified client!",
            c.unit.name,
            c.id,
            trade.target_money_putup,
            c.money
        );
        return;
    }

    let mut has_errors = 0;
    let mut tasks_owner = Vec::new();
    let mut tasks_target = Vec::new();
    let mut txn = ItemTxn::begin(server);

    // Handle Money from Owner
    if trade.owner_money_putup > 0 {
        owner.data().money -= i64::from(trade.owner_money_putup);
        tasks_owner.push(ItemTask::MoneyChange(-trade.owner_money_putup));
        target.data().money += i64::from(trade.owner_money_putup);
        tasks_target.push(ItemTask::MoneyChange(trade.owner_money_putup));
    }
    // Handle Money from Target
    if trade.target_money_putup > 0 {
        owner.data().money += i64::from(trade.target_money_putup);
        tasks_owner.push(ItemTask::MoneyChange(trade.target_money_putup));
        target.data().money -= i64::from(trade.target_money_putup);
        tasks_target.push(ItemTask::MoneyChange(-trade.target_money_putup));
    }

    // Handle Items from Owner
    for &item_id in &trade.owner_items {
        match move_to_bag(&mut txn, owner, target, item_id) {
            Some(item) => {
                tasks_owner.push(ItemTask::remove(&item));
                tasks_target.push(ItemTask::add(&item));
            }
            None => has_errors += 1,
        }
    }
    // Handle Items from Target
    for &item_id in &trade.target_items {
        match move_to_bag(&mut txn, target, owner, item_id) {
            Some(item) => {
                tasks_target.push(ItemTask::remove(&item));
                tasks_owner.push(ItemTask::add(&item));
            }
            None => has_errors += 1,
        }
    }

    // Trade complete, send item task packets
    txn.env.fx.send(
        owner.character_id,
        SCTradeMadePacket {
            action: task_type::TRADE,
            tasks: tasks_owner,
            force_remove: Vec::new(),
        },
    );
    txn.env.fx.send(
        target.character_id,
        SCTradeMadePacket {
            action: task_type::TRADE,
            tasks: tasks_target,
            force_remove: Vec::new(),
        },
    );
    drop(txn);
    let (on, oi) = {
        let c = owner.data();
        (c.unit.name.clone(), c.id)
    };
    let (tn, ti) = {
        let c = target.data();
        (c.unit.name.clone(), c.id)
    };
    tracing::info!(
        "Trade Id:{trade_id} finished. Owner {on} ({oi}) Items/Money: {}/{} <=> Target {tn} ({ti}) Items/Money: {}/{}",
        trade.owner_items.len(),
        trade.owner_money_putup,
        trade.target_items.len(),
        trade.target_money_putup
    );
    if has_errors > 0 {
        tracing::error!(
            "{has_errors}item(s) could not be trade for tradeId: {trade_id} between {on} ({oi}) and {tn} ({ti}), possible exploit or modified client!"
        );
    }
}

/// `to.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, item)` for an item of `from`.
/// Returns the item after the move (the C# builds both `ItemRemove` and `ItemAdd` from the moved
/// object, so the removal names the slot in the receiver's bag — mirrored). An item that is no
/// longer in one of `from`'s containers counts as an error (the C# would add the orphaned
/// object to the bag).
fn move_to_bag(
    txn: &mut ItemTxn<'_>,
    from: &PlayerRef,
    to: &PlayerRef,
    item_id: u64,
) -> Option<Item> {
    let src = Inv::of(from).holder_of_any(item_id)?;
    let bag = Inv::of(to).bag();
    let data = txn.data();
    let (mut s, b) = lock_pair(&src, &bag);
    let mut b = b?; // different players never share a container
    if !b.move_item_from(
        &mut s,
        task_type::INVALID,
        item_id,
        -1,
        data,
        &mut txn.env.fx,
    ) {
        return None;
    }
    b.get_item_by_id(item_id).cloned()
}

// ---------------------------------------------------------------------------------------------
// S->C packets
// ---------------------------------------------------------------------------------------------

/// `SCCanStartTradePacket(objId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCCanStartTradePacket {
    pub obj_id: u32,
}
packet_def!(
    SCCanStartTradePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCanStartTradePacket
);
impl Marshal for SCCanStartTradePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
    }
}

/// `SCCannotStartTradePacket(objId, reason)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCCannotStartTradePacket {
    pub obj_id: u32,
    pub reason: i32,
}
packet_def!(
    SCCannotStartTradePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCannotStartTradePacket
);
impl Marshal for SCCannotStartTradePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_i32(self.reason);
    }
}

/// `SCTradeStartedPacket(objId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeStartedPacket {
    pub obj_id: u32,
}
packet_def!(
    SCTradeStartedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeStartedPacket
);
impl Marshal for SCTradeStartedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
    }
}

/// `SCTradeCanceledPacket(reason, causedByMe)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeCanceledPacket {
    pub reason: i32,
    pub caused_by_me: bool,
}
packet_def!(
    SCTradeCanceledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeCanceledPacket
);
impl Marshal for SCTradeCanceledPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.reason).write_bool(self.caused_by_me);
    }
}

/// `SCTradeItemPutupPacket(slotType, slot, amount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeItemPutupPacket {
    pub slot_type: u8,
    pub slot: u8,
    pub amount: i32,
}
packet_def!(
    SCTradeItemPutupPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeItemPutupPacket
);
impl Marshal for SCTradeItemPutupPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.slot_type) // type
            .write_u8(self.slot) // index
            .write_i32(self.amount); // amount
    }
}

/// `SCOtherTradeItemPutupPacket(item)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCOtherTradeItemPutupPacket {
    pub item: Item,
}
packet_def!(
    SCOtherTradeItemPutupPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCOtherTradeItemPutupPacket
);
impl Marshal for SCOtherTradeItemPutupPacket {
    fn write(&self, s: &mut PacketStream) {
        self.item.write(s);
    }
}

/// `SCTradeItemTookdownPacket(slotType, slot)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeItemTookdownPacket {
    pub slot_type: u8,
    pub slot: u8,
}
packet_def!(
    SCTradeItemTookdownPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeItemTookdownPacket
);
impl Marshal for SCTradeItemTookdownPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.slot_type).write_u8(self.slot);
    }
}

/// `SCOtherTradeItemTookdownPacket(item)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCOtherTradeItemTookdownPacket {
    pub item: Item,
}
packet_def!(
    SCOtherTradeItemTookdownPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCOtherTradeItemTookdownPacket
);
impl Marshal for SCOtherTradeItemTookdownPacket {
    fn write(&self, s: &mut PacketStream) {
        self.item.write(s);
    }
}

/// `SCTradeMoneyPutupPacket(moneyAmount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeMoneyPutupPacket {
    pub money_amount: i32,
}
packet_def!(
    SCTradeMoneyPutupPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeMoneyPutupPacket
);
impl Marshal for SCTradeMoneyPutupPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.money_amount);
    }
}

/// `SCOtherTradeMoneyPutupPacket(moneyAmount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCOtherTradeMoneyPutupPacket {
    pub money_amount: i32,
}
packet_def!(
    SCOtherTradeMoneyPutupPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCOtherTradeMoneyPutupPacket
);
impl Marshal for SCOtherTradeMoneyPutupPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.money_amount);
    }
}

/// `SCTradeLockUpdatePacket(myLock, otherLock)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeLockUpdatePacket {
    pub my_lock: bool,
    pub other_lock: bool,
}
packet_def!(
    SCTradeLockUpdatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeLockUpdatePacket
);
impl Marshal for SCTradeLockUpdatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.my_lock).write_bool(self.other_lock);
    }
}

/// `SCTradeOkUpdatePacket(myOk, otherOk)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTradeOkUpdatePacket {
    pub my_ok: bool,
    pub other_ok: bool,
}
packet_def!(
    SCTradeOkUpdatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeOkUpdatePacket
);
impl Marshal for SCTradeOkUpdatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.my_ok).write_bool(self.other_ok);
    }
}

/// `SCTradeMadePacket(action, tasks, forceRemove)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCTradeMadePacket {
    pub action: u8,
    pub tasks: Vec<ItemTask>,
    pub force_remove: Vec<u64>,
}
packet_def!(
    SCTradeMadePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTradeMadePacket
);
impl Marshal for SCTradeMadePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.action);
        s.write_u8(self.tasks.len() as u8); // TODO (C#) max count 30
        for t in &self.tasks {
            t.write(s);
        }
        s.write_u8(self.force_remove.len() as u8); // TODO (C#) max count 30
        for r in &self.force_remove {
            s.write_u64(*r);
        }
        s.write_u32(0); // type(id)
    }
}

// ---------------------------------------------------------------------------------------------
// C->S packets
// ---------------------------------------------------------------------------------------------

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSStartTradePacket>(LEVEL_PLAIN, cs::CSStartTradePacket);
    reg.register::<CSCanStartTradePacket>(LEVEL_PLAIN, cs::CSCanStartTradePacket);
    reg.register::<CSCannotStartTradePacket>(LEVEL_PLAIN, cs::CSCannotStartTradePacket);
    reg.register::<CSCancelTradePacket>(LEVEL_PLAIN, cs::CSCancelTradePacket);
    reg.register::<CSPutupTradeItemPacket>(LEVEL_PLAIN, cs::CSPutupTradeItemPacket);
    reg.register::<CSPutupTradeMoneyPacket>(LEVEL_PLAIN, cs::CSPutupTradeMoneyPacket);
    reg.register::<CSTakedownTradeItemPacket>(LEVEL_PLAIN, cs::CSTakedownTradeItemPacket);
    reg.register::<CSTradeLockPacket>(LEVEL_PLAIN, cs::CSTradeLockPacket);
    reg.register::<CSTradeOkPacket>(LEVEL_PLAIN, cs::CSTradeOkPacket);
}

/// `CSCanStartTradePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCanStartTradePacket {
    pub obj_id: u32,
}

impl CSCanStartTradePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(target) = character(server, self.obj_id) else {
            return;
        };
        let Some(owner) = active(conn) else { return };
        can_start_trade(server, &owner, &target);
    }
}
sync_client_packet!(CSCanStartTradePacket);

/// `CSCannotStartTradePacket` (logged only, TODO in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCannotStartTradePacket {
    pub obj_id: u32,
    pub reason: i32,
}

impl CSCannotStartTradePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            reason: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!(
            "CannotStartTrade, ObjId: {}, Reason: {}",
            self.obj_id,
            self.reason
        );
        // TODO (C#)
    }
}
sync_client_packet!(CSCannotStartTradePacket);

/// `CSStartTradePacket`: the accepting player (`ActiveChar`) is the trade's target.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSStartTradePacket {
    pub obj_id: u32,
}

impl CSStartTradePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(owner) = character(server, self.obj_id) else {
            return;
        };
        let Some(target) = active(conn) else { return };
        start_trade(server, &owner, &target);
    }
}
sync_client_packet!(CSStartTradePacket);

/// `CSCancelTradePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCancelTradePacket {
    pub reason: i32,
}

impl CSCancelTradePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            reason: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!("CancelTrade, Reason: {}", self.reason);
        let Some(p) = active(conn) else { return };
        cancel_trade(server, p.obj_id, self.reason, 0);
    }
}
sync_client_packet!(CSCancelTradePacket);

/// `CSPutupTradeItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPutupTradeItemPacket {
    pub slot_type: u8,
    pub slot: u8,
    pub amount: i32,
}

impl CSPutupTradeItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
            amount: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        add_item(server, &p, self.slot_type, self.slot, self.amount);
    }
}
sync_client_packet!(CSPutupTradeItemPacket);

/// `CSPutupTradeMoneyPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPutupTradeMoneyPacket {
    pub money_amount: i32,
}

impl CSPutupTradeMoneyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            money_amount: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        add_money(server, &p, self.money_amount);
    }
}
sync_client_packet!(CSPutupTradeMoneyPacket);

/// `CSTakedownTradeItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTakedownTradeItemPacket {
    pub slot_type: u8,
    pub slot: u8,
}

impl CSTakedownTradeItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        remove_item(server, &p, self.slot_type, self.slot);
    }
}
sync_client_packet!(CSTakedownTradeItemPacket);

/// `CSTradeLockPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTradeLockPacket {
    pub lock: bool,
}

impl CSTradeLockPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            lock: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        lock_trade(server, &p, self.lock);
    }
}
sync_client_packet!(CSTradeLockPacket);

/// `CSTradeOkPacket` (empty body).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTradeOkPacket;

impl CSTradeOkPacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        ok_trade(server, &p);
    }
}
sync_client_packet!(CSTradeOkPacket);
