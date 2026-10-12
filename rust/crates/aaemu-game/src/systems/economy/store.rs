//! Stores and merchants: `CSBuyItemsPacket`, `CSSellItemsPacket`, the buy-back container
//! (`Character.BuyBackItems`), `CSListSoldItemPacket` / `SCSoldItemListPacket`,
//! `CSBuyPriestBuffPacket`, `CSBuyCoinItemPacket`, `CSSellBackpackGoodsPacket`,
//! `CSExpandExpertPacket` / `SCExpertExpandedPacket` (`CharacterActability.ExpandExpert`),
//! `CSChangeItemLookPacket` and `CSConvertItemLookPacket`.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use crate::error::Result;
use crate::models::container::{slot_type, ContainerKind, ContainerRef, ItemContainer};
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemFx, ItemTask};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{DoodadRef, NpcRef, PlayerRef, WorldInstance, WorldObject};

use super::craft::distance_2d;
use super::error_message;
use super::inventory::{self, game_point_kind, lock_pair, Inv, ItemTxn};
use super::item_handlers::{active, sync_client_packet};
use super::item_packets::{SCGamePointChangedPacket, SCItemTaskSuccessPacket};

/// Discard a failed staged checkout while releasing only newly allocated IDs.
fn discard_shop_stage(
    txn: &mut ItemTxn<'_>,
    staged: &ItemContainer,
    bag: &ItemContainer,
    buyback: &ItemContainer,
) {
    let original = |id| bag.get_item_by_id(id).is_some() || buyback.get_item_by_id(id).is_some();
    let mut release: Vec<u64> = staged
        .items
        .iter()
        .map(|i| i.id)
        .filter(|id| !original(*id))
        .collect();
    for fx in txn.env.take_fx().0 {
        if let ItemFx::ReleaseId(id) = fx {
            if !original(id) {
                release.push(id);
            }
        }
    }
    release.sort_unstable();
    release.dedup();
    for id in release {
        txn.env.fx.push(ItemFx::ReleaseId(id));
    }
}

fn checked_shop_refund(server: &GameServer, item: &Item) -> Option<i32> {
    let template = server.game_data.items.template(item.template_id)?;
    let multiplier = server
        .game_data
        .systems
        .economy
        .store
        .refund_multiplier(item.grade)?;
    if template.refund < 0 || multiplier < 0 || item.count <= 0 {
        return None;
    }
    // Preserve grade refund rounding, widening the multiplication before it.
    let per = (i64::from(template.refund) * i64::from(multiplier)) as f32 / 100.0;
    i32::try_from((per as i64).checked_mul(i64::from(item.count))?).ok()
}

/// `ShopCurrencyType`.
pub mod shop_currency_type {
    pub const MONEY: u8 = 0;
    pub const HONOR: u8 = 1;
    pub const VOCATION_BADGES: u8 = 2;
    pub const SIEGE_SHOP: u8 = 3;
}

/// The arguments of `new MailForSpeciality(seller, crafterId, tradepackTemplate, tradeRate,
/// itemRewardTemplateId, itemCountBase, itemCountBonus, itemCountForSeller,
/// itemCountForCrafter, interestRate)` + which `Finalize*` runs.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SpecialtyMail {
    pub seller_character_id: u32,
    pub crafter_id: u32,
    pub trade_pack_template_id: u32,
    pub trade_rate: i32,
    pub item_reward_template_id: u32,
    pub item_count_base: i32,
    pub item_count_bonus: i32,
    pub item_count_for_seller: i32,
    pub item_count_for_crafter: i32,
    pub interest_rate: i32,
    /// `FinalizeForCrafter()` (else `FinalizeForSeller()`).
    pub for_crafter: bool,
}

/// What the stores need from other subsystems.
pub trait StoreHooks: Send + Sync {
    /// npcs: `npc.Template.SpecialtyCoinId`.
    fn npc_specialty_coin_id(&self, _server: &GameServer, _npc: &NpcRef) -> u32 {
        0
    }

