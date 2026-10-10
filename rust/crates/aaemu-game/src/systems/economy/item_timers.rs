//! Item timers: `ItemManager.UpdateItemTimers` / `UpdateItemContainerTimers` (the 1 s
//! `ItemTimerTask` started by `LoadUserItems`: expiration by end time and by online minutes,
//! recharge buff expiry, `LifespanExpiration` removals) and `Inventory.SetInitialItemExpirationTimers`
//! (the `SCSyncItemLifespanPacket`s sent with the inventory at login).

use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::data::items::bind_type;
use crate::models::container::{expire_item, slot_type, ContainerRef, MIN_DATE_TIME};
use crate::models::item::{Item, ITEM_FLAG_UNPACKED};
use crate::models::item_task::task_type;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

use super::inventory::{Inv, ItemTxn};
use super::item_packets::SCSyncItemLifespanPacket;

/// `ItemManager.LastTimerCheck` (+ `ItemTimerLock`).
#[derive(Debug, Default)]
pub struct ItemTimers {
    /// Unix milliseconds of the last check (`None` until [`start`] / the first tick).
    last_check_ms: Mutex<Option<i64>>,
}

impl ItemTimers {
    /// Sets `LastTimerCheck` (C#: `ItemManager.Load`).
    pub fn reset(&self, now_ms: i64) {
        *self.last_check_ms.lock().unwrap_or_else(|e| e.into_inner()) = Some(now_ms);
    }

    /// `delta = now - LastTimerCheck; LastTimerCheck = now` (milliseconds).
    fn take_delta(&self, now_ms: i64) -> i64 {
        let mut g = self.last_check_ms.lock().unwrap_or_else(|e| e.into_inner());
        let delta = now_ms - g.unwrap_or(now_ms);
        *g = Some(now_ms);
        delta
    }
}

/// "Starting Timed Items Task ...": `ItemTimerTask` every second.
pub fn start(server: &Arc<GameServer>) {
    server
        .systems
        .economy
        .item_timers
        .reset(chrono::Utc::now().timestamp_millis());
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut iv = tokio::time::interval(Duration::from_secs(1));
        loop {
            iv.tick().await;
            let Some(server) = weak.upgrade() else { break };
            update_item_timers(&server, chrono::Utc::now().timestamp_millis());
        }
    });
}

/// `ItemManager.UpdateItemTimers()`: only the containers of online characters tick (equipment,
/// bag, warehouse). Returns the number of expired items.
pub fn update_item_timers(server: &GameServer, now_ms: i64) -> i32 {
    let delta_ms = server.systems.economy.item_timers.take_delta(now_ms);
    let delta_minutes = delta_ms as f64 / 60_000.0;
    let mut res = 0;
    for player in server.world.all_characters() {
        let inv = Inv::of(&player);
        for st in [slot_type::EQUIPMENT, slot_type::INVENTORY, slot_type::BANK] {
            match inv.get(st) {
                Some(c) => {
                    res += update_item_container_timers(
                        server,
                        &player,
                        &c,
                        st == slot_type::EQUIPMENT,
                        delta_minutes,
                        now_ms,
                    )
                }
                None => tracing::error!("Invalid itemContainer when processing item timers"),
            }
        }
    }
    if res > 0 {
        tracing::warn!("{res} item(s) expired and have been removed.");
    }
    res
}