    /// mail: `new MailForSpeciality(...)`, `FinalizeForSeller()` / `FinalizeForCrafter()`,
    /// `Send()`. Default `false` (no mail system: the sale is refused with
    /// `MailUnknownFailure` instead of destroying the pack without a payout).
    fn send_specialty_mail(
        &self,
        _server: &GameServer,
        _seller: &PlayerRef,
        _mail: &SpecialtyMail,
    ) -> bool {
        tracing::warn!("StoreHooks::send_specialty_mail is not installed (mail subsystem)");
        false
    }

    /// npcs: the rest of `CSBuyCoinItemPacket` after the doodad lookup:
    /// `DoodadManager.GetFunc(id)`, `GetFuncTemplate(func.FuncId, func.FuncType)`,
    /// `funcTemplate.Use(character, doodad, 0)` (with the C# warnings when they are missing).
    fn buy_coin_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        doodad: &DoodadRef,
        id: u32,
    ) {
        tracing::warn!(
            "BuyCoinItem, no doodadFunc for objId: {}, id: {id}",
            doodad.obj_id
        );
    }
}

struct NoHooks;
impl StoreHooks for NoHooks {}

/// Store state (`server.systems.economy.store`).
#[derive(Debug, Default)]
pub struct StoreManager {
    /// `Character.BuyBackItems` by character id (`new ItemContainer(Id, SlotType.None, false)`,
    /// not persisted).
    buyback: Mutex<HashMap<u32, ContainerRef>>,
    pub hooks: HookSlot<dyn StoreHooks>,
}

impl StoreManager {
    /// `Character.BuyBackItems` (created on first use, like `Character.Load`).
    pub fn buyback(&self, character_id: u32) -> ContainerRef {
        self.buyback
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .entry(character_id)
            .or_insert_with(|| {
                ItemContainer::new(ContainerKind::Item, character_id, slot_type::NONE, 0).into_ref()
            })
            .clone()
    }

    pub fn hooks(&self) -> Arc<dyn StoreHooks> {
        self.hooks.get().unwrap_or_else(|| Arc::new(NoHooks))
    }

    /// `BuyBackItems.Wipe()` (`EnterWorldManager` leave-world / `ShutdownTask`): call when the
    /// character leaves the world.
    pub fn wipe_buyback(&self, server: &GameServer, character_id: u32) {
        let c = self.buyback(character_id);
        let mut txn = ItemTxn::begin(server);
        c.lock().expect("container poisoned").wipe(&mut txn.env);
    }
}

pub fn register(reg: &mut PacketRegistry) {
    // Not registered in the 1.2 `GameNetwork` (no opcode): `CSChangeItemLookPacket`.
    reg.register::<CSBuyItemsPacket>(LEVEL_PLAIN, cs::CSBuyItemsPacket);
    reg.register::<CSSellItemsPacket>(LEVEL_PLAIN, cs::CSSellItemsPacket);
    reg.register::<CSListSoldItemPacket>(LEVEL_PLAIN, cs::CSListSoldItemPacket);
    reg.register::<CSBuyPriestBuffPacket>(LEVEL_PLAIN, cs::CSBuyPriestBuffPacket);
    reg.register::<CSBuyCoinItemPacket>(LEVEL_PLAIN, cs::CSBuyCoinItemPacket);
    reg.register::<CSSellBackpackGoodsPacket>(LEVEL_PLAIN, cs::CSSellBackpackGoodsPacket);
    reg.register::<CSExpandExpertPacket>(LEVEL_PLAIN, cs::CSExpandExpertPacket);
    reg.register::<CSConvertItemLookPacket>(LEVEL_PLAIN, cs::CSConvertItemLookPacket);
}

/// `Connection.ActiveChar.ParentWorld`.
pub fn parent_world(server: &GameServer, p: &PlayerRef) -> Option<Arc<WorldInstance>> {
    server.world.world_of(&WorldObject::from(p))
}

/// `MathUtil.CalculateDistance(a.Transform.World.Position, b.Transform.World.Position)`.
pub fn world_distance(world: &WorldInstance, a: &WorldObject, b: &WorldObject) -> f32 {
    let pa = world.world_pos_rot(a);
    let pb = world.world_pos_rot(b);
    distance_2d(pa.x, pa.y, pb.x, pb.y)
}

/// `npc.Template.Merchant` / `MerchantPackId` through the NPC hooks (unknown = not a merchant).
fn shop_info(server: &GameServer, npc: &NpcRef) -> super::hooks::NpcShopInfo {
    server
        .systems
        .economy
        .hooks
        .npcs
        .get()
        .and_then(|h| h.shop_info(server, npc))
        .unwrap_or_default()
}

/// `(int)(item.Template.Refund * GetGradeTemplate(item.Grade).RefundMultiplier / 100f) *
/// item.Count` (`None` where the C# dereferences a missing template).
pub fn refund_value(server: &GameServer, item: &Item) -> Option<i32> {
    let t = server.game_data.items.template(item.template_id)?;
    let mul = server
        .game_data
        .systems
        .economy
        .store
        .refund_multiplier(item.grade)?;
    let per = (t.refund.wrapping_mul(mul) as f32 / 100f32) as i32;
    Some(per.wrapping_mul(item.count))
}

// -------------------------------------------------------------------------------------------
// CSBuyItemsPacket
// -------------------------------------------------------------------------------------------

/// One `(itemId, grade, count, currency)` entry of `CSBuyItemsPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BuyEntry {
    pub item_id: u32,
    pub grade: u8,
    pub count: i32,
    pub currency: u8,
}

/// `CSBuyItemsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSBuyItemsPacket {
    pub npc_obj_id: u32,
    pub doodad_obj_id: u32,
    pub unk_id: u32,
    pub buy: Vec<BuyEntry>,
    pub buy_back: Vec<i32>,
    pub use_aa_point: bool,
}