/// `ItemManager.UpdateItemContainerTimers(delta, container, character)`.
pub fn update_item_container_timers(
    server: &GameServer,
    player: &PlayerRef,
    container: &ContainerRef,
    is_equipment: bool,
    delta_minutes: f64,
    now_ms: i64,
) -> i32 {
    let now = now_ms.div_euclid(1000);
    let data = &server.game_data.items;
    let mut res = 0;
    let mut expired_buffs = Vec::new();
    {
        let mut txn = ItemTxn::begin(server);
        let mut c = container.lock().expect("container poisoned");
        let owner = c.owner_id;
        for i in (0..c.items.len()).rev() {
            let item = &mut c.items[i];
            // Check if buffs need to expire
            if is_equipment && item.is_equip() {
                if let Some(e) = data
                    .template(item.template_id)
                    .and_then(|t| t.equip().map(|e| (t, e)))
                    .filter(|(_, e)| e.recharge_buff_id > 0)
                {
                    let (t, e) = e;
                    let unpack = t.bind_type == bind_type::BIND_ON_UNPACK;
                    let expire_check = if unpack {
                        item.unpack_time
                    } else {
                        item.charge_start_time
                    } + i64::from(e.charge_lifetime) * 60;
                    let mut check_charges = e.charge_count > 0;
                    if unpack && !item.has_flag(ITEM_FLAG_UNPACKED) {
                        check_charges = false;
                    }
                    let mut expire_buff = false;
                    if e.charge_lifetime > 0 && expire_check <= now {
                        expire_buff = true;
                    }
                    if check_charges && e.charge_count > 0 && item.charge_count <= 0 {
                        expire_buff = true;
                    }
                    if expire_buff {
                        expired_buffs.push(e.recharge_buff_id);
                    }
                }
            }
            // Check if item itself needs to be expired
            let mut do_expire = false;
            if item.expiration_time > MIN_DATE_TIME && item.expiration_time <= now {
                do_expire = true; // Item expired by predefined end time
            } else if item.expiration_online_minutes_left > 0.0 {
                item.expiration_online_minutes_left -= delta_minutes;
                item.dirty = true;
                if item.expiration_online_minutes_left <= 0.0 {
                    do_expire = true; // online timed lifespan is done
                }
            }
            if do_expire {
                res += 1;
                let id = item.id;
                txn.env
                    .fx
                    .push(expire_item(item).with_owner(player.character_id));
                c.remove_item_task(task_type::LIFESPAN_EXPIRATION, id, true, &mut txn.env);
            }
        }
        let _ = owner;
    }
    // Apply the "expire" buff if needed (character.Buffs.CheckBuff / RemoveBuff)
    if !expired_buffs.is_empty() {
        {
            let mut ch = player.data();
            for b in expired_buffs {
                if ch.unit.buffs.check_buff(b) {
                    ch.unit.buffs.remove_buff(b);
                }
            }
        }
        crate::systems::dispatch_buff_events(server, &WorldObject::from(player));
    }
    res
}

/// `Inventory.SetInitialItemExpirationTimers(items)`: the lifespan packets for `items` (call
/// after the bag contents, after the bank contents, then with the equipment, as
/// `Inventory.Send` does).
pub fn initial_item_expiration_packets<'a>(
    items: impl IntoIterator<Item = Option<&'a Item>>,
    now_ms: i64,
) -> Vec<SCSyncItemLifespanPacket> {
    let mut out = Vec::new();
    for item in items.into_iter().flatten() {
        if item.expiration_time > MIN_DATE_TIME {
            out.push(SCSyncItemLifespanPacket {
                added: true,
                item_id: item.id,
                template_id: item.template_id,
                expire_time: item.expiration_time,
            });
        }
        if item.expiration_online_minutes_left > 0.0 {
            // DateTime.UtcNow.AddMinutes(minutes), truncated to unix seconds
            let t = (now_ms as f64 / 1000.0 + item.expiration_online_minutes_left * 60.0).floor();
            out.push(SCSyncItemLifespanPacket {
                added: true,
                item_id: item.id,
                template_id: item.template_id,
                expire_time: t as i64,
            });
        }
    }
    out
}

/// Sends [`initial_item_expiration_packets`] of one container of `player` (Equipment / Inventory /
/// Bank) straight to its connection.
pub fn send_initial_item_expiration_timers(player: &PlayerRef, st: u8, now_ms: i64) {
    let Some(c) = Inv::of(player).get(st) else {
        return;
    };
    // Bag / bank: `GetSlottedItemsList()`; equipment: `Items`.
    let items: Vec<Option<Item>> = {
        let g = c.lock().expect("container poisoned");
        if st == slot_type::EQUIPMENT {
            g.items.iter().cloned().map(Some).collect()
        } else {
            g.slotted_items()
        }
    };
    for p in initial_item_expiration_packets(items.iter().map(Option::as_ref), now_ms) {
        player.send(p);
    }
}