impl CSBuyItemsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        let npc_obj_id = s.read_bc()?;
        let doodad_obj_id = s.read_bc()?;
        let unk_id = s.read_u32()?; // type(id)?
        let n_buy = s.read_u8()?;
        let n_buy_back = s.read_u8()?;
        let mut buy = Vec::with_capacity(usize::from(n_buy));
        for _ in 0..n_buy {
            buy.push(BuyEntry {
                item_id: s.read_u32()?,
                grade: s.read_u8()?,
                count: s.read_i32()?,
                currency: s.read_u8()?,
            });
        }
        let mut buy_back = Vec::with_capacity(usize::from(n_buy_back));
        for _ in 0..n_buy_back {
            buy_back.push(s.read_i32()?);
        }
        Ok(Self {
            npc_obj_id,
            doodad_obj_id,
            unk_id,
            buy,
            buy_back,
            use_aa_point: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let Some(world) = parent_world(server, &p) else {
            return;
        };
        let npc = world.npc(self.npc_obj_id);
        let doodad = world.doodad(self.doodad_obj_id);
        tracing::debug!(
            "NPCObjId:{} DoodadObjId:{} unkId:{} nBuy:{} nBuyBack{}",
            self.npc_obj_id,
            self.doodad_obj_id,
            self.unk_id,
            self.buy.len(),
            self.buy_back.len()
        );

        // If a NPC was provided, check if it's valid
        let mut pack_id = None;
        if self.npc_obj_id != 0 {
            let Some(n) = &npc else { return };
            let info = shop_info(server, n);
            if !info.merchant || info.merchant_pack_id == 0 {
                return;
            }
        }
        if let Some(n) = &npc {
            let dist = world_distance(&world, &WorldObject::from(&p), &WorldObject::from(n));
            if dist > 3.0 {
                // 3m should be enough for NPC shops
                inventory::send_error_message(&p, error_message::TOO_FAR_AWAY, 0, true);
                return;
            }
            pack_id = Some(shop_info(server, n).merchant_pack_id);
        }

        // If a Doodad was provided, check if we're near it
        if self.doodad_obj_id != 0 {
            let Some(d) = &doodad else { return };
            let dist = world_distance(&world, &WorldObject::from(&p), &WorldObject::from(d));
            if dist > 3.0 {
                // 3m should be enough for these
                inventory::send_error_message(&p, error_message::TOO_FAR_AWAY, 0, true);
                return;
            }
        }

        let npc_hooks = server.systems.economy.hooks.npcs.get();
        let mut money = 0i32;
        let mut honor_points = 0i32;
        let mut vocation_badges = 0i32;
        let mut items_buy = Vec::new();
        for e in &self.buy {
            if e.count <= 0
                || !matches!(
                    e.currency,
                    shop_currency_type::MONEY
                        | shop_currency_type::HONOR
                        | shop_currency_type::VOCATION_BADGES
                )
            {
                return;
            }
            if self.npc_obj_id != 0 {
                let sells = match (pack_id, &npc_hooks) {
                    (Some(pack), Some(h)) => h.merchant_sells(server, pack, e.item_id),
                    _ => false,
                };
                if !sells {
                    continue;
                }
            }
            // Doodad catalog policy remains the existing separate limitation.
            let Some(template) = server.game_data.items.template(e.item_id) else {
                return;
            };
            if template.max_count <= 0 {
                return;
            }
            let (price, total) = match e.currency {
                shop_currency_type::MONEY => (template.price, &mut money),
                shop_currency_type::HONOR => (template.honor_price, &mut honor_points),
                shop_currency_type::VOCATION_BADGES => {
                    (template.living_point_price, &mut vocation_badges)
                }
                _ => return,
            };
            // A zero non-money price is an unpriced currency, not a free alternative.
            if price < 0 || (price == 0 && e.currency != shop_currency_type::MONEY) {
                return;
            }
            let Some(cost) = price.checked_mul(e.count) else {
                return;
            };
            let Some(sum) = total.checked_add(cost) else {
                return;
            };
            *total = sum;
            items_buy.push((e.item_id, e.count));
        }

        let inv = Inv::of(&p);
        let bag_ref = inv.bag();
        let buyback_ref = server.systems.economy.store.buyback(p.character_id);
        let mut txn = ItemTxn::begin(server);
        // Character before containers; all item effects remain queued until commit.
        let mut character = p.data();
        let (mut bag, source) = lock_pair(&bag_ref, &buyback_ref);
        let Some(mut buyback) = source else { return };
        let mut items_buy_back = Vec::new();
        for &index in &self.buy_back {
            let Some(item) = buyback.get_item_by_slot(index) else {
                continue;
            };
            if items_buy_back.contains(&item.id) {
                return;
            }
            let Some(value) = checked_shop_refund(server, item) else {
                return;
            };
            let Some(sum) = money.checked_add(value) else {
                return;
            };
            money = sum;
            items_buy_back.push(item.id);
        }
        if i64::from(money) > character.money
            || honor_points > character.honor_point
            || vocation_badges > character.vocation_point
        {
            return;
        }
        let Some(next_money) = character.money.checked_sub(i64::from(money)) else {
            return;
        };
        let Some(next_honor) = character.honor_point.checked_sub(honor_points) else {
            return;
        };
        // Stock 1.2 store/cmd.lua displays livingPointPrice * quantity and
        // subtracts that exact total from the current balance. Point-gain
        // bonuses belong to earnings, not the configured shop price.
        let vocation_change = -vocation_badges;
        let Some(next_vocation) = character.vocation_point.checked_add(vocation_change) else {
            return;
        };
        if next_vocation < 0 {
            return;
        }

        let mut staged_bag = bag.clone();
        let mut staged_buyback = buyback.clone();
        let mut succeeded = true;
        for (item_id, count) in items_buy {
            let template = txn.data().template(item_id).expect("preflight template");
            let existing: Vec<_> = staged_bag
                .items
                .iter()
                .filter(|i| i.template_id == item_id)
                .collect();
            let units: i64 = existing.iter().map(|i| i64::from(i.count)).sum();
            let capacity = (existing.len() as i64 + i64::from(staged_bag.free_slot_count()))
                * i64::from(template.max_count)
                - units;
            // The existing container acquisition uses i32 space arithmetic.
            if capacity < i64::from(count)
                || capacity > i64::from(i32::MAX)
                || !staged_bag.acquire_default_item(
                    task_type::STORE_BUY,
                    item_id,
                    count,
                    -1,
                    0,
                    &mut txn.env,
                )
            {
                succeeded = false;
                break;
            }
        }
        let mut tasks = Vec::new();
        if succeeded {
            for item_id in items_buy_back {
                if !staged_bag.move_item_from(
                    &mut staged_buyback,
                    task_type::STORE_BUY,
                    item_id,
                    -1,
                    txn.data(),
                    &mut txn.env.fx,
                ) {
                    succeeded = false;
                    break;
                }
                if let Some(item) = staged_bag.get_item_by_id(item_id) {
                    tasks.push(ItemTask::buyback(item));
                }
            }
        }
        if !succeeded {
            discard_shop_stage(&mut txn, &staged_bag, &bag, &buyback);
            return;
        }
        *bag = staged_bag;
        *buyback = staged_buyback;
        character.money = next_money;
        character.honor_point = next_honor;
        character.vocation_point = next_vocation;
        drop(buyback);
        drop(bag);
        drop(character);
        let mut out = txn.out();
        if honor_points > 0 {
            out.send(
                &p,
                SCGamePointChangedPacket {
                    kind: game_point_kind::HONOR,
                    amount: -honor_points,
                },
            );
        }
        if vocation_badges > 0 {
            out.send(
                &p,
                SCGamePointChangedPacket {
                    kind: game_point_kind::VOCATION,
                    amount: vocation_change,
                },
            );
        }
        if money > 0 {
            out.send(
                &p,
                SCItemTaskSuccessPacket {
                    action: task_type::DEPOSIT_MONEY,
                    tasks: vec![ItemTask::MoneyChange(-money)],
                    force_remove: Vec::new(),
                },
            );
        }
        out.send(
            &p,
            SCItemTaskSuccessPacket {
                action: task_type::STORE_BUY,
                tasks,
                force_remove: Vec::new(),
            },
        );
    }
}
sync_client_packet!(CSBuyItemsPacket);

// -------------------------------------------------------------------------------------------
// CSSellItemsPacket
// -------------------------------------------------------------------------------------------

/// One `(slotType, slot, itemId, unkId)` entry of `CSSellItemsPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SellEntry {
    pub slot_type: u8,
    pub slot: u8,
    pub item_id: u64,
    pub unk_id: u32,
}

/// `CSSellItemsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSellItemsPacket {
    pub npc_obj_id: u32,
    pub unk_obj_id: u32,
    pub items: Vec<SellEntry>,
}

impl CSSellItemsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        let npc_obj_id = s.read_bc()?;
        let unk_obj_id = s.read_bc()?;
        let num = s.read_u8()?;
        let mut items = Vec::with_capacity(usize::from(num));
        for _ in 0..num {
            items.push(SellEntry {
                slot_type: s.read_u8()?,
                slot: s.read_u8()?,
                item_id: s.read_u64()?,
                unk_id: s.read_u32()?,
            });
        }
        Ok(Self {
            npc_obj_id,
            unk_obj_id,
            items,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let Some(world) = parent_world(server, &p) else {
            return;
        };
        let Some(npc) = world.npc(self.npc_obj_id) else {
            return;
        };
        if !shop_info(server, &npc).merchant {
            return;
        }
        let inv = Inv::of(&p);
        let mut items = Vec::new();
        for e in &self.items {
            let item = match e.slot_type {
                slot_type::EQUIPMENT | slot_type::INVENTORY => inv.item_at(e.slot_type, e.slot),
                _ => None,
            };
            if let Some(item) = item.filter(|i| i.id == e.item_id) {
                items.push(item);
            }
        }

        let buyback = server.systems.economy.store.buyback(p.character_id);
        let containers = [inv.equipment(), inv.bag()];
        let mut txn = ItemTxn::begin(server);
        let data = txn.data();
        let mut money = 0i32;
        for item in &items {
            if !data.template(item.template_id).is_some_and(|t| t.sellable) {
                continue;
            }
            // The container that holds the item now (a slot listed twice: the buy-back).
            let holder = containers
                .iter()
                .find(|c| {
                    c.lock()
                        .expect("container poisoned")
                        .get_item_by_id(item.id)
                        .is_some()
                })
                .cloned();
            let moved = match holder {
                Some(src) => {
                    let (mut b, src) = lock_pair(&buyback, &src);
                    match src {
                        Some(mut src) => b.move_item_from(
                            &mut src,
                            task_type::STORE_SELL,
                            item.id,
                            -1,
                            data,
                            &mut txn.env.fx,
                        ),
                        None => false,
                    }
                }
                None => buyback.lock().expect("container poisoned").relocate_item(
                    task_type::STORE_SELL,
                    item.id,
                    -1,
                    data,
                    &mut txn.env.fx,
                ),
            };
            if !moved {
                tracing::warn!(
                    "Failed to move sold itemId {} ({}) to BuyBack ItemContainer for {}",
                    item.id,
                    item.template_id,
                    p.data().unit.name
                );
            } else {
                // BuyBack is a non-persisted (SlotType.None) container. Without this the
                // item's existing persisted row would be reloaded and duplicated on relogin
                // (#1189). Queue that row for deletion; buying the item back re-enters a
                // persisted container and re-saves it.
                server.items.mark_for_db_deletion(item.id);
            }
            let Some(v) = refund_value(server, item) else {
                tracing::error!("CSSellItemsPacket: no refund data for item {}", item.id);
                return;
            };
            money = money.wrapping_add(v);
        }

        let mut out = txn.out();
        inventory::change_money_at(
            &mut out,
            &p,
            slot_type::INVENTORY,
            money,
            task_type::DEPOSIT_MONEY,
        );
    }
}
sync_client_packet!(CSSellItemsPacket);

// -------------------------------------------------------------------------------------------
// CSListSoldItemPacket / SCSoldItemListPacket
// -------------------------------------------------------------------------------------------

/// `CSListSoldItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSListSoldItemPacket {
    pub npc_obj_id: u32,
}

impl CSListSoldItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            npc_obj_id: s.read_bc()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let Some(world) = parent_world(server, &p) else {
            return;
        };
        let Some(npc) = world.npc(self.npc_obj_id) else {
            return;
        };
        if !shop_info(server, &npc).merchant {
            return;
        }
        let buyback = server.systems.economy.store.buyback(p.character_id);
        let items = {
            let mut b = buyback.lock().expect("container poisoned");
            b.renumber_slots(false);
            b.items.clone()
        };
        p.send(SCSoldItemListPacket::new(items));
    }
}
sync_client_packet!(CSListSoldItemPacket);

/// `SCSoldItemListPacket(items)` (the first 12 items of `BuyBackItems.Items`).
#[derive(Debug, Clone, PartialEq)]
pub struct SCSoldItemListPacket {
    pub items: Vec<Item>,
}
packet_def!(
    SCSoldItemListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSoldItemListPacket
);
impl SCSoldItemListPacket {
    pub fn new(mut items: Vec<Item>) -> Self {
        items.truncate(12);
        Self { items }
    }
}
impl Marshal for SCSoldItemListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.items.len() as i32);
        for item in &self.items {
            item.write(s);
        }
    }
}

// -------------------------------------------------------------------------------------------
// CSBuyPriestBuffPacket / CSBuyCoinItemPacket / CSSellBackpackGoodsPacket
// -------------------------------------------------------------------------------------------

/// `CSBuyPriestBuffPacket` (purchase the selected priest blessing).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSBuyPriestBuffPacket {
    pub priest_buff_id: u32,
    pub npc_unit_id: u32,
}

impl CSBuyPriestBuffPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            priest_buff_id: s.read_u32()?,
            npc_unit_id: s.read_bc()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        if let Some(player) = &conn.active_char {
            crate::systems::skills::priest::buy(
                server,
                player,
                self.priest_buff_id,
                self.npc_unit_id,
            );
        }
    }
}
sync_client_packet!(CSBuyPriestBuffPacket);

/// `CSBuyCoinItemPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSBuyCoinItemPacket {
    pub obj_id: u32,
    pub id: u32,
}

impl CSBuyCoinItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            id: s.read_u32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        tracing::trace!("BuyCoinItem, objId: {}, id: {}", self.obj_id, self.id);
        let doodad = parent_world(server, &p).and_then(|w| w.doodad(self.obj_id));
        let Some(doodad) = doodad else {
            tracing::warn!("BuyCoinItem, no such doodad objId: {}", self.obj_id);
            return;
        };
        server
            .systems
            .economy
            .store
            .hooks()
            .buy_coin_item(server, &p, &doodad, self.id);
    }
}
sync_client_packet!(CSBuyCoinItemPacket);

/// `CSSellBackpackGoodsPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSellBackpackGoodsPacket {
    pub obj_id: u32,
}

impl CSSellBackpackGoodsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        let base_price = super::specialty::sell_specialty(server, &p, self.obj_id);
        tracing::warn!(
            "CSSellBackpackGoods, ObjId: {}. BasePrice: {base_price}",
            self.obj_id
        );
    }
}
sync_client_packet!(CSSellBackpackGoodsPacket);

// -------------------------------------------------------------------------------------------
// CSExpandExpertPacket / SCExpertExpandedPacket
// -------------------------------------------------------------------------------------------

/// `CSExpandExpertPacket` (empty struct).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSExpandExpertPacket;

impl CSExpandExpertPacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::debug!("ExpandExpert");
        let Some(p) = active(conn) else { return };
        expand_expert(server, &p);
    }
}
sync_client_packet!(CSExpandExpertPacket);

/// `CharacterActability.ExpandExpert()`.
pub fn expand_expert(server: &GameServer, p: &PlayerRef) {
    let (expanded, vocation) = {
        let c = p.data();
        (c.expanded_expert, c.vocation_point)
    };
    let Some(expand) = server
        .game_data
        .systems
        .economy
        .store
        .expand_expert_limit(expanded)
        .copied()
    else {
        return; // TODO ... send msg error?
    };
    if expand.life_point > vocation {
        inventory::send_error_message(p, error_message::NOT_ENOUGH_EXPAND_ITEM_AND_MONEY, 0, true);
        return;
    }
    let inv = Inv::of(p);
    if expand.item_id != 0
        && expand.item_count != 0
        && !inv.check_items(slot_type::INVENTORY, expand.item_id, expand.item_count)
    {
        inventory::send_error_message(p, error_message::NOT_ENOUGH_EXPAND_ITEM, 0, true);
        return;
    }
    let mut txn = ItemTxn::begin(server);
    if expand.life_point > 0 {
        // C# bug mirrored: the cost is *added* (`ChangeGamePoints(Vocation, LifePoint)`).
        let mut out = txn.out();
        inventory::change_game_points(&mut out, p, game_point_kind::VOCATION, expand.life_point);
    }
    if expand.item_id != 0 && expand.item_count != 0 {
        inv.bag().lock().expect("container poisoned").consume_item(
            task_type::EXPAND_EXPERT,
            expand.item_id,
            expand.item_count,
            None,
            &mut txn.env,
        );
    }
    p.data().expanded_expert = expand.expand_count;
    txn.env.fx.send(
        p.character_id,
        SCExpertExpandedPacket {
            next: expand.expand_count,
        },
    );
}

/// `SCExpertExpandedPacket(next)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCExpertExpandedPacket {
    pub next: u8,
}
packet_def!(
    SCExpertExpandedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpertExpandedPacket
);
impl Marshal for SCExpertExpandedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.next);
    }
}

// -------------------------------------------------------------------------------------------
// CSChangeItemLookPacket / CSConvertItemLookPacket
// -------------------------------------------------------------------------------------------

/// `CSChangeItemLookPacket` (only logged in the 1.2 C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSChangeItemLookPacket {
    pub slot_type1: u8,
    pub slot1: u8,
    pub slot_type2: u8,
    pub slot2: u8,
    pub item_id: u64,
    pub look_id: u64,
}

impl CSChangeItemLookPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        s.read_u8()?;
        let slot_type1 = s.read_u8()?;
        s.read_u8()?;
        let slot1 = s.read_u8()?;
        s.read_u8()?;
        let slot_type2 = s.read_u8()?;
        s.read_u8()?;
        let slot2 = s.read_u8()?;
        Ok(Self {
            slot_type1,
            slot1,
            slot_type2,
            slot2,
            item_id: s.read_u64()?,
            look_id: s.read_u64()?,
        })
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!(
            "ChangeItemLook, ItemId: {}, LookId: {}",
            self.item_id,
            self.look_id
        );
    }
}
sync_client_packet!(CSChangeItemLookPacket);

/// `CSConvertItemLookPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSConvertItemLookPacket {
    pub base_id: u64,
    pub look_id: u64,
}

impl CSConvertItemLookPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            base_id: s.read_u64()?,
            look_id: s.read_u64()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(p) = active(conn) else { return };
        convert_item_look(server, &p, self.base_id, self.look_id);
    }
}
sync_client_packet!(CSConvertItemLookPacket);

/// The body of `CSConvertItemLookPacket.Read`.
pub fn convert_item_look(server: &GameServer, p: &PlayerRef, base_id: u64, look_id: u64) {
    let inv = Inv::of(p);
    let (Some(to_image), Some(image_item)) = (inv.item_by_id(base_id), inv.item_by_id(look_id))
    else {
        inventory::send_error_message(p, error_message::FAILED_TO_USE_ITEM, 0, true);
        return;
    };
    if !to_image.class.is_equip() {
        inventory::send_error_message(
            p,
            error_message::ITEM_LOOK_CONVERT_AS_INVALID_COMBINATION,
            0,
            true,
        );
        return;
    }
    if server
        .game_data
        .items
        .template(to_image.template_id)
        .and_then(|t| t.equip())
        .is_none()
    {
        return;
    }
    let Some(convert) = server
        .game_data
        .systems
        .economy
        .store
        .item_look_convert(to_image.template_id)
        .copied()
    else {
        // C#: template.ItemLookConvert is null (accessories) -> NullReferenceException
        tracing::error!(
            "ConvertItemLook: template {} has no ItemLookConvert",
            to_image.template_id
        );
        return;
    };

    let mut txn = ItemTxn::begin(server);
    // Use powder
    let used = inv.bag().lock().expect("container poisoned").consume_item(
        task_type::SKILL_REAGENTS,
        convert.required_item_id,
        convert.required_item_count,
        None,
        &mut txn.env,
    );
    if used <= 0 {
        // Not enough powder
        // Probably not the correct error, but the client should have already caught this
        txn.env.fx.send(
            p.character_id,
            crate::packets::world::SCErrorMsgPacket::new(
                error_message::NOT_ENOUGH_REQUIRED_ITEM,
                convert.required_item_id,
                true,
            ),
        );
        return;
    }

    // Update item looks
    let updated = inv.holder_of(base_id).and_then(|c| {
        let mut g = c.lock().expect("container poisoned");
        let item = g.get_item_by_id_mut(base_id)?;
        item.details.image_item_template_id = image_item.template_id;
        item.dirty = true;
        Some(item.clone())
    });
    if let Some(item) = updated {
        txn.env.fx.send(
            p.character_id,
            SCItemTaskSuccessPacket {
                action: task_type::CONVERT_ITEM_LOOK,
                tasks: vec![ItemTask::update(&item)],
                force_remove: Vec::new(),
            },
        );
    }

    // Remove image item
    if let Some(holder) = inv.holder_of(look_id) {
        holder.lock().expect("container poisoned").remove_item_task(
            task_type::SKILL_REAGENTS,
            look_id,
            true,
            &mut txn.env,
        );
    }
}
